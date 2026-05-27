using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Serilog;
using TelegramAutoDownload.Headless;
using TelegramAutoDownload.Headless.Data;
using TelegramAutoDownload.Headless.Logging;
using TelegramAutoDownload.Headless.Scanning;
using TelegramAutoDownload.Models;
using TelegramClient.Models;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(HeadlessPaths.LogsDir, "headless-.log"),
                  rollingInterval: RollingInterval.Day,
                  retainedFileCountLimit: 7,
                  shared: true,
                  outputTemplate: TelegramAutoDownload.Headless.Logging.HeadlessLogFormat.FileOutputTemplate)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<MediaRepository>();
builder.Services.AddSingleton<MediaTracker>();
builder.Services.AddSingleton<HeadlessHost>();
builder.Services.AddSingleton<LoginCoordinator>();
builder.Services.AddSingleton<FloodWaitTracker>();
builder.Services.AddSingleton<ScanRateLimits>();
builder.Services.AddSingleton<BootstrapManager>();
builder.Services.AddSingleton<ChatDownloadTester>();
builder.Services.AddSingleton<HeadlessLogReader>();
builder.Services.AddHostedService<DownloadOrchestrator>();

builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("LISTEN_URL") ?? "http://0.0.0.0:8080");

var app = builder.Build();

// Serve the static web UI from wwwroot/
app.UseDefaultFiles();
app.UseStaticFiles();

// Apply migrations + import legacy dedup index BEFORE the login probe runs,
// so the new DB is consistent and queryable from the first request.
var db   = app.Services.GetRequiredService<Database>();
var repo = app.Services.GetRequiredService<MediaRepository>();
await db.InitializeAsync();
await LegacyDedupMigration.RunAsync(repo);
Log.Information("Database ready at {Path}", HeadlessPaths.DatabaseFile);

// Probe the session in the background so a restored login is reflected immediately.
var probe = app.Services.GetRequiredService<LoginCoordinator>();
_ = Task.Run(async () =>
{
    try { await probe.ProbeAsync(); }
    catch (Exception ex) { Log.Warning(ex, "Initial login probe failed"); }
});

// ─── Login endpoints ────────────────────────────────────────────────────────
app.MapGet("/api/login/status", (LoginCoordinator login) =>
    Results.Json(new
    {
        stage    = login.Stage.ToString(),
        userId   = login.UserId,
        loggedIn = login.IsLoggedIn,
        error    = login.LastError,
    }));

app.MapPost("/api/login/phone", async (LoginCoordinator login, PhoneRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Phone)) return Results.BadRequest(new { error = "Phone is required" });
    await login.SubmitPhoneAsync(req.Phone);
    return Results.Json(new { stage = login.Stage.ToString(), error = login.LastError });
});

app.MapPost("/api/login/code", async (LoginCoordinator login, CodeRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Code)) return Results.BadRequest(new { error = "Code is required" });
    await login.SubmitCodeAsync(req.Code);
    return Results.Json(new { stage = login.Stage.ToString(), error = login.LastError });
});

app.MapPost("/api/login/password", async (LoginCoordinator login, PasswordRequest req) =>
{
    if (string.IsNullOrEmpty(req.Password)) return Results.BadRequest(new { error = "Password is required" });
    await login.SubmitPasswordAsync(req.Password);
    return Results.Json(new { stage = login.Stage.ToString(), error = login.LastError });
});

app.MapPost("/api/login/logout", async (LoginCoordinator login) =>
{
    await login.LogoutAsync();
    return Results.Json(new { stage = login.Stage.ToString() });
});

// ─── Settings & credentials ─────────────────────────────────────────────────
app.MapGet("/api/settings", (HeadlessHost host) =>
{
    var cfg = host.ReadConfig();
    return Results.Json(new
    {
        appIdConfigured = cfg.AppId != 0 && !string.IsNullOrEmpty(cfg.ApiHash),
        downloadFolder  = cfg.PathSaveFile,
        folderLayout    = cfg.FolderLayout.ToString(),
        downloadThreads = cfg.DownloadThreads,
        scannerApiCapacity        = cfg.ScannerApiCapacity,
        scannerApiRefillPerSecond = cfg.ScannerApiRefillPerSecond,
    });
});

app.MapPost("/api/settings/credentials", (HeadlessHost host, CredentialsRequest req) =>
{
    if (req.AppId == 0 || string.IsNullOrWhiteSpace(req.ApiHash))
        return Results.BadRequest(new { error = "AppId and ApiHash are required" });
    host.UpdateSettings(c => { c.AppId = req.AppId; c.ApiHash = req.ApiHash; });
    return Results.Json(new { ok = true });
});

