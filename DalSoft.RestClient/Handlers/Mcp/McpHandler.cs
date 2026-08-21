using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DalSoft.RestClient.Serialization;

namespace DalSoft.RestClient.Handlers.Mcp
{
    /// <summary>
    /// Talks to an MCP server using the Streamable HTTP transport https://modelcontextprotocol.io/specification/2025-06-18/basic/transports
    /// Wraps the posted object in a JSON-RPC envelope, lazily initializes the session, tracks Mcp-Session-Id, reads SSE responses and unwraps the JSON-RPC result so the RestClient response is just the result.
    /// </summary>
    public class McpHandler : DelegatingHandler
    {
        public const string SessionIdHeader = "Mcp-Session-Id";
        public const string ProtocolVersionHeader = "MCP-Protocol-Version";
        internal const string EventStreamMediaType = "text/event-stream";
        private const string InitializeMethod = "initialize";
        private const string InitializedNotification = "notifications/initialized";

        private readonly McpHandlerOptions _options;

        public McpSession Session { get; }

        public McpHandler() : this(null, null) { }

        public McpHandler(McpHandlerOptions options) : this(options, null) { }

        public McpHandler(McpHandlerOptions options, McpSession session)
        {
            _options = options ?? new McpHandlerOptions();
            Session = session ?? new McpSession();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post) // GET (listen) and DELETE (terminate session) pass straight through with the session headers
            {
                SetHeaders(request);
                var passThroughResponse = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                CaptureSessionId(passThroughResponse);
                return passThroughResponse;
            }

            var message = JsonRpcMessage.FromContent(request.GetContent(), Session, GetSerializer(request));

            if (message.Method == InitializeMethod) // Caller is driving initialization themselves, complete the handshake for them
            {
                await Session.InitializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try { return await Initialize(request, message, cancellationToken).ConfigureAwait(false); }
                finally { Session.InitializeLock.Release(); }
            }

            await EnsureInitialized(request, cancellationToken).ConfigureAwait(false);

            var response = await SendJsonRpc(request, message, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound && Session.SessionId != null) // Server expired the session, start a new one and resend once
            {
                response.Dispose();
                Session.Reset();
                await EnsureInitialized(request, cancellationToken).ConfigureAwait(false);
                response = await SendJsonRpc(CloneRequest(request), message, cancellationToken).ConfigureAwait(false);
            }

            return response;
        }

        private async Task EnsureInitialized(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Session.IsInitialized) return;

