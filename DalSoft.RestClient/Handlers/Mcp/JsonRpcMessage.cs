using System;
using System.IO;
using System.Text.Json;
using DalSoft.RestClient.Serialization;

namespace DalSoft.RestClient.Handlers.Mcp
{
    /// <summary>A JSON-RPC 2.0 request or notification ready to be sent, the envelope (jsonrpc, id) is added to whatever the caller supplied</summary>
    internal class JsonRpcMessage
    {
        public byte[] Body { get; private set; }
        /// <summary>Raw JSON text of the id (for example 1 or "abc"), null for notifications</summary>
        public string Id { get; private set; }
        public string Method { get; private set; }
        public bool IsNotification => Id == null;

        public static JsonRpcMessage FromContent(object content, McpSession session, IJsonSerializer serializer)
        {
            if (content == null)
                throw new ArgumentException("MCP requests need a body containing the JSON-RPC method, for example Post(new { method = \"tools/list\" })");

            var json = content as string ?? serializer.Serialize(content);

            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("MCP request body must serialize to a JSON object containing the JSON-RPC method");

                return Build(document.RootElement, session);
            }
        }

        public static JsonRpcMessage Create(string method, object @params, McpSession session, IJsonSerializer serializer)
        {
            return FromContent(@params == null ? new { method } : (object)new { method, @params }, session, serializer);
        }

        private static JsonRpcMessage Build(JsonElement root, McpSession session)
        {
            var message = new JsonRpcMessage
            {
                Method = root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString() : null
            };

            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("jsonrpc", "2.0");

                    if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null)
                    {
                        writer.WritePropertyName("id");
                        idElement.WriteTo(writer);
                        message.Id = idElement.GetRawText();
                    }
                    else if (message.Method == null || !message.Method.StartsWith("notifications/", StringComparison.Ordinal))
                    {
                        var id = session.NextRequestId();
                        writer.WriteNumber("id", id);
                        message.Id = id.ToString();
                    }

                    foreach (var property in root.EnumerateObject())
                    {
                        if (property.Name == "jsonrpc" || property.Name == "id") continue;
                        property.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                }

                message.Body = stream.ToArray();
            }

            return message;
        }
    }
}
