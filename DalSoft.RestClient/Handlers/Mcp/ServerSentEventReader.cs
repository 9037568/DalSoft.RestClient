using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DalSoft.RestClient.Handlers.Mcp
{
    internal class ServerSentEvent
    {
        public string Id { get; set; }
        public string Event { get; set; }
        public string Data { get; set; }
    }

    /// <summary>Minimal text/event-stream parser https://html.spec.whatwg.org/multipage/server-sent-events.html#event-stream-interpretation</summary>
    internal class ServerSentEventReader
    {
        private readonly StreamReader _reader;

        public ServerSentEventReader(Stream stream)
        {
            _reader = new StreamReader(stream, new UTF8Encoding(false));
        }

        /// <summary>Returns the next event with data, or null at end of stream</summary>
        public async Task<ServerSentEvent> ReadAsync()
        {
            string id = null, eventName = null;
            StringBuilder data = null;

            while (true)
            {
                var line = await _reader.ReadLineAsync().ConfigureAwait(false);

                if (line == null) // End of stream, dispatch anything pending (lenient, the spec discards it)
                    return data == null ? null : new ServerSentEvent { Id = id, Event = eventName, Data = data.ToString() };

                if (line.Length == 0)
                {
                    if (data == null) { id = null; eventName = null; continue; } // Blank line with no data, nothing to dispatch
                    return new ServerSentEvent { Id = id, Event = eventName, Data = data.ToString() };
                }

                if (line[0] == ':') continue; // Comment

                string field, value;
                var colon = line.IndexOf(':');
                if (colon < 0) { field = line; value = string.Empty; }
                else
                {
                    field = line.Substring(0, colon);
                    value = line.Substring(colon + 1);
                    if (value.Length > 0 && value[0] == ' ') value = value.Substring(1);
                }

                switch (field)
                {
                    case "data":
                        if (data == null) data = new StringBuilder();
                        else data.Append('\n');
                        data.Append(value);
                        break;
                    case "event":
                        eventName = value;
                        break;
                    case "id":
                        if (value.IndexOf('\0') < 0) id = value;
                        break;
                    // retry and unknown fields are ignored
                }
            }
        }
    }
}
