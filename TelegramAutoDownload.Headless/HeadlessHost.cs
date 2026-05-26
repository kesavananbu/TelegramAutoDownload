using System.Collections.Concurrent;
using Serilog;
using TelegramAutoDownload.Headless.Scanning;
using TelegramAutoDownload.Models;
using TelegramClient;
using TelegramClient.Models;

namespace TelegramAutoDownload.Headless;

public enum DownloadStatus { Queued, Downloading, Done, Error, Skipped }

public sealed record DownloadEntry(
    string Chat,
    int    MsgId,
    string FileName,
    string Plugin,
    DownloadStatus Status,
    double Percent,
    long   BytesDone,
    long   BytesTotal,
    string? Error,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Holds the singleton <see cref="TelegramApp"/> instance, applies user config changes,
/// and keeps an in-memory view of the active/recent downloads for the web UI to poll.
/// </summary>
public sealed class HeadlessHost
{
    private readonly ConfigStore _config;
    private readonly MediaTracker? _tracker;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, DownloadEntry> _downloads = new();

    public TelegramApp? Telegram { get; private set; }

    public HeadlessHost(ConfigStore config, MediaTracker? tracker = null)
    {
        _config = config;
        _tracker = tracker;
    }

    /// <summary>Initialise <see cref="Telegram"/> lazily; reuses the existing instance on subsequent calls.</summary>
    public async Task EnsureTelegramAsync()
    {
        if (Telegram != null) return;

        var cfg = _config.Read();
        if (cfg.AppId == 0 || string.IsNullOrEmpty(cfg.ApiHash))
            throw new InvalidOperationException(
                "APP_ID and API_HASH are required. Set them via env vars (APP_ID, API_HASH) " +
                "or POST /api/settings/credentials before logging in.");

        Telegram = new TelegramApp(cfg.AppId, cfg.ApiHash);
        await Telegram.WaitForLoginAsync(2000);
        WireDownloadEvents(Telegram);
        // Attach DB tracker AFTER the live event wiring so MediaTracker layers on top
        // (its handlers chain rather than replace the existing in-memory ones).
        _tracker?.Attach(Telegram);
    }

    /// <summary>Called by LoginCoordinator after a successful login — applies the saved config.</summary>
    public Task OnLoggedInAsync()
    {
        if (Telegram == null) return Task.CompletedTask;
        var cfg = _config.Read();
        Telegram.UpdateConfig(cfg);
        return Task.CompletedTask;
    }

    public async Task LogoutAsync()
    {
        if (Telegram == null) return;
        await Telegram.LogoutAsync();
        Telegram = null;
        _downloads.Clear();
    }

    private void WireDownloadEvents(TelegramApp app)
    {
        app.OnEnqueued = (chat, msgId, name) => Upsert(chat, msgId, name, "", DownloadStatus.Queued, 0, 0, 0, null);
        app.OnStarted  = (chat, msgId)        => UpsertStarted(chat, msgId);
        app.OnSkipped  = (chat, msgId)        => Remove(chat, msgId);
        app.OnProgress = (chat, file, plugin, percent, done, total) =>
            UpsertByName(chat, file, plugin, DownloadStatus.Downloading, percent, done, total, null);
        app.OnComplete = (chat, file, success) =>
            UpsertByName(chat, file, null, success ? DownloadStatus.Done : DownloadStatus.Error, 100, 0, 0,
                         success ? null : "Download failed");
    }

    private static string Key(string chat, int msgId) => $"{chat}#{msgId}";
    private static string KeyByName(string chat, string file) => $"{chat}|{file}";

    private void Upsert(string chat, int msgId, string file, string plugin,
                        DownloadStatus status, double pct, long done, long total, string? err)
    {
        var entry = new DownloadEntry(chat, msgId, file, plugin, status, pct, done, total, err, DateTimeOffset.UtcNow);
        _downloads[Key(chat, msgId)] = entry;
    }

    private void UpsertStarted(string chat, int msgId)
    {
        if (_downloads.TryGetValue(Key(chat, msgId), out var prev))
            _downloads[Key(chat, msgId)] = prev with { Status = DownloadStatus.Downloading, UpdatedAt = DateTimeOffset.UtcNow };
    }

    private void UpsertByName(string chat, string file, string? plugin, DownloadStatus status,
                              double pct, long done, long total, string? err)
    {
        // Progress events arrive by filename; find the matching msg-id entry.
        var match = _downloads.Values
            .Where(d => d.Chat == chat && (d.FileName == file || d.FileName.EndsWith(file)))
            .OrderByDescending(d => d.UpdatedAt)
            .FirstOrDefault();
        var key = match != null ? Key(match.Chat, match.MsgId) : KeyByName(chat, file);
        var updated = new DownloadEntry(
            chat,
            match?.MsgId ?? 0,
            file,
            plugin ?? match?.Plugin ?? "",
            status,
            pct,
            done == 0 ? match?.BytesDone  ?? 0 : done,
            total == 0 ? match?.BytesTotal ?? 0 : total,
            err,
            DateTimeOffset.UtcNow);
        _downloads[key] = updated;

        // Trim completed/error rows older than 60s so the snapshot stays small.
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60);
        foreach (var kv in _downloads.Where(p =>
                     (p.Value.Status == DownloadStatus.Done || p.Value.Status == DownloadStatus.Error)
                     && p.Value.UpdatedAt < cutoff).ToList())
            _downloads.TryRemove(kv.Key, out _);
    }

    private void Remove(string chat, int msgId) => _downloads.TryRemove(Key(chat, msgId), out _);

    public IReadOnlyList<DownloadEntry> SnapshotDownloads() =>
        _downloads.Values.OrderByDescending(d => d.UpdatedAt).Take(200).ToList();

    /// <summary>Refreshes the chat list from Telegram and merges it with saved per-chat settings.</summary>
    public async Task<IList<ChatDto>> RefreshChatsAsync()
    {
        if (Telegram == null) throw new InvalidOperationException("Not logged in.");

        var cfg = _config.Read();
        var fresh = await Telegram.GetAllChats();

        foreach (var chat in fresh)
        {
            chat.NameLower = chat.Name?.ToLowerInvariant() ?? string.Empty;
            chat.UsernameLower = chat.Username?.ToLowerInvariant() ?? string.Empty;

            var saved = cfg.Chats.FirstOrDefault(c => c.Id == chat.Id);
            if (saved == null) continue;

            chat.Selected           = saved.Selected;
            chat.ReactionIcon       = saved.ReactionIcon;
            chat.DownloadStartIcon  = saved.DownloadStartIcon;
            if (saved.Download != null)
            {
                chat.Download.Videos = saved.Download.Videos;
                chat.Download.Photos = saved.Download.Photos;
                chat.Download.Music  = saved.Download.Music;
                chat.Download.Files  = saved.Download.Files;
            }
            chat.DownloadFromSize    = saved.DownloadFromSize;
            chat.IgnoreFileByRegex   = saved.IgnoreFileByRegex;
            chat.EnabledPlugins      = saved.EnabledPlugins ?? new Dictionary<string, bool>();
            chat.FolderTemplate      = saved.FolderTemplate ?? string.Empty;
            chat.SocialDownloadFolderTemplate  = saved.SocialDownloadFolderTemplate ?? string.Empty;
            chat.YoutubeDownloadFolderTemplate = saved.YoutubeDownloadFolderTemplate ?? string.Empty;
            chat.OtherDownloadFolderTemplate   = saved.OtherDownloadFolderTemplate ?? string.Empty;
            chat.TorrentDownloadFolderTemplate = saved.TorrentDownloadFolderTemplate ?? string.Empty;
            chat.SaveHistory         = saved.SaveHistory;
            chat.HistoryIcon         = saved.HistoryIcon ?? string.Empty;
            chat.Muted               = saved.Muted;
        }

        // Persist the refreshed list so the saved config has the current set of dialogs
        // (selection state for previously-unknown chats defaults to false).
        cfg.Chats = fresh.ToList();
        _config.Save(cfg);

        if (Telegram != null)
            Telegram.UpdateConfig(cfg);

        return fresh;
    }

    /// <summary>
    /// Patches one chat's settings (selected, types, providers, etc.) and reloads the
    /// running engine so changes apply immediately.
    /// </summary>
    public void UpdateChatSettings(long chatId, Action<ChatDto> mutate)
    {
        lock (_lock)
        {
            var cfg = _config.Read();
            var chat = cfg.Chats.FirstOrDefault(c => c.Id == chatId)
                ?? throw new KeyNotFoundException($"Chat {chatId} not found — refresh the chat list first.");
            mutate(chat);
            _config.Save(cfg);

            Telegram?.UpdateConfig(cfg);
        }
    }

    public void UpdateSettings(Action<ConfigParams> mutate)
    {
        lock (_lock)
        {
            var cfg = _config.Read();
            mutate(cfg);
            _config.Save(cfg);
            Telegram?.UpdateConfig(cfg);
        }
    }

    public Task SyncHistoryAsync(long chatId, Action<string>? onStatus)
    {
        var cfg = _config.Read();
        var chat = cfg.Chats.FirstOrDefault(c => c.Id == chatId);
        if (chat == null || Telegram == null)
            return Task.CompletedTask;
        return Telegram.SyncHistoryAsync(chat, onStatus);
    }

    public ConfigParams ReadConfig() => _config.Read();
}
