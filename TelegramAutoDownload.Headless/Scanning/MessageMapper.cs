using System.Security.Cryptography;
using System.Text;
using BasePlugins;
using TelegramAutoDownload.Headless.Data;
using TelegramClient;
using TelegramClient.Models;
using TL;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Pure functions that turn a Telegram <see cref="Message"/> into the SQLite row
/// shape used by Phase 1+ persistence. No I/O, no allocation beyond the result.
/// </summary>
public static class MessageMapper
{
    /// <summary>
    /// Returns a <see cref="MediaRecord"/> for the message, or null if the message
    /// has nothing trackable (no media, no URL, no magnet link).
    /// </summary>
    public static MediaRecord? FromMessage(ChatDto chat, Message msg)
    {
        // Native document/photo/audio/video
        if (msg.media is MessageMediaDocument { document: Document doc })
            return FromDocument(chat, msg, doc);

        if (msg.media is MessageMediaPhoto { photo: Photo photo })
            return FromPhoto(chat, msg, photo);

        // URL or magnet — text-only message
        if (!string.IsNullOrWhiteSpace(msg.message))
        {
            var magnet = MagnetLinkHelper.TryExtract(msg.message);
            if (magnet != null) return FromMagnet(chat, msg, magnet);

            var url = TryExtractFirstUrl(msg.message);
            if (url != null) return FromUrl(chat, msg, url);
        }

        return null;
    }

    private static MediaRecord FromDocument(ChatDto chat, Message msg, Document doc)
    {
        // Stickers / voice messages — the WPF app silently skips these
        var isSticker = doc.attributes?.Any(a => a is DocumentAttributeSticker) == true;
        var isVoice = doc.attributes?.Any(a => a is DocumentAttributeAudio audio &&
                                              audio.flags.HasFlag(DocumentAttributeAudio.Flags.voice)) == true;

        var kind = ClassifyDocument(doc);
        var rec = NewRecord(chat, msg);
        rec.document_id = doc.ID;
        rec.size_bytes  = doc.size;
        rec.kind        = kind.ToDbValue();
        rec.file_name   = GetDocumentFileName(doc, msg.ID);
        if (isSticker || isVoice)
            rec.status = MediaStatus.Skipped.ToDbValue();
        return rec;
    }

    private static MediaRecord FromPhoto(ChatDto chat, Message msg, Photo photo)
    {
        var rec = NewRecord(chat, msg);
        rec.document_id = photo.id;
        // Photo doesn't expose a single size — pick the largest size variant
        rec.size_bytes  = photo.sizes?.OfType<PhotoSize>().Select(s => (long)s.size).DefaultIfEmpty(0).Max() ?? 0;
        rec.kind        = MediaKind.Photo.ToDbValue();
        rec.file_name   = $"photo_{msg.ID}.jpg";
        return rec;
    }

    private static MediaRecord FromMagnet(ChatDto chat, Message msg, string magnet)
    {
        var rec = NewRecord(chat, msg);
        rec.url_hash  = Sha1(magnet);
        rec.kind      = MediaKind.Torrent.ToDbValue();
        rec.file_name = magnet.Length > 80 ? magnet[..80] + "…" : magnet;
        return rec;
    }

    private static MediaRecord FromUrl(ChatDto chat, Message msg, string url)
    {
        var rec = NewRecord(chat, msg);
        rec.url_hash  = Sha1(url);
        rec.kind      = MediaKind.Url.ToDbValue();
        rec.file_name = url.Length > 80 ? url[..80] + "…" : url;
        return rec;
    }

    private static MediaRecord NewRecord(ChatDto chat, Message msg) => new()
    {
        chat_id    = chat.Id,
        message_id = msg.ID,
        date_utc   = msg.date.ToUniversalTime().ToString("o"),
        status     = MediaStatus.Pending.ToDbValue(),
    };

    private static MediaKind ClassifyDocument(Document doc)
    {
        var mime = doc.mime_type ?? string.Empty;
        if (mime.Contains("video", StringComparison.OrdinalIgnoreCase)) return MediaKind.Video;
        if (mime.Contains("image", StringComparison.OrdinalIgnoreCase)) return MediaKind.Photo;
        if (mime.Contains("audio", StringComparison.OrdinalIgnoreCase)) return MediaKind.Audio;

        // Fall back to attribute inspection (e.g. video without explicit mime)
        if (doc.attributes != null)
        {
            foreach (var attr in doc.attributes)
            {
                if (attr is DocumentAttributeVideo) return MediaKind.Video;
                if (attr is DocumentAttributeAudio) return MediaKind.Audio;
                if (attr is DocumentAttributeImageSize) return MediaKind.Photo;
            }
        }
        return MediaKind.File;
    }

    private static string GetDocumentFileName(Document doc, int msgId)
    {
        if (doc.attributes != null)
        {
            foreach (var attr in doc.attributes)
            {
                if (attr is DocumentAttributeFilename fn && !string.IsNullOrEmpty(fn.file_name))
                    return fn.file_name;
                if (attr is DocumentAttributeVideo)
                    return $"video_{msgId}.mp4";
                if (attr is DocumentAttributeAudio audio)
                    return string.IsNullOrEmpty(audio.title) ? $"audio_{msgId}.mp3" : audio.title;
            }
        }
        return $"file_{msgId}";
    }

    private static string? TryExtractFirstUrl(string text)
    {
        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var ix = line.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            if (ix < 0) continue;
            var slice = line[ix..];
            // Stop at first whitespace
            var end = slice.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            return end > 0 ? slice[..end] : slice;
        }
        return null;
    }

    private static string Sha1(string s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
