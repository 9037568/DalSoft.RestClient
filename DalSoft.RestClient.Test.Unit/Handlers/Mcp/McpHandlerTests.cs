using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DalSoft.RestClient.Handlers.Mcp;
using NUnit.Framework;

namespace DalSoft.RestClient.Test.Unit.Handlers.Mcp
{
    [TestFixture]
    public class McpHandlerTests
    {
        private const string McpEndpoint = "http://mcp.test/mcp";

        private static IRestClient CreateClient(FakeMcpServer server, McpHandlerOptions options = null, McpSession session = null, Func<Config, Config> configure = null)
        {
            var config = new Config();
            config = configure?.Invoke(config) ?? config;

            return new RestClient(McpEndpoint, config.UseMcpHandler(options, session).UseUnitTestHandler(server.Handle));
        }

        [Test]
        public async Task Send_FirstRequest_LazilyInitializesThenSendsRequest()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            await client.ListTools();

            var methods = server.Bodies.Select(_ => _.RootElement.GetProperty("method").GetString()).ToList();
            Assert.That(methods, Is.EqualTo(new[] { "initialize", "notifications/initialized", "tools/list" }));
        }

        [Test]
        public async Task Send_Initialize_SendsProtocolVersionCapabilitiesAndClientInfo()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server, new McpHandlerOptions { ClientName = "MyClient", ClientVersion = "2.0", Capabilities = new { roots = new { listChanged = true } } });

            await client.Ping();

            var initialize = server.BodiesFor("initialize").Single().RootElement;
            Assert.That(initialize.GetProperty("jsonrpc").GetString(), Is.EqualTo("2.0"));
            Assert.That(initialize.GetProperty("params").GetProperty("protocolVersion").GetString(), Is.EqualTo(McpHandlerOptions.DefaultProtocolVersion));
            Assert.That(initialize.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString(), Is.EqualTo("MyClient"));
            Assert.That(initialize.GetProperty("params").GetProperty("clientInfo").GetProperty("version").GetString(), Is.EqualTo("2.0"));
            Assert.True(initialize.GetProperty("params").GetProperty("capabilities").GetProperty("roots").GetProperty("listChanged").GetBoolean());
        }

        [Test]
        public async Task Send_InitializedNotification_HasNoIdAndServerReturns202()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            await client.Ping();

            var initialized = server.BodiesFor("notifications/initialized").Single().RootElement;
            Assert.False(initialized.TryGetProperty("id", out _));
        }

        [Test]
        public async Task Send_Envelope_AddsJsonRpcAndIncrementingId()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            await client.Ping();
            await client.Ping();

            var ids = server.Bodies.Where(body => body.RootElement.TryGetProperty("id", out _)).Select(body => body.RootElement.GetProperty("id").GetInt64()).ToList();
            Assert.That(ids.Count, Is.EqualTo(3)); // initialize, ping, ping
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Is.All.GreaterThan(0));
            Assert.True(server.Bodies.All(_ => _.RootElement.GetProperty("jsonrpc").GetString() == "2.0"));
        }

        [Test]
        public async Task Send_Request_SetsAcceptHeaderForJsonAndEventStream()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            await client.Ping();

            foreach (var request in server.Requests)
            {
                var accept = request.Headers.Accept.Select(_ => _.MediaType).ToList();
                Assert.That(accept, Is.EquivalentTo(new[] { "application/json", "text/event-stream" }));
                Assert.That(request.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
            }
        }

        [Test]
        public async Task Send_AfterInitialize_SessionIdAndProtocolVersionHeadersAreSent()
        {
            var server = new FakeMcpServer();
            var session = new McpSession();
            var client = CreateClient(server, session: session);

            await client.ListTools();

            var initializeRequest = server.Requests[0];
            Assert.False(initializeRequest.Headers.Contains(McpHandler.SessionIdHeader));
            Assert.False(initializeRequest.Headers.Contains(McpHandler.ProtocolVersionHeader));

            foreach (var request in server.Requests.Skip(1))
            {
                Assert.That(request.Headers.GetValues(McpHandler.SessionIdHeader).Single(), Is.EqualTo(FakeMcpServer.SessionId));
                Assert.That(request.Headers.GetValues(McpHandler.ProtocolVersionHeader).Single(), Is.EqualTo(FakeMcpServer.ProtocolVersion));
            }

            Assert.True(session.IsInitialized);
            Assert.That(session.SessionId, Is.EqualTo(FakeMcpServer.SessionId));
            Assert.That(session.NegotiatedProtocolVersion, Is.EqualTo(FakeMcpServer.ProtocolVersion));
            Assert.That(session.ServerInfo.Value.GetProperty("name").GetString(), Is.EqualTo("fake"));
            Assert.True(session.ServerCapabilities.Value.TryGetProperty("tools", out _));
        }

        [Test]
        public async Task Send_StatelessServer_NoSessionHeaderIsSent()
        {
            var server = new FakeMcpServer { Stateless = true };
            var session = new McpSession();
            var client = CreateClient(server, session: session);

            await client.ListTools();

            Assert.Null(session.SessionId);
            Assert.True(server.Requests.All(_ => !_.Headers.Contains(McpHandler.SessionIdHeader)));
        }

        [Test]
        public async Task Send_ConcurrentFirstRequests_InitializesOnlyOnce()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.Ping()));

            Assert.That(server.CountOf("initialize"), Is.EqualTo(1));
            Assert.That(server.CountOf("notifications/initialized"), Is.EqualTo(1));
            Assert.That(server.CountOf("ping"), Is.EqualTo(10));
        }

        [Test]
        public async Task Send_JsonResponse_UnwrapsResult()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            var result = await client.ListTools();

            Assert.That((string)result.tools[0].name, Is.EqualTo("echo"));
        }

        [Test]
        public async Task Send_JsonResponse_UnwrapsResultToStronglyTypedObject()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            var result = await client.ListTools<ToolsListResult>();

            Assert.That(result.tools.Single().name, Is.EqualTo("echo"));
        }

        [Test]
        public async Task Send_SseResponse_UnwrapsResultAndRaisesNotifications()
        {
            var server = new FakeMcpServer();
            var notifications = new List<KeyValuePair<string, JsonElement>>();
            server.Methods["tools/call"] = (id, @params) => FakeMcpServer.Sse(
                "event: message\n" +
                "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progress\":50}}\n\n" +
                ": keep-alive\n\n" +
                "id: evt-7\n" +
                "data: {\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"streamed\"}]}}\n\n");

            var session = new McpSession();
            var client = CreateClient(server, new McpHandlerOptions { OnNotification = (method, @params) => notifications.Add(new KeyValuePair<string, JsonElement>(method, @params)) }, session);

            var result = await client.CallTool("echo", new { message = "hi" });

            Assert.That((string)result.content[0].text, Is.EqualTo("streamed"));
            Assert.That(notifications.Single().Key, Is.EqualTo("notifications/progress"));
            Assert.That(notifications.Single().Value.GetProperty("progress").GetInt32(), Is.EqualTo(50));
            Assert.That(session.LastEventId, Is.EqualTo("evt-7"));
        }

        [Test]
        public async Task Send_SseResponseWithOtherIds_WaitsForMatchingId()
        {
            var server = new FakeMcpServer();
            server.Methods["tools/call"] = (id, @params) => FakeMcpServer.Sse(
                "data: {\"jsonrpc\":\"2.0\",\"id\":999,\"result\":{\"wrong\":true}}\n\n" +
                "data: {\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"right\":true}}\n\n");

            var client = CreateClient(server);

            var result = await client.CallTool("echo");

            Assert.True((bool)result.right);
        }

        [Test]
        public void Send_SseStreamClosesWithoutResponse_ThrowsMcpException()
        {
            var server = new FakeMcpServer();
            server.Methods["tools/call"] = (id, @params) => FakeMcpServer.Sse("data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{}}\n\n");

            var client = CreateClient(server);

            var exception = Assert.ThrowsAsync<McpException>(async () => await client.CallTool("echo"));
            Assert.That(exception.Code, Is.EqualTo(0));
            Assert.That(exception.Message, Does.Contain("tools/call"));
        }

        [Test]
        public async Task Send_ServerToClientRequestOnStream_IsRejectedWithMethodNotFound()
        {
            var server = new FakeMcpServer();
            server.Methods["tools/call"] = (id, @params) => FakeMcpServer.Sse(
                "data: {\"jsonrpc\":\"2.0\",\"id\":\"srv-1\",\"method\":\"sampling/createMessage\",\"params\":{}}\n\n" +
                "data: {\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"ok\":true}}\n\n");

            var client = CreateClient(server);

            var result = await client.CallTool("echo");

            Assert.True((bool)result.ok);
            var rejection = server.Bodies.Single(body => body.RootElement.TryGetProperty("error", out _)).RootElement;
            Assert.That(rejection.GetProperty("id").GetString(), Is.EqualTo("srv-1"));
            Assert.That(rejection.GetProperty("error").GetProperty("code").GetInt32(), Is.EqualTo(-32601));
        }

        [Test]
        public void Send_JsonRpcError_ThrowsMcpExceptionWithCodeMessageAndData()
        {
            var server = new FakeMcpServer();
            server.Methods["tools/call"] = (id, @params) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32602,\"message\":\"Invalid params\",\"data\":{\"field\":\"name\"}}}");

            var client = CreateClient(server);

            var exception = Assert.ThrowsAsync<McpException>(async () => await client.CallTool("echo"));
            Assert.That(exception.Code, Is.EqualTo(-32602));
            Assert.That(exception.Message, Is.EqualTo("Invalid params"));
            Assert.That(exception.Data.Value.GetProperty("field").GetString(), Is.EqualTo("name"));
        }

        [Test]
        public void Send_JsonRpcErrorOnSse_ThrowsMcpException()
        {
            var server = new FakeMcpServer();
            server.Methods["tools/call"] = (id, @params) => FakeMcpServer.Sse("data: {\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32000,\"message\":\"Boom\"}}\n\n");

            var client = CreateClient(server);

            var exception = Assert.ThrowsAsync<McpException>(async () => await client.CallTool("echo"));
            Assert.That(exception.Code, Is.EqualTo(-32000));
        }

        [Test]
        public void Send_InitializeFailsWithHttpError_ThrowsMcpException()
        {
            var server = new FakeMcpServer();
            server.Methods["initialize"] = (id, @params) => new HttpResponseMessage(HttpStatusCode.InternalServerError);

            var session = new McpSession();
            var client = CreateClient(server, session: session);

            var exception = Assert.ThrowsAsync<McpException>(async () => await client.Ping());
            Assert.That(exception.Message, Does.Contain("500"));
            Assert.False(session.IsInitialized);
        }

        [Test]
        public async Task Send_SessionExpired404_ReinitializesAndResendsOnce()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);
            await client.Ping();

            server.Expired = true;
            var result = await client.ListTools();

            Assert.That((string)result.tools[0].name, Is.EqualTo("echo"));
            Assert.That(server.CountOf("initialize"), Is.EqualTo(2));
            Assert.That(server.CountOf("tools/list"), Is.EqualTo(2));
        }

        [Test]
        public async Task Send_Notification_ReturnsAcceptedWithNoBody()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            var result = await client.McpRequest("notifications/cancelled", new { requestId = 1 });

            HttpResponseMessage response = result;
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.False(server.BodiesFor("notifications/cancelled").Single().RootElement.TryGetProperty("id", out _));
        }

        [Test]
        public async Task Send_UserSuppliedId_IsHonoured()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            var result = await client.Post(new { id = "custom-id", method = "ping" });

            Assert.That(server.BodiesFor("ping").Single().RootElement.GetProperty("id").GetString(), Is.EqualTo("custom-id"));
            HttpResponseMessage response = result;
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task Send_ManualInitialize_CompletesHandshakeAndReturnsUnwrappedResult()
        {
            var server = new FakeMcpServer();
            var session = new McpSession();
            var client = CreateClient(server, session: session);

            var result = await client.McpRequest("initialize", new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "manual", version = "1" } });
            await client.Ping();

            Assert.That((string)result.serverInfo.name, Is.EqualTo("fake"));
            Assert.That(server.CountOf("initialize"), Is.EqualTo(1));
            Assert.That(server.CountOf("notifications/initialized"), Is.EqualTo(1));
            Assert.That(server.BodiesFor("initialize").Single().RootElement.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString(), Is.EqualTo("manual"));
            Assert.True(session.IsInitialized);
        }

        [Test]
        public async Task Send_Delete_PassesThroughWithSessionHeader()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);
            await client.Ping();

            await client.Delete();

            var delete = server.Requests.Single(_ => _.Method == HttpMethod.Delete);
            Assert.That(delete.Headers.GetValues(McpHandler.SessionIdHeader).Single(), Is.EqualTo(FakeMcpServer.SessionId));
        }

        [Test]
        public void Send_PostWithoutBody_ThrowsArgumentException()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server);

            Assert.ThrowsAsync<ArgumentException>(async () => await client.Post());
        }

        [Test]
        public async Task Send_UsingNewtonsoftJson_StillWorks()
        {
            var server = new FakeMcpServer();
            var client = CreateClient(server, configure: config => config.UseNewtonsoftJson());

            var result = await client.CallTool("echo", new { message = "hi" });

            Assert.That((string)result.content[0].text, Is.EqualTo("hi"));
            Assert.That(server.BodiesFor("tools/call").Single().RootElement.GetProperty("params").GetProperty("arguments").GetProperty("message").GetString(), Is.EqualTo("hi"));
        }

        [Test]
        public async Task Send_HttpErrorOnRequest_ReturnsResponseWithoutUnwrapping()
        {
            var server = new FakeMcpServer();
            server.Methods["tools/call"] = (id, @params) => new HttpResponseMessage(HttpStatusCode.InternalServerError);

            var client = CreateClient(server);

            var result = await client.CallTool("echo");

            HttpResponseMessage response = result;
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        }

        public class ToolsListResult
        {
            public List<Tool> tools { get; set; }
        }

        public class Tool
        {
            public string name { get; set; }
            public string description { get; set; }
        }
    }
}
