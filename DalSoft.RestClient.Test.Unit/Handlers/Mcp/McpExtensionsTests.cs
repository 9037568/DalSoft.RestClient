using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DalSoft.RestClient.Handlers.Mcp;
using NUnit.Framework;

namespace DalSoft.RestClient.Test.Unit.Handlers.Mcp
{
    [TestFixture]
    public class McpExtensionsTests
    {
        private FakeMcpServer _server;
        private IRestClient _client;

        [SetUp]
        public void SetUp()
        {
            _server = new FakeMcpServer();
            _server.Methods["resources/list"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"resources\":[]}}");
            _server.Methods["resources/read"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"contents\":[]}}");
            _server.Methods["prompts/list"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"prompts\":[]}}");
            _server.Methods["prompts/get"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"messages\":[]}}");
            _server.Methods["custom/method"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"custom\":true}}");

            _client = new RestClient("http://mcp.test/mcp", new Config().UseMcpHandler().UseUnitTestHandler(_server.Handle));
        }

        private JsonElement LastBody() => _server.Bodies.Last().RootElement;

        [Test]
        public async Task Ping_WhenCalled_PostsPingWithoutParams()
        {
            await _client.Ping();

            Assert.That(LastBody().GetProperty("method").GetString(), Is.EqualTo("ping"));
            Assert.False(LastBody().TryGetProperty("params", out _));
        }

        [Test]
        public async Task ListTools_NoCursor_PostsToolsListWithoutParams()
        {
            await _client.ListTools();

            Assert.That(LastBody().GetProperty("method").GetString(), Is.EqualTo("tools/list"));
            Assert.False(LastBody().TryGetProperty("params", out _));
        }

        [Test]
        public async Task ListTools_WithCursor_PostsCursor()
        {
            await _client.ListTools("next-page");

            Assert.That(LastBody().GetProperty("params").GetProperty("cursor").GetString(), Is.EqualTo("next-page"));
        }

        [Test]
        public async Task CallTool_WithArguments_PostsNameAndArguments()
        {
            var result = await _client.CallTool("echo", new { message = "hi", count = 2 });

            var @params = LastBody().GetProperty("params");
            Assert.That(LastBody().GetProperty("method").GetString(), Is.EqualTo("tools/call"));
            Assert.That(@params.GetProperty("name").GetString(), Is.EqualTo("echo"));
            Assert.That(@params.GetProperty("arguments").GetProperty("message").GetString(), Is.EqualTo("hi"));
            Assert.That(@params.GetProperty("arguments").GetProperty("count").GetInt32(), Is.EqualTo(2));
            Assert.That((string)result.content[0].type, Is.EqualTo("text"));
        }

        [Test]
        public async Task CallTool_NoArguments_OmitsArguments()
        {
            await _client.CallTool("echo");

            Assert.False(LastBody().GetProperty("params").TryGetProperty("arguments", out _));
        }

        [Test]
        public async Task CallTool_Generic_ReturnsStronglyTyped()
        {
            var result = await _client.CallTool<CallToolResult>("echo");

            Assert.That(result.content.Single().text, Is.EqualTo("hi"));
        }

        [Test]
        public async Task ListResources_WhenCalled_PostsResourcesList()
        {
            await _client.ListResources();

            Assert.That(LastBody().GetProperty("method").GetString(), Is.EqualTo("resources/list"));
        }

        [Test]
        public async Task ReadResource_WhenCalled_PostsUri()
        {
            await _client.ReadResource("file:///readme.md");

            Assert.That(LastBody().GetProperty("method").GetString(), Is.EqualTo("resources/read"));
            Assert.That(LastBody().GetProperty("params").GetProperty("uri").GetString(), Is.EqualTo("file:///readme.md"));
        }

        [Test]
        public async Task ListPrompts_WhenCalled_PostsPromptsList()
        {
            await _client.ListPrompts("c1");

            Assert.That(LastBody().GetProperty("method").GetString(), Is.EqualTo("prompts/list"));
            Assert.That(LastBody().GetProperty("params").GetProperty("cursor").GetString(), Is.EqualTo("c1"));
        }

