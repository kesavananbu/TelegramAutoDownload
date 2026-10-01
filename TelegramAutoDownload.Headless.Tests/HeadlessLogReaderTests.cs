using FluentAssertions;
using TelegramAutoDownload.Headless;
using TelegramAutoDownload.Headless.Logging;

namespace TelegramAutoDownload.Headless.Tests;

public sealed class HeadlessLogReaderTests : IDisposable
{
    private readonly string _logDir;
    private readonly HeadlessLogReader _reader = new();

    public HeadlessLogReaderTests()
    {
        _logDir = HeadlessPaths.LogsDir;
        Directory.CreateDirectory(_logDir);
    }

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_logDir, "headless-test-*.log"))
            File.Delete(f);
    }

    private string WriteSampleLog(string name, params string[] lines)
    {
        var path = Path.Combine(_logDir, name);
        File.WriteAllLines(path, lines);
        return name;
    }

    [Fact]
    public void ResolveSafeFileName_rejects_path_traversal_and_invalid_names()
    {
        var act1 = () => _reader.ResolveSafeFileName(@"..\..\secret.log");
        act1.Should().Throw<ArgumentException>();

        var act2 = () => _reader.ResolveSafeFileName("not-a-headless.log");
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResolveSafeFileName_accepts_basename_only()
    {
        var name = WriteSampleLog("headless-test-safe.log", "[INF] ok");
        _reader.ResolveSafeFileName(name).Should().Be(name);
    }

    [Fact]
    public void Tail_returns_matching_lines_with_level_filter()
    {
        var name = WriteSampleLog(
            "headless-test-tail.log",
            "[DBG] debug line",
            "[INF] info line",
            "[ERR] error line");

        var tail = _reader.Tail(name, maxLines: 10, minLevel: "ERR", search: null);
        tail.Lines.Should().HaveCount(1);
        tail.Lines[0].Text.Should().Contain("error line");
        tail.Lines[0].Level.Should().Be("ERR");
    }

    [Fact]
    public void Search_finds_case_insensitive_hits_with_paging()
    {
        var name = WriteSampleLog(
            "headless-test-search.log",
            "[INF] Alpha",
            "[INF] beta",
            "[INF] Gamma");

        var page = _reader.Search(name, "beta", skip: 0, limit: 10);
        page.Lines.Should().HaveCount(1);
        page.Lines[0].Text.Should().Contain("beta");
    }
}
