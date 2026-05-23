using FluentAssertions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TelegramAutoDownload.Models;
using TelegramClient;
using TelegramClient.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    // ===========================================================================
    // 1b. DownloadItem.CanOpenFile / FilePath
    // ===========================================================================

    public class DownloadItemOpenTests
    {
        [Fact]
        public void CanOpenFile_WhenDoneWithExistingPath_ReturnsTrue()
        {
            var path = Path.Combine(Path.GetTempPath(), "tad-open-test-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "x");
            try
            {
                var item = new DownloadItem { Status = "✔ Done", FilePath = path };
                item.CanOpenFile.Should().BeTrue();
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void CanOpenFile_WhenDoneWithoutPath_ReturnsFalse()
        {
            new DownloadItem { Status = "✔ Done" }.CanOpenFile.Should().BeFalse();
        }
    }

    // ===========================================================================
    // 1. DownloadItem.CanRetry / RetryAsync property
    // ===========================================================================

    public class DownloadItemRetryTests
    {
        [Fact]
        public void CanRetry_WhenRetryAsyncIsNull_ReturnsFalse()
        {
            var item = new DownloadItem { Status = "✖ Error" };
            item.CanRetry.Should().BeFalse("RetryAsync is null so retry is not available");
        }

        [Theory]
        [InlineData("✔ Done")]
        [InlineData("⬇ Downloading")]
        [InlineData("⏳ Queued")]
        [InlineData("✖ Cancelled")]
        public void CanRetry_WhenStatusIsNotErrorOrTimeout_ReturnsFalse(string status)
        {
            var item = new DownloadItem
            {
                Status     = status,
                RetryAsync = () => Task.CompletedTask
            };
            item.CanRetry.Should().BeFalse(
                because: $"CanRetry should be false for status '{status}'");
        }

        [Theory]
        [InlineData("✖ Error")]
        [InlineData("✖ Timeout")]
        public void CanRetry_WhenRetryAsyncIsSetAndStatusIsFailure_ReturnsTrue(string status)
        {
            var item = new DownloadItem
            {
                Status     = status,
                RetryAsync = () => Task.CompletedTask
            };
            item.CanRetry.Should().BeTrue(
                because: $"RetryAsync is set and status is '{status}'");
        }

        [Fact]
        public void RetryAsync_Setter_FiresPropertyChangedForCanRetry()
        {
            var item = new DownloadItem { Status = "✖ Error" };
            var raised = new List<string>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            item.RetryAsync = () => Task.CompletedTask;

            raised.Should().Contain(nameof(DownloadItem.CanRetry),
                because: "setting RetryAsync must notify CanRetry so the UI button can appear");
            raised.Should().Contain(nameof(DownloadItem.RetryAsync));
        }

        [Fact]
        public void Status_Setter_FiresPropertyChangedForCanRetry()
        {
            var item = new DownloadItem
            {
                RetryAsync = () => Task.CompletedTask,
                Status     = "⬇ Downloading"
            };
            var raised = new List<string>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            item.Status = "✖ Error";

            raised.Should().Contain(nameof(DownloadItem.CanRetry),
                because: "changing Status must re-evaluate CanRetry so the UI button appears/disappears");
        }

        [Fact]
        public void ClearingRetryAsync_MakesCanRetryFalse()
        {
            var item = new DownloadItem
            {
                Status     = "✖ Error",
                RetryAsync = () => Task.CompletedTask
            };
            item.CanRetry.Should().BeTrue();

            item.RetryAsync = null;

            item.CanRetry.Should().BeFalse("clearing RetryAsync must disable the retry button");
        }
    }

    // ===========================================================================
    // 3. Folder Template resolution
    // ===========================================================================

    public class FolderTemplateTests
    {
        [Fact]
        public void Resolve_NullOrEmpty_ReturnsNull()
        {
            FolderTemplateHelper.Resolve(null,  "Videos", "Chat").Should().BeNull();
            FolderTemplateHelper.Resolve("",    "Videos", "Chat").Should().BeNull();
            FolderTemplateHelper.Resolve("   ", "Videos", "Chat").Should().BeNull();
        }

        [Fact]
        public void Resolve_AllTokens_AreSubstituted()
        {
            var at  = new DateTime(2026, 5, 6);
            var res = FolderTemplateHelper.Resolve(
                "{Type}/{ChatName}/{Year}/{Month}/{Day}", "Videos", "MyChan", at);

            res.Should().Be("Videos/MyChan/2026/05/06");
        }

        [Fact]
        public void Resolve_TokensAreCaseInsensitive()
        {
            var at  = new DateTime(2026, 5, 6);
            var res = FolderTemplateHelper.Resolve("{type}/{chatname}", "Videos", "Chan", at);
            res.Should().Be("Videos/Chan");
        }

        [Fact]
        public void Resolve_ChatNameWithInvalidChars_IsSanitized()
        {
            var res = FolderTemplateHelper.Resolve("{ChatName}", "Videos", "My:Chat/Name", null);
            res.Should().NotContain(":")
               .And.NotContain("/", because: "/ in chat name must be sanitized to a space");
        }

        [Fact]
        public void Resolve_ChatNameWithTilde_IsSanitized()
        {
            var res = FolderTemplateHelper.Resolve("{ChatName}", "Videos", "לולו סרטים ~ סרטים", null)!;
            res.Should().NotContain("~", because: "tilde is stripped so external tools do not treat it as home");
            res.Should().Contain("לולו");
        }

        [Fact]
        public void Resolve_TemplateWithNoTokens_ReturnsTemplateAsIs()
        {
            var res = FolderTemplateHelper.Resolve("static/folder", "Videos", "Chat");
            res.Should().Be("static/folder");
        }

        [Fact]
        public void Resolve_MonthAndDay_ArePaddedToTwoDigits()
        {
            var at  = new DateTime(2026, 1, 5); // January 5th
            var res = FolderTemplateHelper.Resolve("{Year}-{Month}-{Day}", "X", "Y", at);
            res.Should().Be("2026-01-05");
        }

        [Fact]
        public void SupportedTokens_ContainsAllDocumentedTokens()
        {
            var tokens = FolderTemplateHelper.SupportedTokens;
            tokens.Should().Contain("{Type}");
            tokens.Should().Contain("{ChatName}");
            tokens.Should().Contain("{Year}");
            tokens.Should().Contain("{Month}");
            tokens.Should().Contain("{Day}");
        }
    }

    // ===========================================================================
    // 4. IgnoreFileByRegex — filter string parsing (UI layer logic)
    // ===========================================================================

    public class IgnoreFilterParsingTests
    {
        // This mirrors the parsing logic in MainWindow.FilterTextBox_LostFocus
        private static List<string> ParseFilterText(string text) =>
            text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

        [Fact]
        public void Parse_EmptyString_ReturnsEmptyList()
        {
            ParseFilterText("").Should().BeEmpty();
        }

        [Fact]
        public void Parse_SinglePattern_ReturnsSingleEntry()
        {
            ParseFilterText(@"\.jpg$").Should().ContainSingle()
                .Which.Should().Be(@"\.jpg$");
        }

        [Fact]
        public void Parse_MultipleSemicolonSeparated_ReturnsAllEntries()
        {
            var result = ParseFilterText(@"\.jpg$; thumb_; \.png$");
            result.Should().HaveCount(3)
                  .And.Contain(@"\.jpg$")
                  .And.Contain("thumb_")
                  .And.Contain(@"\.png$");
        }

        [Fact]
        public void Parse_TrailingAndLeadingSemicolons_AreIgnored()
        {
            ParseFilterText("; pattern1 ; pattern2 ;").Should().HaveCount(2);
        }

        [Fact]
        public void Parse_WhitespaceAroundPatterns_IsTrimmed()
        {
            var result = ParseFilterText("  foo  ;  bar  ");
            result.Should().Contain("foo").And.Contain("bar");
            result.Should().NotContain("  foo  ");
        }

        [Fact]
        public void Parse_ValidRegex_DoesNotThrowOnMatch()
        {
            var patterns = ParseFilterText(@"\.jpg$; ^thumb");
            foreach (var p in patterns)
            {
                var act = () => new System.Text.RegularExpressions.Regex(p);
                act.Should().NotThrow($"pattern '{p}' must be valid regex");
            }
        }

        [Fact]
        public void ChatDto_IgnoreFileByRegex_DefaultsToEmpty()
        {
            var chat = new ChatDto { Name = "Test", Username = "", Type = "" };
            chat.IgnoreFileByRegex.Should().BeEmpty(
                because: "new chats must not have any filters pre-applied");
        }
    }

    // ===========================================================================
    // 5. Persistent Statistics — StatsData JSON serialisation
    // ===========================================================================

    public class StatisticsSerializationTests
    {
        // Tests the internal JSON round-trip without touching Application.Current
        private sealed class StatsSnapshot
        {
            public long TotalFilesAllTime { get; set; }
            public long TotalBytesAllTime { get; set; }
            public DateTime StatsStart    { get; set; }
        }

        [Fact]
        public void StatsData_RoundTrip_PreservesCounters()
        {
            var original = new StatsSnapshot
            {
                TotalFilesAllTime = 42,
                TotalBytesAllTime = 1_073_741_824L, // 1 GB
                StatsStart        = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            var json     = JsonConvert.SerializeObject(original, Formatting.Indented);
            var restored = JsonConvert.DeserializeObject<StatsSnapshot>(json)!;

            restored.TotalFilesAllTime.Should().Be(42);
            restored.TotalBytesAllTime.Should().Be(1_073_741_824L);
            restored.StatsStart.Should().Be(original.StatsStart);
        }

        [Fact]
        public void StatsData_AtomicIncrement_IsThreadSafe()
        {
            long counter = 0;
            const int iterations = 1000;

            Parallel.For(0, iterations, _ => System.Threading.Interlocked.Increment(ref counter));

            counter.Should().Be(iterations,
                because: "Interlocked.Increment must be race-condition-free under parallel writes");
        }

        [Fact]
        public void StatsData_JsonFile_WrittenAndReadBack()
        {
            var path = Path.Combine(Path.GetTempPath(), $"stats_test_{Guid.NewGuid():N}.json");
            try
            {
                var data = new StatsSnapshot { TotalFilesAllTime = 7, TotalBytesAllTime = 1024 };
                File.WriteAllText(path, JsonConvert.SerializeObject(data));

                var loaded = JsonConvert.DeserializeObject<StatsSnapshot>(File.ReadAllText(path))!;
                loaded.TotalFilesAllTime.Should().Be(7);
                loaded.TotalBytesAllTime.Should().Be(1024);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    // ===========================================================================
    // 6. CloseDialog — enum + default result
    // ===========================================================================

    public class CloseDialogTests
    {
        [Fact]
        public void CloseAction_HasExpectedValues()
        {
            Enum.GetValues<CloseAction>().Should().Contain(CloseAction.Cancel);
            Enum.GetValues<CloseAction>().Should().Contain(CloseAction.MinimizeToTray);
            Enum.GetValues<CloseAction>().Should().Contain(CloseAction.Exit);
        }

        [Fact]
        public void CloseAction_Cancel_IsDefaultEnumValue()
        {
            // The default enum value (0) must be Cancel so un-initialised code is safe
            ((int)CloseAction.Cancel).Should().Be(0,
                because: "Cancel must be the zero-value so default(CloseAction) == Cancel");
        }

        [Fact]
        public void CloseAction_MinimizeToTray_IsDistinctFromExit()
        {
            CloseAction.MinimizeToTray.Should().NotBe(CloseAction.Exit);
        }
    }

    // ===========================================================================
    // 7. ChatDto — new fields
    // ===========================================================================

    public class ChatDtoNewFieldsTests
    {
        [Fact]
        public void ChatDto_FolderTemplate_DefaultsToEmpty()
        {
            new ChatDto { Name = "x", Username = "", Type = "" }
                .FolderTemplate.Should().BeEmpty(
                    because: "empty template = use default layout");
        }

        [Fact]
        public void ChatDto_FolderTemplate_CanBeSetAndRead()
        {
            var chat = new ChatDto { Name = "x", Username = "", Type = "", FolderTemplate = "{ChatName}/{Year}" };
            chat.FolderTemplate.Should().Be("{ChatName}/{Year}");
        }

        [Fact]
        public void ChatDto_ProviderFolderTemplates_DefaultEmpty()
        {
            var chat = new ChatDto { Name = "c", Username = "", Type = "" };
            chat.SocialDownloadFolderTemplate.Should().BeEmpty();
            chat.YoutubeDownloadFolderTemplate.Should().BeEmpty();
            chat.OtherDownloadFolderTemplate.Should().BeEmpty();
            chat.TorrentDownloadFolderTemplate.Should().BeEmpty();
        }
    }

    // ===========================================================================
    // Folder Template — absolute-path support
    // ===========================================================================

    public class FolderTemplateAbsolutePathTests
    {
        [Theory]
        [InlineData(@"C:\Downloads\MyChannel")]
        [InlineData(@"D:\Media\Telegram")]
        [InlineData(@"C:\")]
        public void Resolve_AbsoluteWindowsPath_NoTokens_ReturnedAsIs(string absolutePath)
        {
            // Paths without tokens should come back unchanged even after the resolve pass
            var result = FolderTemplateHelper.Resolve(absolutePath, "Videos", "SomeChat");
            result.Should().Be(absolutePath);
        }

        [Fact]
        public void Resolve_AbsolutePathWithTokens_TokensAreReplaced()
        {
            // Absolute path that contains {ChatName} should have the token replaced
            var template = @"C:\Downloads\{ChatName}";
            var result   = FolderTemplateHelper.Resolve(template, "Videos", "MyChannel")!;
            result.Should().Be(@"C:\Downloads\MyChannel");
        }

        [Fact]
        public void Resolve_AbsolutePathWithDateTokens_TokensAreReplaced()
        {
            var template = @"C:\Archive\{Year}\{Month}";
            var result   = FolderTemplateHelper.Resolve(template, "Videos", "Chat",
                               new DateTime(2026, 5, 7))!;
            result.Should().Be(@"C:\Archive\2026\05");
        }

        [Fact]
        public void Resolve_AbsolutePathAfterTokenReplacement_IsStillRooted()
        {
            // After token substitution the path must still be absolute so callers
            // can detect it with Path.IsPathRooted and skip Path.Combine with basePath
            var template = @"C:\Downloads\{ChatName}\{Year}";
            var result   = FolderTemplateHelper.Resolve(template, "Videos", "Chan",
                               new DateTime(2026, 1, 1))!;
            System.IO.Path.IsPathRooted(result).Should().BeTrue();
            result.Should().Be(@"C:\Downloads\Chan\2026");
        }

        [Fact]
        public void Resolve_AbsolutePath_IsPathRooted()
        {
            var result = FolderTemplateHelper.Resolve(@"C:\Custom\Folder", "Videos", "Chat")!;
            System.IO.Path.IsPathRooted(result).Should().BeTrue();
        }

        [Fact]
        public void Resolve_RelativeTemplate_IsNotRooted()
        {
            var result = FolderTemplateHelper.Resolve("{ChatName}/{Year}", "Videos", "Chat",
                             new DateTime(2026, 5, 6))!;
            System.IO.Path.IsPathRooted(result).Should().BeFalse();
            result.Should().Be("Chat/2026");
        }

        [Fact]
        public void Resolve_AbsolutePath_DriveLetterNotSanitised()
        {
            // Colon in drive letter must not be replaced with underscore
            var result = FolderTemplateHelper.Resolve(@"C:\Some Folder", "Videos", "Chat")!;
            result.Should().Contain("C:");
            result.Should().NotContain("C_");
        }

        [Fact]
        public void BaseMessage_PathLocationFolder_AbsoluteTemplate_StillRootedAfterResolve()
        {
            // Callers use Path.IsPathRooted on the resolved value to detect absolute paths
            // and skip Path.Combine with basePath.
            var resolved = FolderTemplateHelper.Resolve(@"C:\DirectFolder", "Videos", "Chat");
            System.IO.Path.IsPathRooted(resolved!).Should().BeTrue();
        }
    }

    // ===========================================================================
    // FolderTemplateDialog — pure logic (no WPF host needed)
    // ===========================================================================

    public class FolderTemplateDialogLogicTests
    {
        // Mirrors the fixed UpdatePreview logic: always resolve tokens, then check
        // whether the RESULT is rooted (not the raw template).
        private static string BuildPreview(string template, string basePath, string type, string chatName)
        {
            if (string.IsNullOrWhiteSpace(template))
                return System.IO.Path.Combine(basePath, type, chatName);

            var resolved = FolderTemplateHelper.Resolve(template, type, chatName,
                               new DateTime(2026, 5, 6))
                           ?? System.IO.Path.Combine(type, chatName);

            return System.IO.Path.IsPathRooted(resolved)
                ? resolved
                : System.IO.Path.Combine(basePath, resolved);
        }

        [Fact]
        public void Preview_EmptyTemplate_ShowsDefaultPath()
        {
            var preview = BuildPreview("", @"D:\TG", "Videos", "MyChannel");
            preview.Should().Be(@"D:\TG\Videos\MyChannel");
        }

        [Fact]
        public void Preview_RelativeTemplate_CombinesWithBase()
        {
            var preview = BuildPreview("{ChatName}/{Year}", @"C:\TG", "Videos", "Chan");
            preview.Should().StartWith(@"C:\TG\").And.Contain("Chan").And.Contain("2026");
        }

        [Fact]
        public void Preview_AbsoluteTemplate_NoTokens_IgnoresBase()
        {
            var absolute = @"E:\Archive\Telegram";
            BuildPreview(absolute, @"C:\TG", "Videos", "Chan")
                .Should().Be(absolute);
        }

        [Fact]
        public void Preview_AbsoluteTemplate_WithChatNameToken_ResolvesToken()
        {
            // Bug fix: absolute paths with tokens must show resolved value in preview
            var template = @"C:\Downloads\{ChatName}";
            var preview  = BuildPreview(template, @"D:\Base", "Videos", "MySeries");
            preview.Should().Be(@"C:\Downloads\MySeries",
                because: "token must be substituted even when path is absolute");
        }

        [Fact]
        public void Preview_AbsoluteTemplate_WithDateTokens_ResolvesTokens()
        {
            var template = @"C:\Archive\{Year}\{Month}";
            // BuildPreview uses date 2026-05-06
            var preview  = BuildPreview(template, @"D:\Base", "Videos", "Chan");
            preview.Should().Be(@"C:\Archive\2026\05");
        }

        [Fact]
        public void Preview_AbsoluteTemplate_DoesNotDoubleRoot()
        {
            var absolute = @"C:\My Folder";
            var preview  = BuildPreview(absolute, @"D:\Base", "Group", "Chat");
            preview.Should().Be(absolute);
            preview.Should().NotContain(@"D:\Base");
        }

        [Fact]
        public void TokenInsertion_AtCursor_InsertsWithoutClearingExistingText()
        {
            // Simulate the fixed Token_Click logic: insert at cursor without wiping
            const string existingText = @"C:\Users\Desktop\";
            const string token        = "{ChatName}";
            int           cursorPos   = existingText.Length; // cursor at end

            var result = existingText.Insert(cursorPos, token);

            result.Should().Be(@"C:\Users\Desktop\{ChatName}",
                because: "token must be appended at the cursor without clearing the path");
        }

        [Fact]
        public void TokenInsertion_AtMiddleOfText_InsertsAtCorrectPosition()
        {
            const string existingText = @"C:\Desktop\suffix";
            const string token        = "{ChatName}\\";
            int           cursorPos   = @"C:\Desktop\".Length;

            var result = existingText.Insert(cursorPos, token);

            result.Should().Be(@"C:\Desktop\{ChatName}\suffix");
        }
    }
}
