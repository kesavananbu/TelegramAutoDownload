using Serilog;
using System;
using System.IO;

namespace TelegramAutoDownload.Services
{
    /// <summary>
    /// Owns Serilog initialization so log files can be deleted safely while the app runs.
    /// Serilog keeps today's rolling log file open — delete requires closing the logger first.
    /// </summary>
    public static class AppLogService
    {
        public static void Initialize()
        {
            Log.Logger = CreateLogger();
        }

        public static void Shutdown()
        {
            Log.CloseAndFlush();
        }

        /// <summary>
        /// True when Serilog is currently writing to this file (today's rolling log).
        /// </summary>
        public static bool IsActiveLogFile(string path)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith($"app-{DateTime.Now:yyyyMMdd}", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Deletes a log file. If it is the active Serilog target, closes the logger first.
        /// </summary>
        public static void DeleteLogFile(string path)
        {
            if (!File.Exists(path))
                return;

            if (IsActiveLogFile(path))
            {
                Shutdown();
                try
                {
                    File.Delete(path);
                }
                finally
                {
                    Initialize();
                    Log.Information("Log file deleted: {File}", Path.GetFileName(path));
                }
                return;
            }

            File.Delete(path);
        }

        private static Serilog.Core.Logger CreateLogger()
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            return new LoggerConfiguration()
                .WriteTo.File(
                    Path.Combine(AppPaths.LogsDir, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true,
                    outputTemplate: SerilogFileSettings.FileOutputTemplate)
                .WriteTo.Sink(new LogAlertSink(SerilogFileSettings.FileOutputTemplate))
                .CreateLogger();
        }
    }
}