            await Session.InitializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Session.IsInitialized) return;

                var initializeParams = new
                {
                    protocolVersion = _options.ProtocolVersion,
                    capabilities = _options.Capabilities ?? new { },
                    clientInfo = new { name = _options.ClientName, version = _options.ClientVersion }
                };

                var message = JsonRpcMessage.Create(InitializeMethod, initializeParams, Session, GetSerializer(request));

                using (var response = await Initialize(CloneRequest(request), message, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new McpException($"MCP initialize failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }
            }
            finally
            {
                Session.InitializeLock.Release();
            }
        }

        /// <summary>Sends initialize and on success notifications/initialized, caller must hold InitializeLock</summary>
        private async Task<HttpResponseMessage> Initialize(HttpRequestMessage request, JsonRpcMessage message, CancellationToken cancellationToken)
        {
            Session.Reset();

            var response = await SendJsonRpc(request, message, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var initialized = JsonRpcMessage.Create(InitializedNotification, null, Session, GetSerializer(request));
                using (await SendJsonRpc(CloneRequest(request), initialized, cancellationToken).ConfigureAwait(false)) { }

                Session.IsInitialized = true;
            }

            return response;
        }

        private async Task<HttpResponseMessage> SendJsonRpc(HttpRequestMessage request, JsonRpcMessage message, CancellationToken cancellationToken)
        {
            request.Content?.Dispose();
            request.Content = new ByteArrayContent(message.Body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(Config.JsonMediaType);
            SetHeaders(request);

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            CaptureSessionId(response);

            if (!response.IsSuccessStatusCode || message.IsNotification || response.StatusCode == HttpStatusCode.Accepted || response.Content == null)
                return response; // Nothing to unwrap, the caller can inspect the HttpResponseMessage

            JsonDocument document;
            var mediaType = response.Content.Headers.ContentType?.MediaType;

            if (string.Equals(mediaType, EventStreamMediaType, StringComparison.OrdinalIgnoreCase))
            {
                document = await ReadResponseFromEventStream(request, response, message.Id, cancellationToken).ConfigureAwait(false);

                if (document == null)
                    throw new McpException($"MCP server closed the event stream without sending a response for {message.Method} (id {message.Id})");
            }
            else
            {
                var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (body.Length == 0) return response;
                document = JsonDocument.Parse(body);
            }

            using (document)
            {
                if (message.Method == InitializeMethod)
                    CaptureInitializeResult(document.RootElement);

                ReplaceContent(response, Unwrap(document.RootElement));
            }

            return response;
        }

        private async Task<JsonDocument> ReadResponseFromEventStream(HttpRequestMessage request, HttpResponseMessage response, string expectedId, CancellationToken cancellationToken)
        {
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            using (stream)
            using (cancellationToken.Register(() => stream.Dispose()))
            {
                var reader = new ServerSentEventReader(stream);
                ServerSentEvent serverSentEvent;

                while ((serverSentEvent = await reader.ReadAsync().ConfigureAwait(false)) != null)
                {
                    if (serverSentEvent.Id != null) Session.LastEventId = serverSentEvent.Id;
                    if (string.IsNullOrWhiteSpace(serverSentEvent.Data)) continue;

                    JsonDocument document;
                    try { document = JsonDocument.Parse(serverSentEvent.Data); }
                    catch (JsonException) { continue; } // Not JSON-RPC, ignore

                    var root = document.RootElement;
                    var hasId = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null;
                    var hasMethod = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String;

                    if (hasId && !hasMethod) // Response
                    {
                        if (root.GetProperty("id").GetRawText() == expectedId)
                            return document;
                    }
                    else if (hasMethod && !hasId) // Notification e.g. notifications/progress
                    {
                        var @params = root.TryGetProperty("params", out var paramsElement) ? paramsElement.Clone() : default;
                        _options.OnNotification?.Invoke(root.GetProperty("method").GetString(), @params);
                    }
                    else if (hasMethod) // Server to client request e.g. sampling, not supported so tell the server
                    {
                        await RejectServerRequest(request, root.GetProperty("id").GetRawText(), cancellationToken).ConfigureAwait(false);
                    }

                    document.Dispose();
                }
            }

            return null;
        }

        private async Task RejectServerRequest(HttpRequestMessage request, string id, CancellationToken cancellationToken)
        {
            var body = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}";
            var reply = CloneRequest(request);
            reply.Content = new StringContent(body, Encoding.UTF8, Config.JsonMediaType);
            SetHeaders(reply);

            try { using (await base.SendAsync(reply, cancellationToken).ConfigureAwait(false)) { } }
            catch (HttpRequestException) { } // Best effort, the server will time the request out
        }

        private static string Unwrap(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number ? codeElement.GetInt32() : 0;
                var errorMessage = error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String ? messageElement.GetString() : "MCP server returned an error";
                JsonElement? data = error.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : (JsonElement?)null;

                throw new McpException(code, errorMessage, data);
            }

            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result) ? result.GetRawText() : root.GetRawText();
        }

        private static void ReplaceContent(HttpResponseMessage response, string json)
        {
            response.Content?.Dispose();
            response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(Config.JsonMediaType) { CharSet = "utf-8" };
        }

        private void CaptureInitializeResult(JsonElement root)
        {
            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return;

            if (result.TryGetProperty("protocolVersion", out var protocolVersion) && protocolVersion.ValueKind == JsonValueKind.String)
                Session.NegotiatedProtocolVersion = protocolVersion.GetString();

            if (result.TryGetProperty("serverInfo", out var serverInfo)) Session.ServerInfo = serverInfo.Clone();
            if (result.TryGetProperty("capabilities", out var capabilities)) Session.ServerCapabilities = capabilities.Clone();
        }

        private void CaptureSessionId(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues(SessionIdHeader, out var values))
            {
                var sessionId = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(sessionId)) Session.SessionId = sessionId;
            }
        }

        private void SetHeaders(HttpRequestMessage request)
        {
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(Config.JsonMediaType));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(EventStreamMediaType));

            request.Headers.Remove(SessionIdHeader);
            if (Session.SessionId != null) request.Headers.TryAddWithoutValidation(SessionIdHeader, Session.SessionId);

            request.Headers.Remove(ProtocolVersionHeader);
            if (Session.NegotiatedProtocolVersion != null) request.Headers.TryAddWithoutValidation(ProtocolVersionHeader, Session.NegotiatedProtocolVersion);
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            foreach (var property in request.GetStateBag())
                clone.GetStateBag()[property.Key] = property.Value;

            return clone;
        }

        private static IJsonSerializer GetSerializer(HttpRequestMessage request) => request.GetConfig()?.JsonSerializer ?? SystemTextJsonSerializer.Default;
    }
}
