using Serilog;
using BasePlugins;
using TelegramAutoDownload.Headless.Data;
using TelegramAutoDownload.Models;
using TelegramClient;
using TelegramClient.Models;
using TL;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Probes a chat by finding the newest N media messages, validating settings and
/// disk state, then attempting a real download for each — without writing to the queue DB.
/// </summary>
public sealed class ChatDownloadTester
{
    private const int MaxHistoryPages = 50;
    private const int HistoryPageSize = 100;

    private readonly HeadlessHost _host;
    private readonly MediaRepository _repo;
    private readonly ScanRateLimits _limits;
    private readonly FloodWaitTracker _floodWait;

    public ChatDownloadTester(
        HeadlessHost host,
        MediaRepository repo,
        ScanRateLimits limits,
        FloodWaitTracker floodWait)
    {
        _host = host;
        _repo = repo;
        _limits = limits;
        _floodWait = floodWait;
    }

    public async Task<TestDownloadReport> RunAsync(
        ChatDto chat,
        int sampleSize = 10,
        CancellationToken ct = default)
    {
        sampleSize = Math.Clamp(sampleSize, 1, 20);
        var setupLogs = new List<TestDownloadStepLog>();
        var items = new List<TestDownloadItemResult>();

        void Setup(string phase, bool ok, string detail)
        {
            setupLogs.Add(new TestDownloadStepLog(phase, ok, detail));
            Log.Information("[TestDownload] setup chat={Chat} phase={Phase} ok={Ok} {Detail}",
                chat.Name, phase, ok, detail);
        }

        var cfg = _host.ReadConfig();
        var app = _host.Telegram;
        if (app == null)
        {
            Setup("login", false, "Not logged in to Telegram.");
            return BuildReport(chat, sampleSize, 0, setupLogs, items,
                "Cannot run test — log in first.");
        }

        _host.EnsureDownloadPipelineReady();
        Setup("download_pipeline", app.IsDownloadPipelineReady,
            app.IsDownloadPipelineReady
                ? "FactoryMessagesService initialized."
                : "Download pipeline not initialized — UpdateConfig failed.");
        if (!app.IsDownloadPipelineReady)
        {
            return BuildReport(chat, sampleSize, 0, setupLogs, items,
                "Download pipeline not initialized. Restart the container or refresh chats, then retry.");
        }

        Setup("monitor", chat.Selected,
            chat.Selected ? "Monitor enabled." : "Monitor is OFF — enable before bootstrap for live capture.");

        Setup("download_types",
            chat.Download.Videos || chat.Download.Photos || chat.Download.Music || chat.Download.Files,
            $"V={chat.Download.Videos} P={chat.Download.Photos} M={chat.Download.Music} F={chat.Download.Files}");

        var downloadRoot = string.IsNullOrWhiteSpace(cfg.PathSaveFile)
            ? HeadlessPaths.DownloadsDir
            : cfg.PathSaveFile;

        var folderStep = CheckFolderWritable(downloadRoot);
        setupLogs.Add(folderStep);
        Log.Information("[TestDownload] setup chat={Chat} phase={Phase} ok={Ok} {Detail}",
            chat.Name, folderStep.Phase, folderStep.Ok, folderStep.Detail);
        Setup("download_folder", true, downloadRoot);

        InputPeer? peer;
        try
        {
            await _floodWait.WaitIfPausedAsync(ct).ConfigureAwait(false);
            peer = await app.ResolveInputPeerAsync(chat).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Setup("peer_resolve", false, ex.Message);
            return BuildReport(chat, sampleSize, 0, setupLogs, items,
                $"Cannot resolve chat peer — {ex.Message}");
        }

        if (peer == null)
        {
            Setup("peer_resolve", false, "ResolveInputPeerAsync returned null — refresh chats or re-login.");
            return BuildReport(chat, sampleSize, 0, setupLogs, items,
                "Cannot resolve chat peer. Try Refresh from Telegram on the Chats tab.");
        }

        Setup("peer_resolve", true, $"Peer resolved ({chat.Type}).");

        var samples = await CollectMediaSamplesAsync(app, chat, peer, sampleSize, ct)
            .ConfigureAwait(false);

        if (samples.Count == 0)
        {
            Setup("history_scan", false, "No trackable media found in recent history.");
            return BuildReport(chat, sampleSize, 0, setupLogs, items,
                "No downloadable media found in recent history — nothing to test.");
        }

        Setup("history_scan", true, $"Found {samples.Count} sample(s) (requested {sampleSize}).");

        foreach (var (msg, rec) in samples)
        {
            ct.ThrowIfCancellationRequested();
            items.Add(await ProbeItemAsync(app, chat, downloadRoot, msg, rec, ct).ConfigureAwait(false));
        }

        return BuildReport(chat, sampleSize, samples.Count, setupLogs, items, null);
    }

