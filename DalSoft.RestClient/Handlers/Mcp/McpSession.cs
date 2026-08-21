using System.Text.Json;
using System.Threading;

namespace DalSoft.RestClient.Handlers.Mcp
{
    /// <summary>Holds MCP session state (session id, negotiated protocol version) so it can outlive a single McpHandler instance, for example across IHttpClientFactory handler rotation</summary>
    public class McpSession
    {
        private long _requestId;
        private volatile bool _isInitialized;

        internal readonly SemaphoreSlim InitializeLock = new SemaphoreSlim(1, 1);

        /// <summary>Mcp-Session-Id returned by the server, null if the server is stateless</summary>
        public string SessionId { get; internal set; }
        public string NegotiatedProtocolVersion { get; internal set; }
        public string LastEventId { get; internal set; }
        public JsonElement? ServerInfo { get; internal set; }
        public JsonElement? ServerCapabilities { get; internal set; }

        public bool IsInitialized
        {
            get => _isInitialized;
            internal set => _isInitialized = value;
        }

        internal long NextRequestId() => Interlocked.Increment(ref _requestId);

        internal void Reset()
        {
            IsInitialized = false;
            SessionId = null;
            NegotiatedProtocolVersion = null;
            LastEventId = null;
            ServerInfo = null;
            ServerCapabilities = null;
        }
    }
}
