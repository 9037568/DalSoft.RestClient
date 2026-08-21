using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DalSoft.RestClient.DependencyInjection;
using DalSoft.RestClient.Handlers.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DalSoft.RestClient.Test.Integration
{
    /// <summary>Real HTTP (HttpListener) MCP server so the SSE streaming path is exercised end to end, UnitTestHandler can't do that as it buffers into a MemoryStream</summary>
    [TestFixture]
    public class McpHandlerTests
    {
        private FakeStreamableHttpMcpServer _server;

        [OneTimeSetUp]
        public void OneTimeSetUp() => _server = new FakeStreamableHttpMcpServer();

        [OneTimeTearDown]
        public void OneTimeTearDown() => _server.Dispose();

        [SetUp]
        public void SetUp() => _server.Reset();

        [Test]
        public async Task ListTools_RealHttp_ReturnsTools()
        {
            IRestClient mcp = new RestClient(_server.Endpoint, new Config().UseMcpHandler());

            var result = await mcp.ListTools();

            Assert.That((string)result.tools[0].name, Is.EqualTo("echo"));
            Assert.That(_server.Methods, Is.EqualTo(new[] { "initialize", "notifications/initialized", "tools/list" }));
        }

        [Test]
        public async Task CallTool_SseResponse_UnwrappedResultAndProgressNotificationReceived()
        {
            var notifications = new List<string>();
            IRestClient mcp = new RestClient(_server.Endpoint, new Config().UseMcpHandler(new McpHandlerOptions { OnNotification = (method, @params) => notifications.Add(method) }));

            var result = await mcp.CallTool("echo", new { message = "hello" });

            Assert.That((string)result.content[0].text, Is.EqualTo("hello"));
            Assert.That(notifications, Is.EqualTo(new[] { "notifications/progress" }));
        }

        [Test]
        public async Task Session_Id_RoundTripsOnEveryRequest()
        {
            var session = new McpSession();
            IRestClient mcp = new RestClient(_server.Endpoint, new Config().UseMcpHandler(session: session));

            await mcp.Ping();
            await mcp.Ping();

            Assert.That(session.SessionId, Is.EqualTo(_server.CurrentSessionId));
            Assert.That(_server.SessionHeaders.Skip(1), Is.All.EqualTo(_server.CurrentSessionId)); // Everything after initialize
            Assert.That(_server.ProtocolVersionHeaders.Skip(1), Is.All.EqualTo(McpHandlerOptions.DefaultProtocolVersion));
        }

        [Test]
        public async Task Session404_ReinitialisesAndSucceeds()
        {
            IRestClient mcp = new RestClient(_server.Endpoint, new Config().UseMcpHandler());
            await mcp.Ping();
            var firstSession = _server.CurrentSessionId;

            _server.ExpireSession();
            var result = await mcp.ListTools();

            Assert.That((string)result.tools[0].name, Is.EqualTo("echo"));
            Assert.That(_server.CurrentSessionId, Is.Not.EqualTo(firstSession));
            Assert.That(_server.Methods.Count(_ => _ == "initialize"), Is.EqualTo(2));
        }

        [Test]
        public void ToolCallError_ThrowsMcpException()
        {
            IRestClient mcp = new RestClient(_server.Endpoint, new Config().UseMcpHandler());

            var exception = Assert.ThrowsAsync<McpException>(async () => await mcp.CallTool("does-not-exist"));

            Assert.That(exception.Code, Is.EqualTo(-32602));
            Assert.That(exception.Message, Does.Contain("Unknown tool"));
        }

        [Test]
        public async Task AddRestClientTClient_WithUseMcpHandler_TypedClientTalksToMcpServer()
        {
            var services = new ServiceCollection();
            services.AddRestClient<McpClient>(_server.Endpoint).UseMcpHandler();
            var serviceProvider = services.BuildServiceProvider();

            var echoed = await serviceProvider.GetRequiredService<McpClient>().Echo("typed");
            var echoedAgain = await serviceProvider.GetRequiredService<McpClient>().Echo("again"); // New transient client, same session

            Assert.That(echoed, Is.EqualTo("typed"));
            Assert.That(echoedAgain, Is.EqualTo("again"));
            Assert.That(_server.Methods.Count(_ => _ == "initialize"), Is.EqualTo(1));
        }

        public class McpClient
        {
            private readonly IRestClient _restClient;
            public McpClient(IRestClient restClient) => _restClient = restClient;

            public async Task<string> Echo(string message)
            {
                var result = await _restClient.CallTool("echo", new { message });
                return result.content[0].text;
            }
        }
    }

    public class FakeStreamableHttpMcpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _expired;

        public string Endpoint { get; }
        public string CurrentSessionId { get; private set; }
        public List<string> Methods { get; } = new List<string>();
        public List<string> SessionHeaders { get; } = new List<string>();
        public List<string> ProtocolVersionHeaders { get; } = new List<string>();

        public FakeStreamableHttpMcpServer()
        {
            var port = GetFreePort();
            Endpoint = $"http://localhost:{port}/mcp";
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/mcp/");
            _listener.Start();
            Task.Run(Listen);
        }

        public void Reset()
        {
            Methods.Clear();
            SessionHeaders.Clear();
            ProtocolVersionHeaders.Clear();
            CurrentSessionId = null;
            _expired = false;
        }

        public void ExpireSession() => _expired = true;

        private static int GetFreePort()
        {
            var tcpListener = new TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();
            return port;
        }

        private async Task Listen()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) when (_cancellation.IsCancellationRequested) { return; }

                _ = Task.Run(() => Handle(context));
            }
        }

        private async Task Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                    body = await reader.ReadToEndAsync();

                using (var document = JsonDocument.Parse(body))
                {
                    var root = document.RootElement;
                    var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
                    var id = root.TryGetProperty("id", out var idElement) ? idElement.GetRawText() : null;

                    lock (Methods)
                    {
                        Methods.Add(method);
                        SessionHeaders.Add(request.Headers[McpHandler.SessionIdHeader]);
                        ProtocolVersionHeaders.Add(request.Headers[McpHandler.ProtocolVersionHeader]);
                    }

                    if (!request.AcceptTypes.Contains("application/json") || !request.AcceptTypes.Contains("text/event-stream"))
                    {
                        await WriteJson(response, 406, "{\"error\":\"Accept must include application/json and text/event-stream\"}");
                        return;
                    }

                    if (method == "initialize")
                    {
                        CurrentSessionId = Guid.NewGuid().ToString("N");
                        response.Headers.Add(McpHandler.SessionIdHeader, CurrentSessionId);
                        await WriteJson(response, 200, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"protocolVersion\":\"" + McpHandlerOptions.DefaultProtocolVersion + "\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"FakeStreamableHttpMcpServer\",\"version\":\"1.0\"}}}");
                        return;
                    }

                    var sessionHeader = request.Headers[McpHandler.SessionIdHeader];

                    if (sessionHeader == null) { response.StatusCode = 400; response.Close(); return; }
                    if (_expired || sessionHeader != CurrentSessionId) { _expired = false; response.StatusCode = 404; response.Close(); return; }

                    if (id == null || method == null) { response.StatusCode = 202; response.Close(); return; } // Notification or client response

                    switch (method)
                    {
                        case "ping":
                            await WriteJson(response, 200, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{}}");
                            return;

                        case "tools/list":
                            await WriteJson(response, 200, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"tools\":[{\"name\":\"echo\",\"description\":\"Echoes the message\",\"inputSchema\":{\"type\":\"object\"}}]}}");
                            return;

                        case "tools/call":
                            var @params = root.GetProperty("params");
                            var name = @params.GetProperty("name").GetString();

                            if (name != "echo")
                            {
                                await WriteJson(response, 200, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32602,\"message\":\"Unknown tool: " + name + "\"}}");
                                return;
                            }

                            var message = @params.GetProperty("arguments").GetProperty("message").GetString();

                            response.StatusCode = 200;
                            response.ContentType = "text/event-stream";
                            response.SendChunked = true;

                            await WriteEvent(response, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progressToken\":\"t\",\"progress\":50,\"total\":100}}");
                            await Task.Delay(200); // Prove the client really streams rather than reading a buffered body
                            await WriteEvent(response, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(message) + "}]}}");
                            response.Close();
                            return;

                        default:
                            await WriteJson(response, 200, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}");
                            return;
                    }
                }
            }
            catch (Exception)
            {
                try { response.StatusCode = 500; response.Close(); } catch { /* client went away */ }
            }
        }

        private static async Task WriteJson(HttpListenerResponse response, int statusCode, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }

        private static async Task WriteEvent(HttpListenerResponse response, string data)
        {
            var bytes = Encoding.UTF8.GetBytes("event: message\ndata: " + data + "\n\n");
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            await response.OutputStream.FlushAsync();
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _listener.Stop();
            _listener.Close();
        }
    }
}
