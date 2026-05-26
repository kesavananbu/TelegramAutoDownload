using System.Collections.Concurrent;
using Serilog;
using TelegramAutoDownload.Headless.Data;
using TelegramClient;
using TelegramClient.Models;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Subscribes to <see cref="TelegramApp"/> live events and writes corresponding
/// rows to the SQLite store. Phase 2 runs alongside the existing in-memory queue;
/// the DB becomes the authoritative state of every tracked media.
///
/// State machine wiring:
///   OnMessageDiscovered → upsert(pending)             — message arrived
///   OnStarted           → in_progress                  — download began
///   OnSaved             → done, set downloaded_path    — finished OK
///   OnSkipped           → skipped                      — dedup / filter skip
///   OnComplete(false)   → failed                       — download error
/// </summary>
public sealed class MediaTracker
{
    private readonly MediaRepository _repo;

    // Resolve (chatName, messageId) → composite key. The TelegramApp callbacks
    // identify chats by display name, while the DB is keyed by numeric ID, so
    // we cache the most-recent mapping per chat.
    private readonly ConcurrentDictionary<string, long> _chatIdByName = new();
    // (chatName + msgId) → file_name captured from OnStarted for OnSaved matching
    private readonly ConcurrentDictionary<string, int> _lastMsgIdByChatFile = new();

    public MediaTracker(MediaRepository repo) { _repo = repo; }

    /// <summary>Wires the tracker into a freshly-created <see cref="TelegramApp"/>.</summary>
    public void Attach(TelegramApp app)
    {
        app.OnMessageDiscovered = OnMessageDiscovered;

        var prevStarted  = app.OnStarted;
        var prevSkipped  = app.OnSkipped;
        var prevComplete = app.OnComplete;

        app.OnStarted = (chatName, msgId) =>
        {
            try { _ = OnStartedAsync(chatName, msgId); } catch (Exception ex) { Log.Warning(ex, "MediaTracker.OnStarted"); }
            prevStarted?.Invoke(chatName, msgId);
        };
        app.OnSkipped = (chatName, msgId) =>
        {
            try { _ = OnSkippedAsync(chatName, msgId); } catch (Exception ex) { Log.Warning(ex, "MediaTracker.OnSkipped"); }
            prevSkipped?.Invoke(chatName, msgId);
        };
        app.OnComplete = (chatName, fileName, success) =>
        {
            try { _ = OnCompleteAsync(chatName, fileName, success); } catch (Exception ex) { Log.Warning(ex, "MediaTracker.OnComplete"); }
            prevComplete?.Invoke(chatName, fileName, success);
        };
    }

    private void OnMessageDiscovered(ChatDto chat, TL.Message msg)
    {
        _chatIdByName[chat.Name] = chat.Id;
        var rec = MessageMapper.FromMessage(chat, msg);
        if (rec == null) return;

        // Live-path media is fully owned by Client_OnUpdates — the orchestrator
        // must not pick it up — so we insert as pending and rely on OnStarted/OnSaved
        // to advance it through the state machine.
        _ = SafeRun(() => _repo.InsertPendingAsync(rec));
        if (rec.file_name != null)
            _lastMsgIdByChatFile[Key(chat.Name, rec.file_name)] = msg.ID;
    }

    private async Task OnStartedAsync(string chatName, int msgId)
    {
        if (!_chatIdByName.TryGetValue(chatName, out var chatId)) return;
        await _repo.SetStatusAsync(chatId, msgId, MediaStatus.InProgress);
    }

    private async Task OnSkippedAsync(string chatName, int msgId)
    {
        if (!_chatIdByName.TryGetValue(chatName, out var chatId)) return;
        await _repo.SetStatusAsync(chatId, msgId, MediaStatus.Skipped);
    }

    private async Task OnCompleteAsync(string chatName, string fileName, bool success)
    {
        if (!_chatIdByName.TryGetValue(chatName, out var chatId)) return;
        if (!_lastMsgIdByChatFile.TryGetValue(Key(chatName, fileName), out var msgId)) return;

        if (success)
            await _repo.SetStatusAsync(chatId, msgId, MediaStatus.Done);
        else
            await _repo.SetStatusAsync(chatId, msgId, MediaStatus.Failed, error: "Download reported failure");

        _lastMsgIdByChatFile.TryRemove(Key(chatName, fileName), out _);
    }

    private static string Key(string chatName, string fileName) => $"{chatName}::{fileName}";

    private static async Task SafeRun(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Log.Warning(ex, "MediaTracker DB write failed"); }
    }
}
