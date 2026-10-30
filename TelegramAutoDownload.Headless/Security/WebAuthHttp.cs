using System.Net;
using Microsoft.AspNetCore.Http;

namespace TelegramAutoDownload.Headless.Security;

internal static class WebAuthHttp
{
    public static IPAddress? GetClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress;
}
