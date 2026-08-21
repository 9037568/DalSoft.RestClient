using System;
using System.Collections.Generic;

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DalSoft.RestClient.DependencyInjection;
using DalSoft.RestClient.Handlers.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DalSoft.RestClient.Test.Integration
{
    /// <summary>Live tests against a public Streamable HTTP MCP server https://mcpservers.org/remote-mcp-servers/yahoo-finance (no auth, SSE responses, stateless)</summary>
    [TestFixture]
    public class YahooFinanceMcpTests
    {
        private const string McpEndpoint = "https://gateway.mcpservers.org/yahoo-finance/mcp";

        private static IRestClient CreateClient(McpSession session = null) =>
            new RestClient(McpEndpoint, new Config().UseMcpHandler(new McpHandlerOptions { ClientName = "DalSoft.RestClient.Test.Integration" }, session));

        [Test]
        public async Task ListTools_YahooFinance_ReturnsGetQuoteTool()
        {
            var session = new McpSession();
            var mcp = CreateClient(session);

            var result = await mcp.ListTools();
            IEnumerable<dynamic> tools = result.tools;

            Assert.That(tools.Select(tool => (string)tool.name), Does.Contain("get_quote"));
            Assert.True(session.IsInitialized);
            Assert.That(session.NegotiatedProtocolVersion, Is.EqualTo(McpHandlerOptions.DefaultProtocolVersion));
            Assert.That(session.ServerInfo?.GetProperty("name").GetString(), Is.EqualTo("yahoo-finance-mcp"));
        }

        [Test]
        public async Task CallTool_GetQuoteForMicrosoft_ReturnsQuoteOverSse()
        {
            var mcp = CreateClient();

            var result = await mcp.CallTool("get_quote", new { symbols = new[] { "MSFT" } });

            Assert.That((bool?)result.isError, Is.Null.Or.False);
            string text = result.content[0].text;
            using (var quotes = JsonDocument.Parse(text))
            {
                var quote = quotes.RootElement[0];
                Assert.That(quote.GetProperty("symbol").GetString(), Is.EqualTo("MSFT"));
                Assert.That(quote.GetProperty("shortName").GetString(), Does.Contain("Microsoft"));
                Assert.That(quote.GetProperty("currency").GetString(), Is.EqualTo("USD"));
            }
        }

        [Test]
        public async Task CallTool_StronglyTyped_DeserializesCallToolResult()
        {
            var mcp = CreateClient();

            var result = await mcp.CallTool<CallToolResult>("search", new { query = "Microsoft", quotesCount = 3, newsCount = 0 });

            Assert.False(result.isError);
            Assert.That(result.content.Single().type, Is.EqualTo("text"));
            Assert.That(result.content.Single().text, Does.Contain("MSFT"));
        }

        [Test]
        public async Task CallToolJson_GetQuoteForMicrosoft_DeserializesTextContentAsJson()
        {
            var mcp = CreateClient();

            var quotes = await mcp.CallToolJson<Quote[]>("get_quote", new { symbols = new[] { "MSFT", "AAPL" } });

            Assert.That(quotes.Select(quote => quote.symbol), Is.EquivalentTo(new[] { "MSFT", "AAPL" }));
            Assert.That(quotes.Single(quote => quote.symbol == "MSFT").shortName, Does.Contain("Microsoft"));
            Assert.That(quotes.All(quote => quote.currency == "USD"));
        }
        
        [Test]
        public async Task CallToolJsonDynamic_GetQuoteForMicrosoft_DeserializesTextContentAsJson()
        {
            var mcp = CreateClient();

            var quotes = await mcp.CallToolJson("get_quote", new { symbols = new[] { "MSFT", "AAPL" } });

            Assert.That(quotes[0].shortName, Does.Contain("Microsoft"));
            Assert.That(quotes[0].currency, Is.EqualTo("USD"));
            Assert.That(quotes[0].regularMarketPrice > 0); // Plain dynamic, no GetDouble()
            Assert.That(quotes.Count, Is.EqualTo(2));

            IEnumerable<dynamic> enumerable = quotes; // foreach and LINQ work like any RestClient response
            Assert.That(enumerable.Select(quote => (string)quote.symbol), Is.EquivalentTo(new[] { "MSFT", "AAPL" }));
        }

        [Test]
        public void CallToolJson_UnknownTool_ThrowsMcpExceptionWithToolsText()
        {
            var mcp = CreateClient();

            var exception = Assert.ThrowsAsync<McpException>(async () => await mcp.CallToolJson<Quote[]>("does_not_exist"));

            Assert.That(exception.Message, Does.Contain("does_not_exist"));
        }

        [Test]
        public async Task CallTool_UnknownTool_ReturnsIsErrorResultNotException()
        {
            var mcp = CreateClient();

            var result = await mcp.CallTool("does_not_exist");

            Assert.True((bool)result.isError); // Tool execution errors are a result with isError per the MCP spec, not a JSON-RPC error
            Assert.That((string)result.content[0].text, Does.Contain("does_not_exist"));
        }

        [Test]
        public async Task CallTool_SeveralCallsOnOneClient_InitializesOnceAndAllSucceed()
        {
            var initializeCalls = 0;
            IRestClient mcp = new RestClient(McpEndpoint, new Config()
                .UseMcpHandler()
                .UseHandler(async (request, token, next) =>
                {
                    var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
                    if (body.Contains("\"method\":\"initialize\"")) initializeCalls++;
                    return await next(request, token);
                }));

            await mcp.Ping();
            var apple = await mcp.CallTool("get_quote", new { symbols = new[] { "AAPL" } });
            var tools = await mcp.ListTools();

            Assert.That(initializeCalls, Is.EqualTo(1));
            Assert.That((string)apple.content[0].text, Does.Contain("Apple"));
            Assert.That((IEnumerable<dynamic>)tools.tools, Is.Not.Empty);
        }

        [Test]
        public async Task AddRestClientTClient_WithUseMcpHandler_TypedYahooFinanceClientWorks()
        {
            var services = new ServiceCollection();
            services.AddRestClient<YahooFinanceClient>(McpEndpoint).UseMcpHandler();

            var client = services.BuildServiceProvider().GetRequiredService<YahooFinanceClient>();

            var name = await client.GetShortName("MSFT");

            Assert.That(name, Does.Contain("Microsoft"));
        }

        public class YahooFinanceClient
        {
            private readonly IRestClient _mcp;

            public YahooFinanceClient(IRestClient mcp) => _mcp = mcp;

            public async Task<string> GetShortName(string symbol)
            {
                var quotes = await _mcp.CallToolJson<Quote[]>("get_quote", new { symbols = new[] { symbol } });

                return quotes.Single().shortName;
            }
        }

        public class Quote
        {
            public string symbol { get; set; }
            public string shortName { get; set; }
            public string currency { get; set; }
            public double regularMarketPrice { get; set; }
        }

        public class CallToolResult
        {
            public List<Content> content { get; set; }
            public bool isError { get; set; }
        }

        public class Content
        {
            public string type { get; set; }
            public string text { get; set; }
        }
    }
}
