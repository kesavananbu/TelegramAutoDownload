using System.Collections.Concurrent;
using System.Net;

namespace TelegramAutoDownload.Headless.Security;

public sealed class WebAuthLoginLimiter
{
    private readonly ConcurrentDictionary<string, FailureState> _byKey = new();
    private readonly object _lock = new();

    public bool IsLockedOut(string username, IPAddress? ip, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var now = DateTimeOffset.UtcNow;
        var userKey = "u:" + (username ?? "").Trim().ToLowerInvariant();
        var ipKey = "ip:" + (ip?.ToString() ?? "unknown");

        if (IsKeyLocked(this, userKey, now, out retryAfter)) return true;
        if (IsKeyLocked(this, ipKey, now, out retryAfter)) return true;
        return false;
    }

    public void RecordFailure(string username, IPAddress? ip)
    {
        var now = DateTimeOffset.UtcNow;
        RecordKeyFailure("u:" + (username ?? "").Trim().ToLowerInvariant(), now);
        RecordKeyFailure("ip:" + (ip?.ToString() ?? "unknown"), now);
    }

    public void RecordSuccess(string username, IPAddress? ip)
    {
        _byKey.TryRemove("u:" + (username ?? "").Trim().ToLowerInvariant(), out _);
        _byKey.TryRemove("ip:" + (ip?.ToString() ?? "unknown"), out _);
    }

    private static bool IsKeyLocked(WebAuthLoginLimiter limiter, string key, DateTimeOffset now, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!limiter._byKey.TryGetValue(key, out var state) || state.LockedUntil is null)
            return false;

        if (now >= state.LockedUntil)
        {
            limiter._byKey.TryRemove(key, out _);
            return false;
        }

        retryAfter = state.LockedUntil.Value - now;
        return true;
    }

    private void RecordKeyFailure(string key, DateTimeOffset now)
    {
        _byKey.AddOrUpdate(
            key,
            _ => new FailureState { ConsecutiveFailures = 1 },
            (_, existing) =>
            {
                existing.ConsecutiveFailures++;
                if (existing.ConsecutiveFailures >= 5)
                {
                    var exponent = Math.Min(existing.ConsecutiveFailures - 5, 4);
                    var minutes = 15 << exponent;
                    existing.LockedUntil = now.AddMinutes(minutes);
                }

                return existing;
            });
    }

    private sealed class FailureState
    {
        public int ConsecutiveFailures;
        public DateTimeOffset? LockedUntil;
    }
}
