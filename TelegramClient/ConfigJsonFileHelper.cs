using System;
using Serilog;
using System.IO;
using System.Text.Json;

namespace TelegramClient
{
    /// <summary>
    /// Atomic JSON file writes and corrupt-config recovery shared by WPF and Headless hosts.
    /// </summary>
    public static class ConfigJsonFileHelper
    {
        public static void WriteAtomic(string path, string json)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            if (File.Exists(path))
                File.Replace(tmpPath, path, destinationBackupFileName: null);
            else
                File.Move(tmpPath, path);
        }

        /// <summary>
        /// Reads JSON text; on parse failure backs up the file and returns null.
        /// </summary>
        public static string? ReadJsonOrBackupCorrupt(string path, out string? corruptBackupPath)
        {
            corruptBackupPath = null;
            var data = File.ReadAllText(path);
            try
            {
                JsonDocument.Parse(data);
                return data;
            }
            catch (JsonException ex)
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
                var backup = $"{path}.corrupt-{stamp}";
                try
                {
                    File.Copy(path, backup, overwrite: false);
                    corruptBackupPath = backup;
                }
                catch (Exception copyEx)
                {
                    Log.Warning(copyEx, "Failed to back up corrupt config at {Path}", path);
                }

                Log.Error(ex, "Config file at {Path} is corrupt; using defaults (backup: {Backup})", path, backup);
                return null;
            }
        }
    }
}
