using System.Collections.Concurrent;
using Serilog;
using TelegramAutoDownload.Headless.Data;
using TelegramClient.Models;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Tracks running per-chat bootstrap jobs. At most one job per chat may run
/// concurrently — starting a new one cancels the previous. Status is queryable
/// for the UI poller.
/// </summary>
public sealed class BootstrapManager
{
    private readonly HeadlessHost _host;
    private readonly MediaRepository _repo;
    private readonly ScanRateLimits _limits;

    private readonly ConcurrentDictionary<long, BootstrapState> _running = new();

    public BootstrapManager(HeadlessHost host, MediaRepository repo, ScanRateLimits limits)
    {
        _host = host;
        _repo = repo;
        _limits = limits;
    }

    public void Start(ChatDto chat)
    {
        if (_host.Telegram == null) throw new InvalidOperationException("Not logged in.");

        // Cancel any prior run for this chat
        if (_running.TryGetValue(chat.Id, out var existing))
        {
            existing.Cts.Cancel();
            _running.TryRemove(chat.Id, out _);
        }

        var cts = new CancellationTokenSource();
        var state = new BootstrapState(chat.Id, chat.Name, cts);
        _running[chat.Id] = state;

        // Fire and forget — progress is exposed via Snapshot()
        _ = Task.Run(async () =>
        {
            try
            {
                var scanner = new BootstrapScanner(_host.Telegram!, _repo, _limits.TelegramApi);
                var result = await scanner.RunAsync(chat,
                    p => state.UpdateProgress(p.Discovered, p.Inserted, p.Status),
                    cts.Token).ConfigureAwait(false);
                state.MarkComplete(result.Error);
                Log.Information("Bootstrap for {ChatName}: discovered={Discovered}, inserted={Inserted}",
                    chat.Name, result.Discovered, result.Inserted);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Bootstrap for {ChatName} failed", chat.Name);
                state.MarkComplete(ex.Message);
            }
            finally
            {
                // Keep the state for a few minutes so the UI can poll the final result,
                // then drop it.
                _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ => _running.TryRemove(chat.Id, out BootstrapState? _));
            }
        });
    }

    public bool Cancel(long chatId)
    {
        if (!_running.TryGetValue(chatId, out var state)) return false;
        state.Cts.Cancel();
        return true;
    }

    public IReadOnlyList<BootstrapJobView> Snapshot() =>
        _running.Values.Select(s => s.ToView()).ToList();

    public BootstrapJobView? Get(long chatId) =>
        _running.TryGetValue(chatId, out var s) ? s.ToView() : null;

    private sealed class BootstrapState
    {
        public long ChatId { get; }
        public string ChatName { get; }
        public CancellationTokenSource Cts { get; }
        public int Discovered { get; private set; }
        public int Inserted { get; private set; }
        public string Status { get; private set; } = "starting";
        public bool Done { get; private set; }
        public string? Error { get; private set; }
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

        public BootstrapState(long chatId, string chatName, CancellationTokenSource cts)
        {
            ChatId = chatId; ChatName = chatName; Cts = cts;
        }

        public void UpdateProgress(int discovered, int inserted, string status)
        {
            Discovered = discovered;
            Inserted   = inserted;
            Status     = status;
            UpdatedAt  = DateTimeOffset.UtcNow;
        }

        public void MarkComplete(string? error)
        {
            Done = true;
            Error = error;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public BootstrapJobView ToView() => new(ChatId, ChatName, Discovered, Inserted, Status, Done, Error, StartedAt, UpdatedAt);
    }
}

public sealed record BootstrapJobView(
    long ChatId,
    string ChatName,
    int Discovered,
    int Inserted,
    string Status,
    bool Done,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt);
