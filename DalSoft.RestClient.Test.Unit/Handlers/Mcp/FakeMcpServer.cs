using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DalSoft.RestClient.Handlers.Mcp;

namespace DalSoft.RestClient.Test.Unit.Handlers.Mcp
{
    /// <summary>Scripted in-memory MCP server for use with UseUnitTestHandler</summary>
    public class FakeMcpServer
    {
        public const string SessionId = "session-123";
        public const string ProtocolVersion = McpHandlerOptions.DefaultProtocolVersion;

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
        public List<JsonDocument> Bodies { get; } = new List<JsonDocument>();
        public Dictionary<string, Func<string, JsonElement, HttpResponseMessage>> Methods { get; } = new Dictionary<string, Func<string, JsonElement, HttpResponseMessage>>();
        public bool Expired { get; set; }
        public bool Stateless { get; set; }

        public IEnumerable<JsonDocument> BodiesFor(string method) => Bodies.Where(_ => _.RootElement.TryGetProperty("method", out var m) && m.GetString() == method);
        public int CountOf(string method) => BodiesFor(method).Count();

        public FakeMcpServer()
        {
            Methods["initialize"] = (id, @params) => Json($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"protocolVersion\":\"{ProtocolVersion}\",\"capabilities\":{{\"tools\":{{}}}},\"serverInfo\":{{\"name\":\"fake\",\"version\":\"1.0\"}}}}}}");
            Methods["ping"] = (id, @params) => Json($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{}}}}");
            Methods["tools/list"] = (id, @params) => Json($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"tools\":[{{\"name\":\"echo\",\"description\":\"Echoes\"}}]}}}}");
            Methods["tools/call"] = (id, @params) => Json($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":\"hi\"}}]}}}}");
        }

        public HttpResponseMessage Handle(HttpRequestMessage request)
        {
            Requests.Add(request);

            if (request.Method != HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.OK);

            var body = JsonDocument.Parse(request.Content.ReadAsStringAsync().Result);
            Bodies.Add(body);

            var root = body.RootElement;
            var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetRawText() : null;
            var @params = root.TryGetProperty("params", out var paramsElement) ? paramsElement : default;

            if (method == null) // JSON-RPC response from the client
                return new HttpResponseMessage(HttpStatusCode.Accepted);

            if (method != "initialize" && !Stateless)
            {
                var sessionHeader = request.Headers.TryGetValues(McpHandler.SessionIdHeader, out var values) ? values.FirstOrDefault() : null;
                if (sessionHeader == null) return new HttpResponseMessage(HttpStatusCode.BadRequest);
                if (Expired) { Expired = false; return new HttpResponseMessage(HttpStatusCode.NotFound); }
            }

            if (id == null) // Notification
                return new HttpResponseMessage(HttpStatusCode.Accepted);

            if (!Methods.TryGetValue(method, out var handler))
                return Json($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-32601,\"message\":\"Method not found\"}}}}");

            var response = handler(id, @params);

            if (method == "initialize" && !Stateless)
                response.Headers.Add(McpHandler.SessionIdHeader, SessionId);

            return response;
        }

        public static HttpResponseMessage Json(string json) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        public static HttpResponseMessage Sse(string eventStream) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(eventStream, Encoding.UTF8, "text/event-stream")
        };
    }
}
