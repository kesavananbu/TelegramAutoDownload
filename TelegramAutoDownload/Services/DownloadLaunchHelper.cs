using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace TelegramAutoDownload.Services
{
    public static class DownloadLaunchHelper
    {
        private static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".msi", ".scr", ".lnk", ".hta", ".com", ".pif", ".jar"
        };

        public static bool IsShellExecuteRiskyExtension(string path)
        {
            var ext = Path.GetExtension(path);
            return !string.IsNullOrEmpty(ext) && RiskyExtensions.Contains(ext);
        }

        public static void OpenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (File.Exists(path))
            {
                if (IsShellExecuteRiskyExtension(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                    {
                        UseShellExecute = true
                    });
                    return;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
