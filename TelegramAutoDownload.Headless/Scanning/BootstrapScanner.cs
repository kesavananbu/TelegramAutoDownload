using Serilog;
using TelegramAutoDownload.Headless.Data;
using TelegramClient;
using TelegramClient.Models;
using TL;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// One-shot per-chat history sweep. Paginates <c>Messages_GetHistory</c>
/// newest → oldest, upserts every media-bearing message into the database
/// as <c>pending</c>. Resumable: re-running picks up from the lowest
/// <c>last_scanned_msg_id</c> still seen, so a partial run isn't wasted.
///
/// The orchestrator (Phase 2) is the consumer — once status flips to
/// <c>queued</c> (either at insert time when the chat is selected, or
/// later via a filter promotion), it will dispatch downloads.
/// </summary>
public sealed class BootstrapScanner
{
    private readonly TelegramApp _app;
    private readonly MediaRepository _repo;
    private readonly TokenBucketRateLimiter? _apiLimiter;
    private readonly FloodWaitTracker? _floodWait;
    private readonly int _pageSize;

    public BootstrapScanner(
        TelegramApp app,
        MediaRepository repo,
        TokenBucketRateLimiter? apiLimiter = null,
        FloodWaitTracker? floodWait = null,
        int pageSize = 100)
    {
        _app = app;
        _repo = repo;
        _apiLimiter = apiLimiter;
        _floodWait = floodWait;
        _pageSize = pageSize;
    }

    public async Task<BootstrapResult> RunAsync(
        ChatDto chat,
        Action<BootstrapProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        var peer = await _app.ResolveInputPeerAsync(chat).ConfigureAwait(false);
        if (peer == null)
        {
            Log.Warning("Bootstrap: could not resolve peer for chat {ChatName} ({ChatId})", chat.Name, chat.Id);
            return new BootstrapResult(chat.Id, 0, 0, "Could not resolve chat peer.");
        }

        int discovered = 0;
        int inserted   = 0;
        int offsetId   = 0;
        int highestSeen = 0;

        onProgress?.Invoke(new BootstrapProgress(chat.Id, 0, 0, "Starting…"));

        while (!ct.IsCancellationRequested)
        {
            // Consume one token per API page so the user-configurable rate limit
            // applies live without restarting the bootstrap.
            if (_apiLimiter != null)
            {
                try { await _apiLimiter.WaitAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }

            Messages_MessagesBase history;
            try
            {
                history = await _app.Client.Messages_GetHistory(peer, offset_id: offsetId, limit: _pageSize)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_floodWait != null && FloodWaitHelper.TryParseSeconds(ex, out var floodSec))
                {
                    _floodWait.Report(ex, $"bootstrap:{chat.Name}");
                    onProgress?.Invoke(new BootstrapProgress(chat.Id, discovered, inserted,
                        $"⚠ FLOOD_WAIT — Telegram paused API for {floodSec}s…"));
                    try { await _floodWait.WaitIfPausedAsync(ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                Log.Warning(ex, "Bootstrap: Messages_GetHistory failed for {ChatName} at offsetId={Offset}", chat.Name, offsetId);
                return new BootstrapResult(chat.Id, discovered, inserted, ex.Message);
            }

            if (history.Messages.Length == 0) break;

            var messages = history.Messages.OfType<Message>().ToList();
            highestSeen = Math.Max(highestSeen, messages.DefaultIfEmpty().Max(m => m?.ID ?? 0));

            foreach (var msg in messages)
            {
                if (ct.IsCancellationRequested) break;
                var rec = MessageMapper.FromMessage(chat, msg);
                if (rec == null) continue;
                discovered++;

                // Inserts default to status='pending'; the orchestrator + filter
                // promotion pass moves them to 'queued' when the chat is monitored.
                if (await _repo.InsertPendingAsync(rec).ConfigureAwait(false))
                    inserted++;
            }

            await _repo.UpsertScanStateAsync(chat.Id, highestSeen, bootstrapComplete: false)
                .ConfigureAwait(false);

            onProgress?.Invoke(new BootstrapProgress(chat.Id, discovered, inserted,
                $"Scanned to msg #{offsetId} — discovered {discovered}, inserted {inserted}"));

            // Use min ID across ALL returned messages (incl. service/empty) so a service-
            // heavy page does not stall pagination — same approach as SyncHistoryAsync.
            if (history.Messages.Length < _pageSize) break;
            offsetId = history.Messages.Select(m => m.ID).Min();
        }

        await _repo.UpsertScanStateAsync(chat.Id, highestSeen, bootstrapComplete: !ct.IsCancellationRequested)
            .ConfigureAwait(false);

        // Promote bootstrap-discovered rows to queued so the orchestrator can pick them up.
        // Only runs on successful completion — a cancelled run leaves them as pending so the
        // user can re-trigger bootstrap without re-queueing partial state.
        if (!ct.IsCancellationRequested)
        {
            var promoted = await _repo.PromotePendingToQueuedAsync(chat.Id).ConfigureAwait(false);
            Log.Information("Bootstrap {ChatName}: promoted {Count} rows pending → queued", chat.Name, promoted);
        }

        var summary = ct.IsCancellationRequested
            ? $"Cancelled — discovered {discovered}, inserted {inserted}"
            : $"Complete — discovered {discovered}, inserted {inserted}";
        onProgress?.Invoke(new BootstrapProgress(chat.Id, discovered, inserted, summary));
        return new BootstrapResult(chat.Id, discovered, inserted, ct.IsCancellationRequested ? "cancelled" : null);
    }
}

public sealed record BootstrapProgress(long ChatId, int Discovered, int Inserted, string Status);
public sealed record BootstrapResult(long ChatId, int Discovered, int Inserted, string? Error);