    private async Task<List<(Message Msg, MediaRecord Rec)>> CollectMediaSamplesAsync(
        TelegramApp app,
        ChatDto chat,
        InputPeer peer,
        int sampleSize,
        CancellationToken ct)
    {
        var samples = new List<(Message, MediaRecord)>();
        var seenMsgIds = new HashSet<int>();
        int offsetId = 0;
        int pages = 0;

        while (samples.Count < sampleSize && pages < MaxHistoryPages && !ct.IsCancellationRequested)
        {
            await _floodWait.WaitIfPausedAsync(ct).ConfigureAwait(false);
            try { await _limits.TelegramApi.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            Messages_MessagesBase history;
            try
            {
                history = await app.Client.Messages_GetHistory(peer, offset_id: offsetId, limit: HistoryPageSize)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (_floodWait != null && FloodWaitHelper.TryParseSeconds(ex, out _))
            {
                _floodWait.Report(ex, $"test-download:{chat.Name}");
                await _floodWait.WaitIfPausedAsync(ct).ConfigureAwait(false);
                continue;
            }

            app.CacheAccessHashesFromHistory(history);

            if (history.Messages.Length == 0) break;

            foreach (var raw in history.Messages)
            {
                if (raw is not Message msg) continue;
                if (!seenMsgIds.Add(msg.ID)) continue;

                var rec = MessageMapper.FromMessage(chat, msg);
                if (rec == null) continue;

                samples.Add((msg, rec));
                if (samples.Count >= sampleSize) break;
            }

            if (history.Messages.Length < HistoryPageSize) break;
            offsetId = history.Messages.Select(m => m.ID).Min();
            pages++;
        }

        Log.Information("[TestDownload] history chat={Chat} pages={Pages} samples={Count}",
            chat.Name, pages, samples.Count);
        return samples;
    }

    private async Task<TestDownloadItemResult> ProbeItemAsync(
        TelegramApp app,
        ChatDto chat,
        string downloadRoot,
        Message msg,
        MediaRecord rec,
        CancellationToken ct)
    {
        var steps = new List<TestDownloadStepLog>();
        void Step(string phase, bool ok, string detail)
        {
            steps.Add(new TestDownloadStepLog(phase, ok, detail));
            Log.Information("[TestDownload] item chat={Chat} msgId={MsgId} phase={Phase} ok={Ok} {Detail}",
                chat.Name, msg.ID, phase, ok, detail);
        }

        Step("classify", true, $"kind={rec.kind} file={rec.file_name ?? "—"} size={rec.size_bytes}");

        if (rec.status == MediaStatus.Skipped.ToDbValue())
        {
            Step("media_type", false, "Sticker or voice message — skipped by app policy.");
            return Item(rec, "skipped", steps, "Sticker/voice not downloaded");
        }

        var (typeOk, typeDetail) = CheckTypeEnabled(chat, rec);
        Step("type_toggle", typeOk, typeDetail);
        if (!typeOk)
            return Item(rec, "skipped", steps, typeDetail);

        if (rec.size_bytes > 0 && chat.DownloadFromSize > 0)
        {
            var sizeMb = rec.size_bytes / 1024.0 / 1024.0;
            var minOk = sizeMb >= chat.DownloadFromSize;
            Step("min_size", minOk,
                minOk
                    ? $"{sizeMb:F1} MB ≥ min {chat.DownloadFromSize} MB"
                    : $"{sizeMb:F1} MB below min {chat.DownloadFromSize} MB");
            if (!minOk)
                return Item(rec, "skipped", steps, "Below Min MB threshold");
        }

        var preflight = await ExistingDownloadValidator.CheckAsync(rec, chat, downloadRoot, _repo, ct)
            .ConfigureAwait(false);
        if (preflight != null)
        {
            Step("disk_preflight", true, preflight.Message);
            return Item(rec, preflight.MarkDone ? "skipped" : "skipped", steps, preflight.Message);
        }

        Step("disk_preflight", true, "No matching file in chat folder yet.");

        ResultExecute? result;
        try
        {
            await _floodWait.WaitIfPausedAsync(ct).ConfigureAwait(false);
            // Message came from history scan — use it directly (fresh file refs, no re-fetch).
            Step("message_source", true, $"Using msg {msg.ID} from history scan (skipping re-fetch).");
            result = await app.ExecuteMessageAsync(chat, msg).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Step("download", false, ex.Message);
            return Item(rec, "failed", steps, ex.Message);
        }

        if (result == null)
        {
            var detail = app.IsDownloadPipelineReady
                ? "Download pipeline returned no result."
                : "Download pipeline not initialized (UpdateConfig missing). Restart or refresh chats.";
            Step("download", false, detail);
            return Item(rec, "failed", steps, detail);
        }

        if (result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage))
        {
            Step("download", true,
                string.IsNullOrEmpty(result.FilePath)
                    ? $"Download OK — {result.FileName}"
                    : $"Download OK — {result.FilePath}");
            return Item(rec, "success", steps, null);
        }

        if (result.IsSuccess && !string.IsNullOrEmpty(result.ErrorMessage))
        {
            Step("download", true, result.ErrorMessage);
            return Item(rec, "skipped", steps, result.ErrorMessage);
        }

        var err = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "(no error text)" : result.ErrorMessage;
        Step("download", false, err);
        return Item(rec, "failed", steps, err);
    }

    private static TestDownloadItemResult Item(
        MediaRecord rec, string outcome, List<TestDownloadStepLog> steps, string? error) =>
        new(rec.message_id, rec.kind, rec.file_name, rec.size_bytes, outcome, steps, error);

    private static (bool Ok, string Detail) CheckTypeEnabled(ChatDto chat, MediaRecord rec)
    {
        var kind = MediaKindExtensions.ParseKind(rec.kind);
        return kind switch
        {
            MediaKind.Photo   => (chat.Download.Photos, chat.Download.Photos ? "Photos enabled" : "Photos disabled — enable P"),
            MediaKind.Video   => (chat.Download.Videos, chat.Download.Videos ? "Videos enabled" : "Videos disabled — enable V"),
            MediaKind.Audio   => (chat.Download.Music,  chat.Download.Music  ? "Music enabled"  : "Music disabled — enable M"),
            MediaKind.File    => (chat.Download.Files,  chat.Download.Files  ? "Files enabled"  : "Files disabled — enable F"),
            MediaKind.Torrent => (IsPluginEnabled(chat, "Torrent"),
                IsPluginEnabled(chat, "Torrent") ? "Torrent plugin enabled" : "Torrent disabled — enable T"),
            MediaKind.Url     => (HasAnyUrlPlugin(chat),
                HasAnyUrlPlugin(chat) ? "URL plugin enabled" : "No URL plugin enabled — enable Y/S/D"),
            _                 => (true, "Unknown kind — attempting download"),
        };
    }

    private static bool IsPluginEnabled(ChatDto chat, string name) =>
        chat.EnabledPlugins.TryGetValue(name, out var on) && on;

    private static bool HasAnyUrlPlugin(ChatDto chat) =>
        IsPluginEnabled(chat, "YouTube") ||
        IsPluginEnabled(chat, "SocialMedia") ||
        IsPluginEnabled(chat, "Other");

    private static TestDownloadStepLog CheckFolderWritable(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".tad_write_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return new TestDownloadStepLog("folder_writable", true, "Download folder is writable.");
        }
        catch (Exception ex)
        {
            return new TestDownloadStepLog("folder_writable", false, $"Download folder not writable: {ex.Message}");
        }
    }

