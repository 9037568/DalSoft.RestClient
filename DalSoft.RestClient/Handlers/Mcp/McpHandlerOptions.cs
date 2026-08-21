using System;
using System.Reflection;
using System.Text.Json;

namespace DalSoft.RestClient.Handlers.Mcp
{
    public class McpHandlerOptions
    {
        public const string DefaultProtocolVersion = "2025-06-18";

        /// <summary>Sent as clientInfo.name in the initialize request</summary>
        public string ClientName { get; set; } = Assembly.GetEntryAssembly()?.GetName().Name ?? "DalSoft.RestClient";
        
        /// <summary>Sent as clientInfo.version in the initialize request</summary>
        public string ClientVersion { get; set; } = "1.0.0";

        /// <summary>Protocol version requested in the initialize request, the server may negotiate a different version</summary>
        public string ProtocolVersion { get; set; } = DefaultProtocolVersion;

        /// <summary>Client capabilities sent in the initialize request, defaults to none</summary>
        public object Capabilities { get; set; } = new { };

        /// <summary>Invoked for each JSON-RPC notification (for example notifications/progress) the server sends on a response stream, params is passed as the JsonElement</summary>
        public Action<string, JsonElement> OnNotification { get; set; }
    }
}
