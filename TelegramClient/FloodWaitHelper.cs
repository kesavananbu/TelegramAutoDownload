using System;
using System.Text.RegularExpressions;
using TL;

namespace TelegramClient;

/// <summary>
/// Parses Telegram <c>FLOOD_WAIT_X</c> errors from RPC and wrapped exceptions.
/// </summary>
public static class FloodWaitHelper
{
    private static readonly Regex FloodWaitRegex =
        new(@"FLOOD_WAIT[_\s]*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParseSeconds(Exception? ex, out int seconds)
    {
        seconds = 0;
        while (ex != null)
        {
            if (ex is RpcException rpc && rpc.Code == 420)
            {
                if (TryParseMessage(rpc.Message, out seconds))
                    return true;
            }

            if (TryParseMessage(ex.Message, out seconds))
                return true;

            ex = ex.InnerException;
        }

        return false;
    }

    private static bool TryParseMessage(string? message, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrEmpty(message)) return false;
        var m = FloodWaitRegex.Match(message);
        return m.Success && int.TryParse(m.Groups[1].Value, out seconds) && seconds > 0;
    }
}