    private static TestDownloadReport BuildReport(
        ChatDto chat,
        int requested,
        int found,
        IReadOnlyList<TestDownloadStepLog> setupLogs,
        IReadOnlyList<TestDownloadItemResult> items,
        string? forcedSummary)
    {
        var succeeded = items.Count(i => i.Outcome == "success");
        var skipped   = items.Count(i => i.Outcome == "skipped");
        var failed    = items.Count(i => i.Outcome == "failed");

        var setupOk = setupLogs.All(s => s.Ok || s.Phase is "monitor" or "download_types")
                    && setupLogs.FirstOrDefault(s => s.Phase == "folder_writable")?.Ok != false;
        var ready = setupOk && failed == 0 && found > 0 &&
                    setupLogs.FirstOrDefault(s => s.Phase == "peer_resolve")?.Ok == true &&
                    setupLogs.FirstOrDefault(s => s.Phase == "folder_writable")?.Ok != false;

        var summary = forcedSummary ?? (
            failed > 0
                ? $"Probe finished — {failed} failure(s) out of {found}. Fix issues before bootstrap."
                : succeeded == found
                    ? $"All {found} sample download(s) succeeded — safe to bootstrap."
                    : $"Probe finished — {succeeded} downloaded, {skipped} skipped, 0 failed. Review skips before bootstrap.");

        if (found < requested && forcedSummary == null)
            summary += $" (only {found} media item(s) found in recent history, requested {requested}).";

        return new TestDownloadReport(
            chat.Id, chat.Name, requested, found, succeeded, skipped, failed,
            ready, summary, setupLogs, items);
    }
}
