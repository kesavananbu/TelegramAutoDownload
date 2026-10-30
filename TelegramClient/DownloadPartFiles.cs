using System.IO;

namespace TelegramClient
{
    /// <summary>
    /// Helpers for .part resume files used by Telegram document downloads.
    /// </summary>
    public static class DownloadPartFiles
    {
        /// <summary>
        /// Renames a completed .part file only when its length equals the expected byte count.
        /// </summary>
        public static bool TryPromotePartToFinal(string partPath, string finalPath, long expectedSizeBytes, out string? errorMessage)
        {
            errorMessage = null;

            if (!File.Exists(partPath))
            {
                errorMessage = "Partial download file is missing.";
                return false;
            }

            var length = new FileInfo(partPath).Length;
            if (length != expectedSizeBytes)
            {
                errorMessage =
                    $"Partial file size ({length} bytes) does not match expected Telegram size ({expectedSizeBytes} bytes); " +
                    "keeping .part for resume.";
                return false;
            }

            File.Move(partPath, finalPath, overwrite: true);
            return true;
        }
    }
}
