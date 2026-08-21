using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DalSoft.RestClient.Extensions;
using DalSoft.RestClient.Handlers.Mcp;
using DalSoft.RestClient.Serialization;

namespace DalSoft.RestClient
{
    /// <summary>Helpers for calling an MCP server, the RestClient base uri must be the MCP endpoint and Config.UseMcpHandler() must be used</summary>
    public static class McpExtensions
    {
        public static Task<dynamic> McpRequest(this IRestClient restClient, string method, object @params = null)
        {
            return @params == null ? restClient.Post(new { method }) : restClient.Post(new { method, @params });
        }

        public static Task<TReturns> McpRequest<TReturns>(this IRestClient restClient, string method, object @params = null) where TReturns : class
        {
            return @params == null ? restClient.Post<object, TReturns>(new { method }) : restClient.Post<object, TReturns>(new { method, @params });
        }

        public static Task<dynamic> Ping(this IRestClient restClient) => restClient.McpRequest("ping");

        public static Task<dynamic> ListTools(this IRestClient restClient, string cursor = null) => restClient.McpRequest("tools/list", Cursor(cursor));
        public static Task<TReturns> ListTools<TReturns>(this IRestClient restClient, string cursor = null) where TReturns : class => restClient.McpRequest<TReturns>("tools/list", Cursor(cursor));

        public static Task<dynamic> CallTool(this IRestClient restClient, string name, object arguments = null) => restClient.McpRequest("tools/call", NameAndArguments(name, arguments));
        public static Task<TReturns> CallTool<TReturns>(this IRestClient restClient, string name, object arguments = null) where TReturns : class => restClient.McpRequest<TReturns>("tools/call", NameAndArguments(name, arguments));

        /// <summary>
        /// Calls a tool and returns its result as JSON, dynamically typed exactly like any other RestClient response. Uses structuredContent when the server returns it, otherwise the first
        /// text content block is parsed as JSON (MCP tool results are content blocks for an LLM, not data - use this when you know the tool returns JSON text). Throws McpException when isError is true.
        /// </summary>
        public static async Task<dynamic> CallToolJson(this IRestClient restClient, string name, object arguments = null)
        {
            var result = await restClient.CallTool(name, arguments).ConfigureAwait(false);

            HttpResponseMessage response = result;
            if (!response.IsSuccessStatusCode)
                throw new McpException($"tools/call {name} failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            if (result.isError == true)
                throw new McpException(GetTextBlocks((object)result).FirstOrDefault() ?? $"Tool {name} returned isError");

            var structuredContent = result.structuredContent;
            if (structuredContent != null)
                return structuredContent;

            string text = GetTextBlocks((object)result).FirstOrDefault();
            if (text == null)
                throw new McpException($"Tool {name} returned no structuredContent and no text content to parse as JSON");

            object root = result;
            var serializer = (root as RestClientResponseObject)?.Serializer ?? SystemTextJsonSerializer.Default;

            if (!serializer.TryParse(text, out var node) || node == null)
                throw new McpException($"Tool {name} returned text content that is not JSON: {text}");

            return node.Wrap();
        }

        private static IEnumerable<string> GetTextBlocks(object result)
        {
            var content = ((dynamic)result).content as IEnumerable<object>;
            if (content == null) yield break;

            foreach (dynamic block in content)
            {
                if (block == null || block.type != "text") continue;

                string text = block.text;
                if (text != null) yield return text;
            }
        }

        /// <summary>
        /// Calls a tool and deserializes its result as JSON. Uses structuredContent when the server returns it, otherwise the first text content block is parsed as JSON
        /// (MCP tool results are content blocks for an LLM, not data - use this when you know the tool returns JSON text). Throws McpException when isError is true.
        /// </summary>
        public static async Task<TResult> CallToolJson<TResult>(this IRestClient restClient, string name, object arguments = null, JsonSerializerOptions jsonSerializerOptions = null)
        {
            var result = await restClient.CallTool(name, arguments).ConfigureAwait(false);

            HttpResponseMessage response = result;
            if (!response.IsSuccessStatusCode)
                throw new McpException($"tools/call {name} failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            string json = result;

            using (var document = JsonDocument.Parse(json))
            {
                var root = document.RootElement;
                var textBlocks = root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                    ? content.EnumerateArray().Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text" && block.TryGetProperty("text", out _)).Select(block => block.GetProperty("text").GetString()).ToList()
                    : new List<string>();

                if (root.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
                    throw new McpException(textBlocks.Any() ? string.Join("\n", textBlocks) : $"Tool {name} returned isError");

                if (root.TryGetProperty("structuredContent", out var structuredContent) && structuredContent.ValueKind != JsonValueKind.Null && structuredContent.ValueKind != JsonValueKind.Undefined)
                    return JsonSerializer.Deserialize<TResult>(structuredContent.GetRawText(), jsonSerializerOptions);

                if (!textBlocks.Any())
                    throw new McpException($"Tool {name} returned no structuredContent and no text content to parse as JSON");

                return JsonSerializer.Deserialize<TResult>(textBlocks[0], jsonSerializerOptions);
            }
        }

        public static Task<dynamic> ListResources(this IRestClient restClient, string cursor = null) => restClient.McpRequest("resources/list", Cursor(cursor));
        public static Task<TReturns> ListResources<TReturns>(this IRestClient restClient, string cursor = null) where TReturns : class => restClient.McpRequest<TReturns>("resources/list", Cursor(cursor));

        public static Task<dynamic> ReadResource(this IRestClient restClient, string uri) => restClient.McpRequest("resources/read", new { uri });
        public static Task<TReturns> ReadResource<TReturns>(this IRestClient restClient, string uri) where TReturns : class => restClient.McpRequest<TReturns>("resources/read", new { uri });

        public static Task<dynamic> ListPrompts(this IRestClient restClient, string cursor = null) => restClient.McpRequest("prompts/list", Cursor(cursor));
        public static Task<TReturns> ListPrompts<TReturns>(this IRestClient restClient, string cursor = null) where TReturns : class => restClient.McpRequest<TReturns>("prompts/list", Cursor(cursor));

        public static Task<dynamic> GetPrompt(this IRestClient restClient, string name, object arguments = null) => restClient.McpRequest("prompts/get", NameAndArguments(name, arguments));
        public static Task<TReturns> GetPrompt<TReturns>(this IRestClient restClient, string name, object arguments = null) where TReturns : class => restClient.McpRequest<TReturns>("prompts/get", NameAndArguments(name, arguments));

        private static object Cursor(string cursor) => cursor == null ? null : new { cursor };
        private static object NameAndArguments(string name, object arguments) => arguments == null ? new { name } : (object)new { name, arguments };
    }
}
