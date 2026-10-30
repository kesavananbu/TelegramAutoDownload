namespace BasePlugins
{
    /// <summary>
    /// Global download size and disk-space gates. Hosts sync from <c>ConfigParams</c> on config read.
    /// </summary>
    public static class DownloadLimits
    {
        public const long DefaultFreeSpaceSafetyMarginBytes = 1L << 30; // 1 GiB

        /// <summary>Extra free bytes required on the target volume (default 1 GiB).</summary>
        public static long FreeSpaceSafetyMarginBytes { get; set; } = DefaultFreeSpaceSafetyMarginBytes;

        /// <summary>Maximum allowed download size in bytes; 0 = unlimited.</summary>
        public static long MaxDownloadFileSizeBytes { get; set; }

        /// <summary>
        /// Returns an error message when the download must not proceed, or null when allowed.
        /// </summary>
        public static string? ValidateDownloadAllowed(string destinationDirectory, long expectedSizeBytes)
        {
            if (expectedSizeBytes > 0 &&
                MaxDownloadFileSizeBytes > 0 &&
                expectedSizeBytes > MaxDownloadFileSizeBytes)
            {
                return $"File size ({expectedSizeBytes} bytes) exceeds configured maximum ({MaxDownloadFileSizeBytes} bytes)";
            }

            if (expectedSizeBytes <= 0)
                return null;

            try
            {
                var fullDir = Path.GetFullPath(destinationDirectory);
                var root = Path.GetPathRoot(fullDir);
                if (string.IsNullOrEmpty(root))
                    return null;

                var drive = new DriveInfo(root);
                // Host sync via ConfigParams.ApplyDownloadLimits(); tests may set 0 for no margin.
                var margin = Math.Max(0, FreeSpaceSafetyMarginBytes);
                var required = expectedSizeBytes + margin;
                if (drive.AvailableFreeSpace < required)
                {
                    return $"Insufficient disk space on {root.TrimEnd('\\', '/')}: need at least {required} bytes free " +
                           $"(file {expectedSizeBytes} bytes + {margin} bytes safety margin), " +
                           $"available {drive.AvailableFreeSpace} bytes";
                }
            }
            catch (Exception ex)
            {
                return $"Unable to verify free disk space: {ex.Message}";
            }

            return null;
        }
    }
}
