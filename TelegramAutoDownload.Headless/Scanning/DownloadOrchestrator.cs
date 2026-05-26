using Microsoft.Extensions.Hosting;
using Serilog;
using TelegramAutoDownload.Headless.Data;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Background worker that drives the DB-backed download queue.
///
/// Loop:
///   1. Read <c>DownloadThreads</c> from config (hot-reload friendly).
///   2. While there is spare capacity, atomically claim up to <c>capacity</c>
///      rows in <c>queued</c> state (flipped to <c>in_progress</c> by the SQL).
///   3. Dispatch each via <see cref="TelegramClient.TelegramApp.ExecuteByMessageIdAsync"/>.
///   4. Update DB row to <c>done</c> / <c>skipped</c> / <c>failed</c> based on result.
///
/// The orchestrator only picks bootstrap-discovered items (the live update path
/// owns its own work and the MediaTracker advances those rows directly through
/// <c>pending → in_progress → done</c> without ever entering <c>queued</c>).
/// </summary>
public sealed class DownloadOrchestrator : BackgroundService
{
    private readonly MediaRepository _repo;
    private readonly HeadlessHost _host;
    private readonly FloodWaitTracker _floodWait;

    private static readonly TimeSpan IdlePollDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BusyPollDelay = TimeSpan.FromMilliseconds(500);

    private int _inFlight = 0;

    public DownloadOrchestrator(MediaRepository repo, HeadlessHost host, FloodWaitTracker floodWait)
    {
        _repo = repo;
        _host = host;
        _floodWait = floodWait;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        Log.Information("DownloadOrchestrator started");

        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (_host.Telegram == null || !_host.Telegram.IsConnected)
                {
                    await SafeDelay(IdlePollDelay, stop);
                    continue;
                }

                _host.EnsureDownloadPipelineReady();

                if (_host.ReadConfig().DownloadsPaused)
                {
                    await SafeDelay(IdlePollDelay, stop);
                    continue;
                }

                var maxConcurrent = Math.Max(1, _host.ReadConfig().DownloadThreads);
                var capacity = maxConcurrent - Volatile.Read(ref _inFlight);
                if (capacity <= 0)
                {
                    await SafeDelay(BusyPollDelay, stop);
                    continue;
                }

                var cfg = _host.ReadConfig();
                var monitoredChatIds = cfg.Chats.Where(c => c.Selected).Select(c => c.Id).ToList();
                var batch = await _repo.PickQueuedAsync(capacity, monitoredChatIds).ConfigureAwait(false);
                if (batch.Count == 0)
                {
                    await SafeDelay(IdlePollDelay, stop);
                    continue;
                }

                foreach (var row in batch)
                {
                    Interlocked.Increment(ref _inFlight);
                    _ = Task.Run(() => DispatchAsync(row, stop), stop);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DownloadOrchestrator loop error — backing off");
                await SafeDelay(IdlePollDelay, stop);
            }
        }

        Log.Information("DownloadOrchestrator stopped");
    }

    private async Task DispatchAsync(MediaRecord row, CancellationToken stop)
    {
        try
        {
            await _floodWait.WaitIfPausedAsync(stop).ConfigureAwait(false);

            var cfg = _host.ReadConfig();
            var chat = cfg.Chats.FirstOrDefault(c => c.Id == row.chat_id);
            if (chat == null)
            {
                await _repo.SetStatusAsync(row.chat_id, row.message_id, MediaStatus.Failed,
                    "Chat removed from config — cannot dispatch").ConfigureAwait(false);
                return;
            }

            var downloadRoot = cfg.PathSaveFile;
            if (string.IsNullOrWhiteSpace(downloadRoot))
                downloadRoot = HeadlessPaths.DownloadsDir;

            var existing = await ExistingDownloadValidator.CheckAsync(row, chat, downloadRoot, _repo, cfg.FolderLayout, stop)
                .ConfigureAwait(false);
            if (existing != null)
            {
                var status = existing.MarkDone ? MediaStatus.Done : MediaStatus.Skipped;
                await _repo.SetStatusAsync(row.chat_id, row.message_id, status, existing.Message)
                    .ConfigureAwait(false);
                if (existing.MarkDone)
                    await _repo.SetDownloadedPathAsync(row.chat_id, row.message_id, existing.Path)
                        .ConfigureAwait(false);
                Log.Information(
                    "Preflight skip chat={ChatId} msg={MsgId}: {Detail} → {Path}",
                    row.chat_id, row.message_id, existing.Message, existing.Path);
                return;
            }

            // Defer cleanly to the existing factoryService pipeline, which already enforces
            // per-chat filters (download types, min size, ignore regex), routes by plugin,
            // and updates the dedup index. The orchestrator stays a thin dispatcher.
            var result = await _host.Telegram!.ExecuteByMessageIdAsync(chat, row.message_id)
                .ConfigureAwait(false);

            if (result == null)
            {
                await _repo.SetStatusAsync(row.chat_id, row.message_id, MediaStatus.Failed,
                    "Message could not be re-fetched from Telegram").ConfigureAwait(false);
                return;
            }

            // ExecuteDirectAsync semantics (mirrored from the WPF flow):
            //   IsSuccess + empty ErrorMessage           → real download succeeded
            //   IsSuccess + non-empty ErrorMessage       → benign skip (dedup / filter)
            //   !IsSuccess                                → real failure
            if (result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage))
            {
                await _repo.SetStatusAsync(row.chat_id, row.message_id, MediaStatus.Done).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(result.FileName))
                    await _repo.SetDownloadedPathAsync(row.chat_id, row.message_id, result.FileName).ConfigureAwait(false);
            }
            else if (result.IsSuccess)
            {
                await _repo.SetStatusAsync(row.chat_id, row.message_id, MediaStatus.Skipped, result.ErrorMessage)
                    .ConfigureAwait(false);
            }
            else
            {
                await _repo.SetStatusAsync(row.chat_id, row.message_id, MediaStatus.Failed, result.ErrorMessage)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex) when (TelegramClient.FloodWaitHelper.TryParseSeconds(ex, out var floodSec))
        {
            _floodWait.Report(ex, $"download:chat{row.chat_id}:msg{row.message_id}");
            Log.Warning("Orchestrator FLOOD_WAIT {Seconds}s — re-queuing chat {ChatId} msg {MsgId}",
                floodSec, row.chat_id, row.message_id);
            try
            {
                await _repo.SetStatusAsync(row.chat_id, row.message_id, MediaStatus.Queued,
                    $"FLOOD_WAIT {floodSec}s — will retry").ConfigureAwait(false);
            }
            catch { /* next poll */ }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Orchestrator dispatch failed for chat {ChatId} msg {MsgId}", row.chat_id, row.message_id);
            try
            {
                await _repo.SetStatusAsync(row.chat_id, row.message_id, MediaStatus.Failed, ex.Message)
                    .ConfigureAwait(false);
            }
            catch { /* swallow — we'll see it in the next poll */ }
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* stopping */ }
    }
}
