using System;
using System.IO;
using System.Net.Sockets;

namespace TelegramClient
{
    /// <summary>
    /// Detects transient network failures (timeouts, dropped connections) that are
    /// expected during idle periods or brief connectivity loss.
    /// </summary>
    public static class NetworkExceptionHelper
    {
        public static bool IsTransientNetworkError(Exception? ex)
        {
            while (ex != null)
            {
                if (ex is SocketException) return true;

                if (ex is IOException io &&
                    (io.Message.Contains("transport connection", StringComparison.OrdinalIgnoreCase) ||
                     io.Message.Contains("connection was closed", StringComparison.OrdinalIgnoreCase) ||
                     io.Message.Contains("Unable to read data", StringComparison.OrdinalIgnoreCase)))
                    return true;

                if (ex is AggregateException agg)
                {
                    foreach (var inner in agg.Flatten().InnerExceptions)
                    {
                        if (IsTransientNetworkError(inner)) return true;
                    }
                    return false;
                }

                ex = ex.InnerException;
            }

            return false;
        }

        public static bool IsBackgroundTelegramConnectionFailure(Exception? ex)
        {
            if (!IsTransientNetworkError(ex)) return false;

            var stack = ex!.ToString();
            return stack.Contains("KeepAlive", StringComparison.Ordinal) ||
                   stack.Contains(".Reactor(", StringComparison.Ordinal);
        }
    }
}
