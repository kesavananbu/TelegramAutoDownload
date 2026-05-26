using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Serilog;
using TelegramAutoDownload.Headless;
using TelegramAutoDownload.Headless.Data;
using TelegramAutoDownload.Models;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(HeadlessPaths.LogsDir, "headless-.log"),
                  rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<MediaRepository>();
builder.Services.AddSingleton<HeadlessHost>();
builder.Services.AddSingleton<LoginCoordinator>();

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
        downloadThreads = cfg.DownloadThreads,
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

app.MapPost("/api/settings/threads", (HeadlessHost host, ThreadsRequest req) =>
{
    host.UpdateSettings(c => { c.DownloadThreads = Math.Clamp(req.Threads, 1, 10); });
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

app.MapPost("/api/chats/{id:long}/sync", async (long id, HeadlessHost host) =>
{
    _ = Task.Run(() => host.SyncHistoryAsync(id, msg => Log.Information("Sync: {Status}", msg)));
    return Results.Json(new { ok = true, message = "Sync started — watch /api/downloads for progress." });
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
    Results.Json((await repo.GetPerChatStatusCountsAsync())
        .GroupBy(r => r.chat_id)
        .Select(g => new
        {
            chatId = g.Key,
            total  = g.Sum(x => x.count),
            bytes  = g.Sum(x => x.total_bytes),
            byStatus = g.Select(x => new { status = x.status, count = x.count, bytes = x.total_bytes }),
        })));

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

app.MapGet("/api/status", (HeadlessHost host, LoginCoordinator login) =>
{
    var cfg = host.ReadConfig();
    return Results.Json(new
    {
        loggedIn        = login.IsLoggedIn,
        userId          = login.UserId,
        downloadFolder  = cfg.PathSaveFile,
        monitoredChats  = cfg.Chats.Count(c => c.Selected),
        totalChats      = cfg.Chats.Count,
        activeDownloads = host.SnapshotDownloads().Count(d => d.Status == DownloadStatus.Downloading),
    });
});

await app.RunAsync();

// ─── DTOs ────────────────────────────────────────────────────────────────────
internal record PhoneRequest(string Phone);
internal record CodeRequest(string Code);
internal record PasswordRequest(string Password);
internal record CredentialsRequest(int AppId, string ApiHash);
internal record FolderRequest(string Folder);
internal record ThreadsRequest(int Threads);

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