app.MapPost("/api/settings/download-folder", (HeadlessHost host, FolderRequest req) =>
{
    host.UpdateSettings(c => { c.PathSaveFile = req.Folder; });
    return Results.Json(new { ok = true });
});

app.MapPost("/api/settings/folder-layout", (HeadlessHost host, FolderLayoutRequest req) =>
{
    if (!Enum.TryParse<FolderLayoutMode>(req.Layout, true, out var layout))
        return Results.BadRequest(new { error = "Layout must be TypeFirst, ChatFirst, or ChatCombined." });
    host.UpdateSettings(c => c.FolderLayout = layout);
    return Results.Json(new { ok = true, folderLayout = layout.ToString() });
});

app.MapPost("/api/settings/threads", (HeadlessHost host, ThreadsRequest req) =>
{
    host.UpdateSettings(c => { c.DownloadThreads = Math.Clamp(req.Threads, 1, 10); });
    return Results.Json(new { ok = true });
});

app.MapGet("/api/settings/limits", (HeadlessHost host, FloodWaitTracker flood, BootstrapManager mgr) =>
{
    var cfg = host.ReadConfig();
    return Results.Json(new
    {
        scannerApiCapacity        = cfg.ScannerApiCapacity,
        scannerApiRefillPerSecond = cfg.ScannerApiRefillPerSecond,
        downloadThreads           = cfg.DownloadThreads,
        allowParallelBootstrap    = cfg.AllowParallelBootstrap,
        maxParallelBootstraps     = cfg.MaxParallelBootstraps,
        downloadsPaused           = cfg.DownloadsPaused,
        activeBootstrapJobs       = mgr.ActiveCount,
        defaults = new
        {
            scannerApiCapacity        = RateLimitThresholds.ScannerCapacityDefault,
            scannerApiRefillPerSecond = RateLimitThresholds.ScannerRefillDefault,
            downloadThreads           = RateLimitThresholds.DownloadThreadsDefault,
            maxParallelBootstraps     = RateLimitThresholds.MaxParallelBootstrapsDefault,
        },
        thresholds = new
        {
            scannerCapacity = new { max = RateLimitThresholds.ScannerCapacityMax, warn = RateLimitThresholds.ScannerCapacityWarn, danger = RateLimitThresholds.ScannerCapacityDanger },
            scannerRefill   = new { max = RateLimitThresholds.ScannerRefillMax, warn = RateLimitThresholds.ScannerRefillWarn, danger = RateLimitThresholds.ScannerRefillDanger },
            downloadThreads = new { max = RateLimitThresholds.DownloadThreadsMax, warn = RateLimitThresholds.DownloadThreadsWarn, danger = RateLimitThresholds.DownloadThreadsDanger },
        },
        floodWait = ToFloodJson(flood.Snapshot()),
    });
});

app.MapPost("/api/settings/limits", (HeadlessHost host, LimitsRequest req) =>
{
    host.UpdateSettings(c =>
    {
        c.ScannerApiCapacity        = Math.Clamp(req.ScannerApiCapacity, 1.0, 100.0);
        c.ScannerApiRefillPerSecond = Math.Clamp(req.ScannerApiRefillPerSecond, 0.1, 50.0);
        if (req.DownloadThreads.HasValue)
            c.DownloadThreads = Math.Clamp(req.DownloadThreads.Value, 1, 10);
        if (req.AllowParallelBootstrap.HasValue)
            c.AllowParallelBootstrap = req.AllowParallelBootstrap.Value;
        if (req.MaxParallelBootstraps.HasValue)
            c.MaxParallelBootstraps = Math.Clamp(req.MaxParallelBootstraps.Value, 1, RateLimitThresholds.MaxParallelBootstrapsCap);
    });
    return Results.Json(new { ok = true });
});

// ─── Chat management ─────────────────────────────────────────────────────────
app.MapGet("/api/chats", (HeadlessHost host) =>
{
    var cfg = host.ReadConfig();
    return Results.Json(cfg.Chats.Select(ChatDtoView.From));
});

