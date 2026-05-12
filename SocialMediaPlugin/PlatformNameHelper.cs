using System;

namespace SocialMediaPlugin
{
    /// <summary>
    /// Maps a URL host to a friendly folder name (YouTube, Facebook, TikTok, …).
    /// Handles subdomains (e.g. m.facebook.com) that would otherwise fall through to "SocialMedia".
    /// </summary>
    public static class PlatformNameHelper
    {
        public static string GetPlatformName(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return "SocialMedia";

            var host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal))
                host = host[4..];

            // Exact hosts first (same as legacy switch)
            return host switch
            {
                "youtube.com" or "youtu.be" => "YouTube",
                "facebook.com" or "fb.watch" or "fb.com" => "Facebook",
                "instagram.com" => "Instagram",
                "tiktok.com" or "vm.tiktok.com" or "vt.tiktok.com" => "TikTok",
                "x.com" or "twitter.com" or "t.co" => "X",
                "reddit.com" or "v.redd.it" => "Reddit",
                "twitch.tv" or "clips.twitch.tv" => "Twitch",
                "vimeo.com" => "Vimeo",
                "dailymotion.com" => "Dailymotion",
                "linkedin.com" => "LinkedIn",
                "pinterest.com" => "Pinterest",
                "snapchat.com" => "Snapchat",
                "threads.net" => "Threads",
                "rumble.com" => "Rumble",
                "odysee.com" => "Odysee",
                "bitchute.com" => "Bitchute",
                "streamable.com" => "Streamable",
                _ => GetPlatformNameBySuffix(host)
            };
        }

        private static string GetPlatformNameBySuffix(string host)
        {
            if (host.EndsWith(".facebook.com", StringComparison.Ordinal) || host == "fb.watch")
                return "Facebook";
            if (host.EndsWith(".fbcdn.net", StringComparison.Ordinal))
                return "Facebook";
            if (host.EndsWith(".instagram.com", StringComparison.Ordinal))
                return "Instagram";
            if (host.EndsWith(".tiktok.com", StringComparison.Ordinal))
                return "TikTok";
            if (host.EndsWith(".youtube.com", StringComparison.Ordinal) || host.EndsWith(".googlevideo.com", StringComparison.Ordinal))
                return "YouTube";
            if (host.EndsWith(".twitter.com", StringComparison.Ordinal) || host.EndsWith(".x.com", StringComparison.Ordinal))
                return "X";
            if (host.EndsWith(".reddit.com", StringComparison.Ordinal) || host == "redd.it")
                return "Reddit";
            if (host.EndsWith(".twitch.tv", StringComparison.Ordinal))
                return "Twitch";
            if (host.EndsWith(".vimeo.com", StringComparison.Ordinal))
                return "Vimeo";
            if (host.EndsWith(".dailymotion.com", StringComparison.Ordinal))
                return "Dailymotion";
            if (host.EndsWith(".linkedin.com", StringComparison.Ordinal))
                return "LinkedIn";
            if (host.EndsWith(".pinterest.com", StringComparison.Ordinal))
                return "Pinterest";

            return "SocialMedia";
        }
    }
}
