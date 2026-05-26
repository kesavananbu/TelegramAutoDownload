using Serilog;
using TelegramClient;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Process-wide pause gate when Telegram returns FLOOD_WAIT.
/// All API limiters and scanners wait here before making the next call.
/// </summary>
public sealed class FloodWaitTracker
{
    private readonly object _lock = new();
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private string? _source;
    private string? _lastMessage;

    public void Report(Exception ex, string source)
    {
        if (!FloodWaitHelper.TryParseSeconds(ex, out var seconds)) return;

        var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
        lock (_lock)
        {
            if (until > _pausedUntil)
            {
                _pausedUntil  = until;
                _source       = source;
                _lastMessage  = ex.Message;
            }
        }

        Log.Warning("FLOOD_WAIT {Seconds}s from {Source} — API paused until {Until:u}",
            seconds, source, until);
    }

    public FloodWaitSnapshot Snapshot()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            if (_pausedUntil <= now)
                return FloodWaitSnapshot.Inactive;

            return new FloodWaitSnapshot(
                Active: true,
                PausedUntil: _pausedUntil,
                RemainingSeconds: (int)Math.Ceiling((_pausedUntil - now).TotalSeconds),
                Source: _source,
                Message: _lastMessage);
        }
    }

    /// <summary>Blocks until the global flood-wait window has elapsed.</summary>
    public async Task WaitIfPausedAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var snap = Snapshot();
            if (!snap.Active) return;

            ct.ThrowIfCancellationRequested();
            var delayMs = Math.Clamp(snap.RemainingSeconds * 1000, 500, 30_000);
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
    }
}

public sealed record FloodWaitSnapshot(
    bool Active,
    DateTimeOffset PausedUntil,
    int RemainingSeconds,
    string? Source,
    string? Message)
{
    public static FloodWaitSnapshot Inactive { get; } = new(false, default, 0, null, null);
}
