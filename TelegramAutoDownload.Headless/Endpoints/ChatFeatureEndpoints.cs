using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TelegramAutoDownload.Headless;
using TelegramAutoDownload.Headless.Security;
using TelegramClient.Models;

namespace TelegramAutoDownload.Headless.Endpoints;

public static class ChatFeatureEndpoints
{
    public static void MapChatFeatureEndpoints(this WebApplication app)
    {
        app.MapPatch("/api/chats/{id:long}/features", PatchChatFeatures);
        app.MapPost("/api/chats/{id:long}/mute", PostMuteChat);
        app.MapGet("/api/chats/{id:long}/reactions", GetChatReactions);
    }

    static IResult PatchChatFeatures(long id, HeadlessHost host, ChatFeaturesPatch patch)
    {
        foreach (var template in new[]
                 {
                     patch.SocialDownloadFolderTemplate, patch.YoutubeDownloadFolderTemplate,
                     patch.OtherDownloadFolderTemplate, patch.TorrentDownloadFolderTemplate,
                 })
        {
            if (!PathPolicy.IsValidFolderTemplate(template, out var error))
                return Results.BadRequest(new { error });
        }

        try
        {
            host.UpdateChatSettings(id, chat =>
            {
                if (patch.ReactionIcon != null) chat.ReactionIcon = patch.ReactionIcon;
                if (patch.DownloadStartIcon != null) chat.DownloadStartIcon = patch.DownloadStartIcon;
                if (patch.HistoryIcon != null) chat.HistoryIcon = patch.HistoryIcon;

                if (patch.SocialDownloadFolderTemplate != null)
                    chat.SocialDownloadFolderTemplate = ValidateFolderTemplateWrite(patch.SocialDownloadFolderTemplate);
                if (patch.YoutubeDownloadFolderTemplate != null)
                    chat.YoutubeDownloadFolderTemplate = ValidateFolderTemplateWrite(patch.YoutubeDownloadFolderTemplate);
                if (patch.OtherDownloadFolderTemplate != null)
                    chat.OtherDownloadFolderTemplate = ValidateFolderTemplateWrite(patch.OtherDownloadFolderTemplate);
                if (patch.TorrentDownloadFolderTemplate != null)
                    chat.TorrentDownloadFolderTemplate = ValidateFolderTemplateWrite(patch.TorrentDownloadFolderTemplate);
            });
            return Results.Json(new { ok = true });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    static async Task<IResult> PostMuteChat(long id, HeadlessHost host, MuteChatRequest req)
    {
        var cfg = host.ReadConfig();
        var chat = cfg.Chats.FirstOrDefault(c => c.Id == id);
        if (chat == null)
            return Results.NotFound(new { error = "Chat not found — refresh the chat list first." });
        if (host.Telegram == null)
            return Results.BadRequest(new { error = "Not logged in." });

        try
        {
            await host.Telegram.MuteChatAsync(chat, req.Muted).ConfigureAwait(false);
            host.UpdateChatSettings(id, c => c.Muted = req.Muted);
            return Results.Json(new { ok = true, muted = req.Muted });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    static async Task<IResult> GetChatReactions(long id, HeadlessHost host)
    {
        var cfg = host.ReadConfig();
        var chat = cfg.Chats.FirstOrDefault(c => c.Id == id);
        if (chat == null)
            return Results.NotFound(new { error = "Chat not found — refresh the chat list first." });
        if (host.Telegram == null)
            return Results.BadRequest(new { error = "Not logged in." });

        try
        {
            var reactions = await host.Telegram.GetChatAvailableReactionsAsync(chat).ConfigureAwait(false);
            return Results.Json(new { reactions });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Templates reaching here have already passed <see cref="PathPolicy.IsValidFolderTemplate"/>.</summary>
    static string ValidateFolderTemplateWrite(string template) =>
        template.Trim();

    internal record ChatFeaturesPatch(
        string? ReactionIcon,
        string? DownloadStartIcon,
        string? HistoryIcon,
        string? SocialDownloadFolderTemplate,
        string? YoutubeDownloadFolderTemplate,
        string? OtherDownloadFolderTemplate,
        string? TorrentDownloadFolderTemplate);

    internal record MuteChatRequest(bool Muted);
}