app.MapPost("/api/chats/refresh", async (HeadlessHost host) =>
{
    try
    {
        var chats = await host.RefreshChatsAsync();
        return Results.Json(chats.Select(ChatDtoView.From));
    }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPatch("/api/chats/{id:long}", (long id, HeadlessHost host, ChatPatch patch) =>
{
    try
    {
        host.UpdateChatSettings(id, chat =>
        {
            if (patch.Selected      .HasValue) chat.Selected           = patch.Selected.Value;
            if (patch.DownloadFromSize.HasValue) chat.DownloadFromSize = patch.DownloadFromSize.Value;
            if (patch.Videos        .HasValue) chat.Download.Videos    = patch.Videos.Value;
            if (patch.Photos        .HasValue) chat.Download.Photos    = patch.Photos.Value;
            if (patch.Music         .HasValue) chat.Download.Music     = patch.Music.Value;
            if (patch.Files         .HasValue) chat.Download.Files     = patch.Files.Value;
            if (patch.SaveHistory   .HasValue) chat.SaveHistory        = patch.SaveHistory.Value;
            if (patch.FolderTemplate != null)  chat.FolderTemplate     = patch.FolderTemplate;
            if (patch.Filter        != null)   chat.IgnoreFileByRegex  =
                patch.Filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (patch.Plugins != null)
                chat.EnabledPlugins = new Dictionary<string, bool>(patch.Plugins, StringComparer.OrdinalIgnoreCase);
        });
        return Results.Json(new { ok = true });
    }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
});

app.MapPost("/api/chats/{id:long}/sync", (long id, HeadlessHost host, BootstrapManager mgr, BootstrapRequest? req) =>
    TryStartBootstrap(id, host, mgr, req, requireMonitored: true));

app.MapGet("/api/flood-wait", (FloodWaitTracker flood) => Results.Json(ToFloodJson(flood.Snapshot())));

// ─── Logs (tail / search / live stream) ─────────────────────────────────────
app.MapGet("/api/logs/files", (HeadlessLogReader logs) => Results.Json(logs.ListFiles()));

app.MapGet("/api/logs/tail", (HeadlessLogReader logs, string? file, int? lines, string? level, string? search) =>
{
    try
    {
        var name = logs.ResolveSafeFileName(file);
        return Results.Json(logs.Tail(name, lines ?? 500, level, search));
    }
    catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/logs/search", (HeadlessLogReader logs, string? file, string q, int? skip, int? limit) =>
{
    try
    {
        var name = logs.ResolveSafeFileName(file);
        return Results.Json(logs.Search(name, q, skip ?? 0, limit ?? 100));
    }
    catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/logs/stream", async Task (HttpContext ctx, HeadlessLogReader logs, string? file, long? fromOffset, CancellationToken ct) =>
{
    string name;
    try
    {
        name = logs.ResolveSafeFileName(file);
    }
    catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }, ct);
        return;
    }

    var path = Path.Combine(HeadlessPaths.LogsDir, name);
    var offset = fromOffset ?? (File.Exists(path) ? new FileInfo(path).Length : 0L);

    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.ContentType = "text/event-stream";

    try
    {
        while (!ct.IsCancellationRequested)
        {
            var chunk = logs.ReadSinceOffset(name, ref offset);
            foreach (var line in chunk.Lines)
            {
                // SSE body must stay text/event-stream — WriteAsJsonAsync would reset Content-Type.
                var payload = JsonSerializer.Serialize(new { text = line.Text, level = line.Level });
                await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
            }
            if (chunk.Lines.Count > 0)
                await ctx.Response.Body.FlushAsync(ct);

            await Task.Delay(1000, ct);
        }
    }
    catch (OperationCanceledException) { /* client disconnected */ }
});

// ─── Queue / persisted-media stats (Phase 1: visibility only) ───────────────
app.MapGet("/api/queue/stats", async (MediaRepository repo) =>
{
    var rows   = await repo.GetGlobalStatusCountsAsync();
    var legacy = await repo.CountLegacyDedupAsync();
    var total  = rows.Sum(r => r.count);
    var totalBytes = rows.Sum(r => r.total_bytes);
    return Results.Json(new
    {
        total,
        totalBytes,
        legacyDedupCount = legacy,
        byStatus = rows.Select(r => new { status = r.status, count = r.count, bytes = r.total_bytes }),
    });
});

app.MapGet("/api/queue/stats/by-chat", async (MediaRepository repo) =>
{
    var scanMap = (await repo.GetAllScanStatesAsync())
        .ToDictionary(s => s.chat_id, s => s);
    return Results.Json((await repo.GetPerChatStatusCountsAsync())
        .GroupBy(r => r.chat_id)
        .Select(g =>
        {
            scanMap.TryGetValue(g.Key, out var scan);
            return new
            {
                chatId = g.Key,
                total  = g.Sum(x => x.count),
                bytes  = g.Sum(x => x.total_bytes),
                byStatus = g.Select(x => new { status = x.status, count = x.count, bytes = x.total_bytes }),
                lastScannedMsgId = scan?.last_scanned_msg_id ?? 0,
                lastScannedDate  = scan?.last_scanned_date,
                bootstrapComplete = scan?.bootstrap_complete == 1,
            };
        }));
});

app.MapGet("/api/queue/items", async (MediaRepository repo, string? status, int? limit) =>
{
    var s = string.IsNullOrWhiteSpace(status) ? "failed" : status.Trim().ToLowerInvariant();
    var rows = await repo.ListByStatusAsync(s, Math.Clamp(limit ?? 50, 1, 200));
    return Results.Json(rows.Select(r => new
    {
        chatId    = r.chat_id,
        messageId = r.message_id,
        fileName  = r.file_name,
        kind      = r.kind,
        sizeBytes = r.size_bytes,
        status    = r.status,
        lastError = r.last_error,
        attempts  = r.attempts,
    }));
});

// ─── Bootstrap (per-chat history sweep) ─────────────────────────────────────
app.MapPost("/api/chats/{id:long}/bootstrap", (long id, HeadlessHost host, BootstrapManager mgr, BootstrapRequest? req) =>
    TryStartBootstrap(id, host, mgr, req, requireMonitored: false));

app.MapPost("/api/chats/{id:long}/test-download", async (
    long id, HeadlessHost host, ChatDownloadTester tester, int? limit, CancellationToken ct) =>
{
    var cfg = host.ReadConfig();
    var chat = cfg.Chats.FirstOrDefault(c => c.Id == id);
    if (chat == null)
        return Results.NotFound(new { error = "Chat not found — refresh the chat list first." });
    if (host.Telegram == null)
        return Results.BadRequest(new { error = "Not logged in." });

    try
    {
        var report = await tester.RunAsync(chat, limit ?? 10, ct).ConfigureAwait(false);
        return Results.Json(report);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Test download failed for chat {ChatId}", id);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/chats/{id:long}/bootstrap", (long id, BootstrapManager mgr) =>
    Results.Json(new { cancelled = mgr.Cancel(id) }));

app.MapGet("/api/chats/{id:long}/bootstrap", (long id, BootstrapManager mgr) =>
{
    var s = mgr.Get(id);
    return s == null ? Results.NotFound() : Results.Json(s);
});

app.MapGet("/api/bootstrap/jobs", (BootstrapManager mgr) => Results.Json(mgr.Snapshot()));

// ─── Queue actions: retry / requeue a failed or skipped row ─────────────────
app.MapPost("/api/queue/{chatId:long}/{messageId:int}/retry",
    async (long chatId, int messageId, MediaRepository repo) =>
{
    await repo.SetStatusAsync(chatId, messageId, MediaStatus.Queued);
    return Results.Json(new { ok = true });
});

app.MapPost("/api/queue/retry-failed", async (MediaRepository repo, long? chatId) =>
{
    var requeued = await repo.RequeueAllFailedAsync(chatId);
    return Results.Json(new { ok = true, requeued });
});

app.MapPost("/api/queue/retry-skipped", async (MediaRepository repo, long? chatId) =>
{
    var requeued = await repo.RequeueAllSkippedAsync(chatId);
    return Results.Json(new { ok = true, requeued });
});

app.MapPost("/api/queue/delete-failed", async (MediaRepository repo, long? chatId) =>
{
    var deleted = await repo.DeleteAllFailedAsync(chatId);
    return Results.Json(new { ok = true, deleted });
});

app.MapPost("/api/queue/clear", async (MediaRepository repo, long chatId) =>
{
    if (chatId == 0) return Results.BadRequest(new { error = "chatId is required." });
    var deleted = await repo.ClearChatQueueAsync(chatId);
    return Results.Json(new { ok = true, deleted });
});

app.MapGet("/api/queue/downloads", (HeadlessHost host) =>
{
    var cfg = host.ReadConfig();
    return Results.Json(new { paused = cfg.DownloadsPaused });
});

app.MapPost("/api/queue/downloads", async (HeadlessHost host, MediaRepository repo, DownloadsPausedRequest req) =>
{
    host.UpdateSettings(c => c.DownloadsPaused = req.Paused);
    var requeued = 0;
    if (req.Paused)
        requeued = await repo.RequeueInProgressAsync();
    return Results.Json(new { ok = true, paused = req.Paused, requeuedInProgress = requeued });
});

// ─── Live status ────────────────────────────────────────────────────────────
app.MapGet("/api/downloads", (HeadlessHost host) =>
    Results.Json(host.SnapshotDownloads().Select(d => new
    {
        chat   = d.Chat,
        msgId  = d.MsgId,
        file   = d.FileName,
        plugin = d.Plugin,
        status = d.Status.ToString(),
        percent = Math.Round(d.Percent, 1),
        bytesDone  = d.BytesDone,
        bytesTotal = d.BytesTotal,
        error  = d.Error,
        at     = d.UpdatedAt,
    })));

app.MapGet("/api/status", (HeadlessHost host, LoginCoordinator login, FloodWaitTracker flood) =>
{
    var cfg = host.ReadConfig();
    return Results.Json(new
    {
        loggedIn        = login.IsLoggedIn,
        userId          = login.UserId,
        downloadFolder  = cfg.PathSaveFile,
        monitoredChats  = cfg.Chats.Count(c => c.Selected),
        totalChats      = cfg.Chats.Count,
        downloadsPaused = cfg.DownloadsPaused,
        activeDownloads = host.SnapshotDownloads().Count(d => d.Status == DownloadStatus.Downloading),
        floodWait       = ToFloodJson(flood.Snapshot()),
    });
});

await app.RunAsync();

static IResult TryStartBootstrap(long id, HeadlessHost host, BootstrapManager mgr, BootstrapRequest? req, bool requireMonitored)
{
    var cfg = host.ReadConfig();
    var chat = cfg.Chats.FirstOrDefault(c => c.Id == id);
    if (chat == null) return Results.NotFound(new { error = "Chat not found — refresh the chat list first." });
    if (host.Telegram == null) return Results.BadRequest(new { error = "Not logged in." });
    if (requireMonitored && !chat.Selected)
        return Results.BadRequest(new { error = "Enable monitoring first — checking the box only captures new messages; Bootstrap scans history through the queue." });

    var overrideParallel = req?.OverrideParallel ?? false;
    try
    {
        mgr.Start(chat, overrideParallel);
    }
    catch (BootstrapConflictException ex)
    {
        return Results.Json(new
        {
            error         = ex.Message,
            conflict      = true,
            canOverride   = true,
            blockingChat  = ex.BlockingChatName,
            blockingChatId = ex.BlockingChatId,
        }, statusCode: StatusCodes.Status409Conflict);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    return Results.Json(new
    {
        ok = true,
        overridden = overrideParallel,
        message = overrideParallel
            ? "Bootstrap started (parallel guard overridden) — scans share the same rate limiter. Watch the Queue tab."
            : "Bootstrap started — history is scanned rate-limited, queued in the database, then downloaded by the orchestrator. Watch the Queue tab.",
    });
}

static object ToFloodJson(FloodWaitSnapshot s) => new
{
    active           = s.Active,
    pausedUntil      = s.Active ? s.PausedUntil : (DateTimeOffset?)null,
    remainingSeconds = s.RemainingSeconds,
    source           = s.Source,
    message          = s.Message,
};

// ─── DTOs ────────────────────────────────────────────────────────────────────
internal record PhoneRequest(string Phone);
internal record CodeRequest(string Code);
internal record PasswordRequest(string Password);
internal record CredentialsRequest(int AppId, string ApiHash);
internal record FolderRequest(string Folder);
internal record FolderLayoutRequest(string Layout);
internal record ThreadsRequest(int Threads);
internal record LimitsRequest(
    double ScannerApiCapacity,
    double ScannerApiRefillPerSecond,
    int? DownloadThreads,
    bool? AllowParallelBootstrap,
    int? MaxParallelBootstraps);

internal record BootstrapRequest(bool OverrideParallel = false);

internal record DownloadsPausedRequest(bool Paused);

internal record ChatPatch(
    bool?  Selected,
    int?   DownloadFromSize,
    bool?  Videos, bool? Photos, bool? Music, bool? Files,
    bool?  SaveHistory,
    string? FolderTemplate,
    string? Filter,
    Dictionary<string, bool>? Plugins);

internal record ChatDtoView(
    long   Id,
    string Name,
    string Username,
    string Type,
    bool   Selected,
    int    DownloadFromSize,
    bool   Videos, bool Photos, bool Music, bool Files,
    string Filter,
    string FolderTemplate,
    bool   SaveHistory,
    Dictionary<string, bool> Plugins,
    int    MembersCount,
    bool   Muted)
{
    public static ChatDtoView From(TelegramClient.Models.ChatDto c) => new(
        c.Id, c.Name ?? "", c.Username ?? "", c.Type ?? "",
        c.Selected, c.DownloadFromSize,
        c.Download?.Videos ?? false, c.Download?.Photos ?? false,
        c.Download?.Music  ?? false, c.Download?.Files  ?? false,
        string.Join("; ", c.IgnoreFileByRegex ?? new List<string>()),
        c.FolderTemplate ?? "",
        c.SaveHistory,
        c.EnabledPlugins ?? new Dictionary<string, bool>(),
        c.MembersCount, c.Muted);
}
