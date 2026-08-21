using System;
using System.Text.Json;

namespace DalSoft.RestClient.Handlers.Mcp
{
    /// <summary>Thrown by McpHandler when the MCP server returns a JSON-RPC error, or the session can't be initialized</summary>
    public class McpException : Exception
    {
        /// <summary>JSON-RPC error code, 0 when the failure is a transport/initialization failure rather than a JSON-RPC error</summary>
        public int Code { get; }
        public JsonElement? Data { get; }

        public McpException(string message) : base(message) { }

        public McpException(int code, string message, JsonElement? data) : base(message)
        {
            Code = code;
            Data = data;
        }
    }
}