        [Test]
        public async Task GetPrompt_WithArguments_PostsNameAndArguments()
        {
            await _client.GetPrompt("summarize", new { text = "abc" });

            Assert.That(LastBody().GetProperty("method").GetString(), Is.EqualTo("prompts/get"));
            Assert.That(LastBody().GetProperty("params").GetProperty("name").GetString(), Is.EqualTo("summarize"));
            Assert.That(LastBody().GetProperty("params").GetProperty("arguments").GetProperty("text").GetString(), Is.EqualTo("abc"));
        }

        [Test]
        public async Task McpRequest_CustomMethod_PostsMethodAndParams()
        {
            var result = await _client.McpRequest("custom/method", new { any = "thing" });

            Assert.That(LastBody().GetProperty("method").GetString(), Is.EqualTo("custom/method"));
            Assert.That(LastBody().GetProperty("params").GetProperty("any").GetString(), Is.EqualTo("thing"));
            Assert.True((bool)result.custom);
        }

        [Test]
        public async Task CallToolJson_TextContentIsJson_ParsesText()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"symbol\\\":\\\"MSFT\\\",\\\"price\\\":481.5}\"}],\"isError\":false}}");

            var quote = await _client.CallToolJson<Quote>("get_quote", new { symbols = new[] { "MSFT" } });

