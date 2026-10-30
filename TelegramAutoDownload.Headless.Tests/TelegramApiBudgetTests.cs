using FluentAssertions;
using TelegramAutoDownload.Headless;
using TelegramAutoDownload.Headless.Scanning;
using TelegramAutoDownload.Models;
using TelegramClient;

namespace TelegramAutoDownload.Headless.Tests;

public sealed class TelegramApiBudgetTests
{
    [Fact]
    public async Task TokenBucket_serializes_back_to_back_acquirers_to_capacity_one()
    {
        var tracker = new FloodWaitTracker();
        var limiter = new TokenBucketRateLimiter(() => new RateLimitOptions(1, 2), tracker);

        await limiter.WaitAsync();
        var second = Task.Run(() => limiter.WaitAsync());

        await Task.Delay(100);
        second.IsCompleted.Should().BeFalse();

        await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FloodWait_report_pauses_all_api_acquirers()
    {
        var tracker = new FloodWaitTracker();
        var limiter = new TokenBucketRateLimiter(() => new RateLimitOptions(5, 5), tracker);

        tracker.Report(TimeSpan.FromSeconds(2), "test");

        var acquired = false;
        var waitTask = Task.Run(async () =>
        {
            await limiter.WaitAsync();
            acquired = true;
        });

        await Task.Delay(200);
        acquired.Should().BeFalse();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
        acquired.Should().BeTrue();
    }

    [Fact]
    public void FloodWait_persisted_pause_survives_new_tracker_instance()
    {
        var first = new FloodWaitTracker();
        first.Report(TimeSpan.FromMinutes(10), "unit-test");

        var restored = new FloodWaitTracker();
        restored.Snapshot().Active.Should().BeTrue();
        restored.Snapshot().RemainingSeconds.Should().BeGreaterThan(60);

        try { File.Delete(Path.Combine(HeadlessPaths.DataDir, "flood-wait-state.json")); } catch { /* cleanup */ }
    }

    [Fact]
    public async Task Shared_download_slots_do_not_exceed_configured_limit()
    {
        var cfgPath = Path.Combine(HeadlessTestRunEnvironment.DataRoot, Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
        var store = new ConfigStore(cfgPath);
        store.Save(new ConfigParams { DownloadThreads = 2, ScannerApiCapacity = 10, ScannerApiRefillPerSecond = 10 });

        var tracker = new FloodWaitTracker();
        var budget = new TelegramApiBudget(new ScanRateLimits(store, tracker), tracker, store);
        budget.ConfigureDownloadConcurrency(2);

        budget.AvailableDownloadSlots.Should().Be(2);

        var slot1 = await budget.AcquireDownloadSlotAsync();
        budget.AvailableDownloadSlots.Should().Be(1);
        var slot2 = await budget.AcquireDownloadSlotAsync();
        budget.AvailableDownloadSlots.Should().Be(0);

        var blocked = Task.Run(async () =>
        {
            using (await budget.AcquireDownloadSlotAsync())
                return true;
        });

        await Task.Delay(100);
        blocked.IsCompleted.Should().BeFalse();

        slot2.Dispose();
        await blocked.WaitAsync(TimeSpan.FromSeconds(5));

        slot1.Dispose();
        budget.AvailableDownloadSlots.Should().Be(2);
    }
}
