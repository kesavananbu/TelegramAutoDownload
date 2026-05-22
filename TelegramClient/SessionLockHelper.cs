using System;
using System.IO;
using System.Threading;

namespace TelegramClient
{
    /// <summary>
    /// Handles session.dat file-lock races after updates or abrupt restarts.
    /// </summary>
    public static class SessionLockHelper
    {
        public static bool IsSessionLockedException(Exception ex)
        {
            while (ex != null)
            {
                if (ex is IOException io &&
                    (io.Message.Contains("session.dat", StringComparison.OrdinalIgnoreCase) ||
                     io.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)))
                    return true;
                ex = ex.InnerException;
            }
            return false;
        }

        /// <summary>
        /// Creates <see cref="TelegramApp"/> with retries when session.dat is temporarily locked.
        /// </summary>
        public static TelegramApp CreateTelegramAppWithRetry(
            int appId,
            string apiHash,
            Action<string>? onStatus = null,
            int maxAttempts = 20,
            int delayMs = 2000)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return new TelegramApp(appId, apiHash);
                }
                catch (Exception ex) when (IsSessionLockedException(ex))
                {
                    if (attempt >= maxAttempts)
                        throw;
                    onStatus?.Invoke($"Waiting for session… ({attempt}/{maxAttempts})");
                    Thread.Sleep(delayMs);
                }
            }

            throw new InvalidOperationException("CreateTelegramAppWithRetry failed unexpectedly.");
        }
    }
}