            Assert.That(quote.symbol, Is.EqualTo("MSFT"));
            Assert.That(quote.price, Is.EqualTo(481.5));
            Assert.That(LastBody().GetProperty("params").GetProperty("arguments").GetProperty("symbols")[0].GetString(), Is.EqualTo("MSFT"));
        }

        [Test]
        public async Task CallToolJson_StructuredContentPresent_PrefersStructuredContentOverText()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"not json\"}],\"structuredContent\":{\"symbol\":\"AAPL\",\"price\":1}}}");

            var quote = await _client.CallToolJson<Quote>("get_quote");

            Assert.That(quote.symbol, Is.EqualTo("AAPL"));
        }

        [Test]
        public async Task CallToolJson_ArrayResult_Deserializes()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"[{\\\"symbol\\\":\\\"A\\\"},{\\\"symbol\\\":\\\"B\\\"}]\"}]}}");

            var quotes = await _client.CallToolJson<Quote[]>("get_quote");

            Assert.That(quotes.Select(q => q.symbol), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public async Task CallToolJson_FirstBlockIsNotText_UsesFirstTextBlock()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"image\",\"data\":\"abc\",\"mimeType\":\"image/png\"},{\"type\":\"text\",\"text\":\"{\\\"symbol\\\":\\\"IMG\\\"}\"}]}}");

            var quote = await _client.CallToolJson<Quote>("get_quote");

            Assert.That(quote.symbol, Is.EqualTo("IMG"));
        }

        [Test]
        public void CallToolJson_IsErrorTrue_ThrowsMcpExceptionWithTextAsMessage()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"Tool get_quote not found\"}],\"isError\":true}}");

            var exception = Assert.ThrowsAsync<McpException>(async () => await _client.CallToolJson<Quote>("get_quote"));

            Assert.That(exception.Message, Is.EqualTo("Tool get_quote not found"));
            Assert.That(exception.Code, Is.EqualTo(0));
        }

        [Test]
        public void CallToolJson_NoTextContent_ThrowsMcpException()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"image\",\"data\":\"abc\",\"mimeType\":\"image/png\"}]}}");

            var exception = Assert.ThrowsAsync<McpException>(async () => await _client.CallToolJson<Quote>("get_tiny_image"));

            Assert.That(exception.Message, Does.Contain("no structuredContent and no text content"));
        }

        [Test]
        public void CallToolJson_TextIsNotJson_ThrowsJsonException()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"Hello world\"}]}}");

            Assert.ThrowsAsync<JsonException>(async () => await _client.CallToolJson<Quote>("echo"));
        }

        [Test]
        public void CallToolJson_JsonRpcError_ThrowsMcpExceptionFromHandler()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32602,\"message\":\"Invalid params\"}}");

            var exception = Assert.ThrowsAsync<McpException>(async () => await _client.CallToolJson<Quote>("get_quote"));

            Assert.That(exception.Code, Is.EqualTo(-32602));
        }

        [Test]
        public async Task CallToolJson_WithJsonSerializerOptions_OptionsAreUsed()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"SYMBOL\\\":\\\"MSFT\\\"}\"}]}}");

            var stock = await _client.CallToolJson<Quote>("get_quote");
            var caseInsensitive = await _client.CallToolJson<Quote>("get_quote", null, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Null(stock.symbol); // Stock System.Text.Json is case sensitive
            Assert.That(caseInsensitive.symbol, Is.EqualTo("MSFT"));
        }

        [Test]
        public async Task CallToolJsonDynamic_TextContentIsJsonObject_ReturnsDynamicObject()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"symbol\\\":\\\"MSFT\\\",\\\"price\\\":481.5,\\\"nested\\\":{\\\"ok\\\":true}}\"}]}}");

            var quote = await _client.CallToolJson("get_quote", new { symbols = new[] { "MSFT" } });

            Assert.That((string)quote.symbol, Is.EqualTo("MSFT"));
            Assert.That(quote.price > 481); // No GetDouble(), just a number
            Assert.That((double)quote.price, Is.EqualTo(481.5));
            Assert.True((bool)quote.nested.ok);
        }

        [Test]
        public async Task CallToolJsonDynamic_TextContentIsJsonArray_IsIndexableCountableAndEnumerable()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"[{\\\"symbol\\\":\\\"A\\\"},{\\\"symbol\\\":\\\"B\\\"}]\"}]}}");

            var quotes = await _client.CallToolJson("get_quote");

            Assert.That(quotes.Count, Is.EqualTo(2));
            Assert.That((string)quotes[1].symbol, Is.EqualTo("B"));
            IEnumerable<dynamic> enumerable = quotes;
            Assert.That(enumerable.Select(quote => (string)quote.symbol), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public async Task CallToolJsonDynamic_StructuredContentPresent_ReturnsStructuredContent()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"not json\"}],\"structuredContent\":{\"symbol\":\"AAPL\",\"price\":1}}}");

            var quote = await _client.CallToolJson("get_quote");

            Assert.That((string)quote.symbol, Is.EqualTo("AAPL"));
        }

        [Test]
        public void CallToolJsonDynamic_IsErrorTrue_ThrowsMcpExceptionWithTextAsMessage()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"Tool get_quote not found\"}],\"isError\":true}}");

            var exception = Assert.ThrowsAsync<McpException>(async () => await _client.CallToolJson("get_quote"));

            Assert.That(exception.Message, Is.EqualTo("Tool get_quote not found"));
        }

        [Test]
        public void CallToolJsonDynamic_TextIsNotJson_ThrowsMcpException()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"Hello world\"}]}}");

            var exception = Assert.ThrowsAsync<McpException>(async () => await _client.CallToolJson("echo"));

            Assert.That(exception.Message, Does.Contain("not JSON"));
        }

        [Test]
        public void CallToolJsonDynamic_NoTextContent_ThrowsMcpException()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"image\",\"data\":\"abc\",\"mimeType\":\"image/png\"}]}}");

            Assert.ThrowsAsync<McpException>(async () => await _client.CallToolJson("get_tiny_image"));
        }

        [Test]
        public async Task CallToolJsonDynamic_UsingNewtonsoftJson_StillWorks()
        {
            _server.Methods["tools/call"] = (id, p) => FakeMcpServer.Json("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"symbol\\\":\\\"MSFT\\\"}\"}]}}");
            IRestClient client = new RestClient("http://mcp.test/mcp", new Config().UseNewtonsoftJson().UseMcpHandler().UseUnitTestHandler(_server.Handle));

            var quote = await client.CallToolJson("get_quote");

            Assert.That((string)quote.symbol, Is.EqualTo("MSFT"));
        }

        public class Quote { public string symbol { get; set; } public double price { get; set; } }
        public class CallToolResult { public Content[] content { get; set; } }
        public class Content { public string type { get; set; } public string text { get; set; } }
    }
}
