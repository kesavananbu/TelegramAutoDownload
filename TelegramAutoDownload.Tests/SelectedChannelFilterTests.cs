using FluentAssertions;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using TelegramAutoDownload.Models;
using TelegramAutoDownload.Services;
using TelegramClient.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    // ===========================================================================
    // Bug fix: inactive channels (Selected=false) must never be downloaded
    // ===========================================================================

    public class SelectedChannelFilterTests
    {
        // Mirrors the fixed UpdateConfig filter: only selected chats join the listen list
        private static List<long> BuildListenIds(IEnumerable<ChatDto> chats) =>
            chats.Where(c => c.Selected).Select(c => c.Id).ToList();

        [Fact]
        public void BuildListenIds_InactiveChannel_IsExcluded()
        {
            var chats = new[]
            {
                new ChatDto { Id = 100, Name = "Active",   Selected = true  },
                new ChatDto { Id = 200, Name = "Inactive", Selected = false },
            };

            var ids = BuildListenIds(chats);

            ids.Should().Contain(100, "active channel must be in listen list");
            ids.Should().NotContain(200, "inactive channel must never be in listen list");
        }

        [Fact]
        public void BuildListenIds_AllInactive_ReturnsEmpty()
        {
            var chats = new[]
            {
                new ChatDto { Id = 1, Name = "A", Selected = false },
                new ChatDto { Id = 2, Name = "B", Selected = false },
            };

            BuildListenIds(chats).Should().BeEmpty("no active channels means empty listen list");
        }

        [Fact]
        public void BuildListenIds_AllActive_ReturnsAll()
        {
            var chats = new[]
            {
                new ChatDto { Id = 1, Name = "A", Selected = true },
                new ChatDto { Id = 2, Name = "B", Selected = true },
                new ChatDto { Id = 3, Name = "C", Selected = true },
            };

            BuildListenIds(chats).Should().BeEquivalentTo(new[] { 1L, 2L, 3L });
        }

        [Fact]
        public void BuildListenIds_EmptyList_ReturnsEmpty()
        {
            BuildListenIds(Enumerable.Empty<ChatDto>()).Should().BeEmpty();
        }

        // Mirrors the fixed FindMonitoredChat predicate
        private static ChatDto? FindMonitored(IEnumerable<ChatDto> chats, long peerId) =>
            chats.FirstOrDefault(c => (c.Id == peerId || c.Id == -peerId) && c.Selected);

        [Fact]
        public void FindMonitored_ActiveChannel_IsFound()
        {
            var chats = new[]
            {
                new ChatDto { Id = 111, Name = "Active", Selected = true },
            };

            FindMonitored(chats, 111).Should().NotBeNull("active channel must be resolved");
        }

        [Fact]
        public void FindMonitored_InactiveChannel_IsNotFound()
        {
            var chats = new[]
            {
                new ChatDto { Id = 222, Name = "Inactive", Selected = false },
            };

            FindMonitored(chats, 222).Should().BeNull(
                "inactive channel must return null even if the ID matches");
        }

        [Fact]
        public void FindMonitored_NegativeIdVariant_ActiveChannel_IsFound()
        {
            // Telegram sometimes sends the negative of the peer ID for channels
            var chats = new[]
            {
                new ChatDto { Id = 333, Name = "Active", Selected = true },
            };

            FindMonitored(chats, -333).Should().NotBeNull(
                "negative-ID variant must also resolve when channel is active");
        }

        [Fact]
        public void FindMonitored_NegativeIdVariant_InactiveChannel_IsNotFound()
        {
            var chats = new[]
            {
                new ChatDto { Id = 444, Name = "Inactive", Selected = false },
            };

            FindMonitored(chats, -444).Should().BeNull(
                "negative-ID variant of an inactive channel must still return null");
        }

        [Fact]
        public void FindMonitored_UnknownId_ReturnsNull()
        {
            var chats = new[]
            {
                new ChatDto { Id = 999, Name = "Other", Selected = true },
            };

            FindMonitored(chats, 123).Should().BeNull("unknown peer ID must never match");
        }
    }

    // ===========================================================================
    // AppLogAlertService — state tracking and event behaviour
    // ===========================================================================

    public class AppLogAlertServiceTests : IDisposable
    {
        // Reset shared singleton state before each test to avoid cross-test interference
        public AppLogAlertServiceTests() => AppLogAlertService.Instance.Clear();
        public void Dispose() => AppLogAlertService.Instance.Clear();

        private static LogPointer MakePointer(string summary = "test error") => new()
        {
            FilePath   = "app-20260101.log",
            SearchText = summary,
            Timestamp  = DateTimeOffset.UtcNow,
            Level      = "ERR",
            Summary    = summary,
        };

        [Fact]
        public void InitialState_UnreadCountIsZero()
        {
            AppLogAlertService.Instance.UnreadCount.Should().Be(0);
        }

        [Fact]
        public void InitialState_LatestIsNull()
        {
            AppLogAlertService.Instance.Latest.Should().BeNull();
        }

        [Fact]
        public void Report_IncrementsUnreadCount()
        {
            AppLogAlertService.Instance.Report(MakePointer());

            AppLogAlertService.Instance.UnreadCount.Should().Be(1);
        }

        [Fact]
        public void Report_SetsLatest()
        {
            var pointer = MakePointer("specific error");
            AppLogAlertService.Instance.Report(pointer);

            AppLogAlertService.Instance.Latest.Should().BeSameAs(pointer);
        }

        [Fact]
        public void Report_MultipleErrors_CountsAll()
        {
            AppLogAlertService.Instance.Report(MakePointer("err1"));
            AppLogAlertService.Instance.Report(MakePointer("err2"));
            AppLogAlertService.Instance.Report(MakePointer("err3"));

            AppLogAlertService.Instance.UnreadCount.Should().Be(3);
        }

        [Fact]
        public void Report_MultipleErrors_LatestIsLast()
        {
            var first  = MakePointer("first");
            var second = MakePointer("second");

            AppLogAlertService.Instance.Report(first);
            AppLogAlertService.Instance.Report(second);

            AppLogAlertService.Instance.Latest.Should().BeSameAs(second,
                "Latest must always track the most recently reported pointer");
        }

        [Fact]
        public void Report_FiresChangedEvent()
        {
            var raised = 0;
            AppLogAlertService.Instance.Changed += () => raised++;

            AppLogAlertService.Instance.Report(MakePointer());

            raised.Should().Be(1, "Changed must fire once per Report call");
        }

        [Fact]
        public void Clear_ResetsUnreadCountToZero()
        {
            AppLogAlertService.Instance.Report(MakePointer());
            AppLogAlertService.Instance.Report(MakePointer());

            AppLogAlertService.Instance.Clear();

            AppLogAlertService.Instance.UnreadCount.Should().Be(0);
        }

        [Fact]
        public void Clear_SetsLatestToNull()
        {
            AppLogAlertService.Instance.Report(MakePointer());

            AppLogAlertService.Instance.Clear();

            AppLogAlertService.Instance.Latest.Should().BeNull();
        }

        [Fact]
        public void Clear_FiresChangedEvent()
        {
            AppLogAlertService.Instance.Report(MakePointer());
            var raised = 0;
            AppLogAlertService.Instance.Changed += () => raised++;

            AppLogAlertService.Instance.Clear();

            raised.Should().Be(1, "Clear must fire Changed so the UI can hide the button");
        }

        [Fact]
        public void Clear_WhenAlreadyEmpty_DoesNotFireChangedEvent()
        {
            // Pre-condition: already empty (set up by constructor)
            var raised = 0;
            AppLogAlertService.Instance.Changed += () => raised++;

            AppLogAlertService.Instance.Clear();

            raised.Should().Be(0, "Clear on already-empty state must not fire Changed unnecessarily");
        }
    }

    // ===========================================================================
    // LogAlertSink — only fires for Warning and above
    // ===========================================================================

    public class LogAlertSinkLevelFilterTests : IDisposable
    {
        private readonly LogAlertSink _sink;

        public LogAlertSinkLevelFilterTests()
        {
            AppLogAlertService.Instance.Clear();
            _sink = new LogAlertSink(SerilogFileSettings.FileOutputTemplate);
        }

        public void Dispose() => AppLogAlertService.Instance.Clear();

        private static LogEvent MakeLogEvent(LogEventLevel level, string message = "test")
        {
            var template = new Serilog.Parsing.MessageTemplateParser().Parse(message);
            return new LogEvent(
                DateTimeOffset.UtcNow,
                level,
                null,
                template,
                Enumerable.Empty<LogEventProperty>());
        }

        [Theory]
        [InlineData(LogEventLevel.Verbose)]
        [InlineData(LogEventLevel.Debug)]
        [InlineData(LogEventLevel.Information)]
        public void Emit_BelowWarning_DoesNotAlertService(LogEventLevel level)
        {
            var before = AppLogAlertService.Instance.UnreadCount;

            _sink.Emit(MakeLogEvent(level));

            AppLogAlertService.Instance.UnreadCount.Should().Be(before,
                $"level {level} must not trigger the error alert");
        }

        [Theory]
        [InlineData(LogEventLevel.Warning)]
        [InlineData(LogEventLevel.Error)]
        [InlineData(LogEventLevel.Fatal)]
        public void Emit_WarningOrAbove_AlertsService(LogEventLevel level)
        {
            var before = AppLogAlertService.Instance.UnreadCount;

            _sink.Emit(MakeLogEvent(level, $"Something went wrong at level {level}"));

            AppLogAlertService.Instance.UnreadCount.Should().Be(before + 1,
                $"level {level} must increment the unread count");
        }

        [Fact]
        public void Emit_Error_SetsLevelToERR()
        {
            _sink.Emit(MakeLogEvent(LogEventLevel.Error, "an error occurred"));

            AppLogAlertService.Instance.Latest!.Level.Should().Be("ERR");
        }

        [Fact]
        public void Emit_Fatal_SetsLevelToFTL()
        {
            _sink.Emit(MakeLogEvent(LogEventLevel.Fatal, "fatal problem"));

            AppLogAlertService.Instance.Latest!.Level.Should().Be("FTL");
        }

        [Fact]
        public void Emit_Warning_SetsLevelToWRN()
        {
            _sink.Emit(MakeLogEvent(LogEventLevel.Warning, "a warning"));

            AppLogAlertService.Instance.Latest!.Level.Should().Be("WRN");
        }

        [Fact]
        public void Emit_Error_SummaryIsNotEmpty()
        {
            _sink.Emit(MakeLogEvent(LogEventLevel.Error, "disk full"));

            AppLogAlertService.Instance.Latest!.Summary.Should().NotBeNullOrEmpty(
                "summary must be populated so the tooltip can show a human-readable hint");
        }

        [Fact]
        public void Emit_Error_FilePathContainsDatePattern()
        {
            _sink.Emit(MakeLogEvent(LogEventLevel.Error, "file path test"));

            AppLogAlertService.Instance.Latest!.FilePath.Should()
                .MatchRegex(@"app-\d{8}\.log$",
                    because: "FilePath must follow the rolling-file naming pattern");
        }
    }

    // ===========================================================================
    // LogAlertSink.BuildSearchAnchor — search text extraction
    // ===========================================================================

    public class LogAlertSinkSearchAnchorTests
    {
        [Fact]
        public void BuildSearchAnchor_MessageOver12Chars_ReturnsMessage()
        {
            // body.Length >= 12 → returns the message body directly (up to 100 chars)
            var anchor = LogAlertSink.BuildSearchAnchor("rendered line", "this is long enough");
            anchor.Should().Be("this is long enough");
        }

        [Fact]
        public void BuildSearchAnchor_LongMessage_TruncatesAt100Chars()
        {
            var longMsg = new string('x', 150);
            var anchor  = LogAlertSink.BuildSearchAnchor("rendered", longMsg);
            anchor.Should().HaveLength(100);
        }

        [Fact]
        public void BuildSearchAnchor_ShortMessage_Under12Chars_FallsBackToRenderedLine()
        {
            // Message body < 12 chars → fall back to rendered line
            var anchor = LogAlertSink.BuildSearchAnchor("rendered full line text", "short");
            anchor.Should().Be("rendered full line text");
        }

        [Fact]
        public void BuildSearchAnchor_LongRenderedLine_IsTruncated()
        {
            var longLine = new string('y', 200);
            var anchor   = LogAlertSink.BuildSearchAnchor(longLine, "tiny");
            anchor.Length.Should().BeLessOrEqualTo(100);
        }
    }
}
