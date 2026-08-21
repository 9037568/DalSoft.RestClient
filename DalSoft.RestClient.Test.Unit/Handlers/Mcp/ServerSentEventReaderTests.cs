using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DalSoft.RestClient.Handlers.Mcp;
using NUnit.Framework;

namespace DalSoft.RestClient.Test.Unit.Handlers.Mcp
{
    [TestFixture]
    public class ServerSentEventReaderTests
    {
        private static async Task<List<ServerSentEvent>> ReadAll(string stream)
        {
            var reader = new ServerSentEventReader(new MemoryStream(Encoding.UTF8.GetBytes(stream)));
            var events = new List<ServerSentEvent>();
            ServerSentEvent serverSentEvent;

            while ((serverSentEvent = await reader.ReadAsync()) != null)
                events.Add(serverSentEvent);

            return events;
        }

        [Test]
        public async Task ReadAsync_SingleEvent_ReturnsData()
        {
            var events = await ReadAll("data: {\"a\":1}\n\n");

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Data, Is.EqualTo("{\"a\":1}"));
            Assert.Null(events[0].Event);
            Assert.Null(events[0].Id);
        }

        [Test]
        public async Task ReadAsync_MultiLineData_JoinsWithNewLine()
        {
            var events = await ReadAll("data: line1\ndata: line2\n\n");

            Assert.That(events[0].Data, Is.EqualTo("line1\nline2"));
        }

        [Test]
        public async Task ReadAsync_EventNameAndId_AreCaptured()
        {
            var events = await ReadAll("event: message\nid: 42\ndata: hello\n\n");

            Assert.That(events[0].Event, Is.EqualTo("message"));
            Assert.That(events[0].Id, Is.EqualTo("42"));
            Assert.That(events[0].Data, Is.EqualTo("hello"));
        }

        [Test]
        public async Task ReadAsync_CommentsAndUnknownFields_AreIgnored()
        {
            var events = await ReadAll(": keep-alive\nretry: 1000\nfoo: bar\ndata: hello\n\n");

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Data, Is.EqualTo("hello"));
        }

        [Test]
        public async Task ReadAsync_CrLfLineEndings_AreHandled()
        {
            var events = await ReadAll("data: one\r\n\r\ndata: two\r\n\r\n");

            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[0].Data, Is.EqualTo("one"));
            Assert.That(events[1].Data, Is.EqualTo("two"));
        }

        [Test]
        public async Task ReadAsync_NoTrailingBlankLine_StillDispatchesLastEvent()
        {
            var events = await ReadAll("data: one\n\ndata: two");

            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[1].Data, Is.EqualTo("two"));
        }

        [Test]
        public async Task ReadAsync_BlankLinesWithoutData_DispatchNothing()
        {
            var events = await ReadAll("\n\n: comment\n\nevent: ping\n\n");

            Assert.That(events, Is.Empty);
        }

        [Test]
        public async Task ReadAsync_DataWithoutSpaceAfterColon_IsParsed()
        {
            var events = await ReadAll("data:hello\n\n");

            Assert.That(events[0].Data, Is.EqualTo("hello"));
        }

        [Test]
        public async Task ReadAsync_EmptyStream_ReturnsNull()
        {
            var events = await ReadAll(string.Empty);

            Assert.That(events, Is.Empty);
        }
    }
}
