using Microsoft.AspNetCore.Http;
using Serilog;

namespace TelegramAutoDownload.Headless;

public sealed class WebAuthMiddleware(RequestDelegate next, WebAuthService auth)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!auth.Enabled)
        {
            Log.Warning("Request served with web authentication disabled: {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            await next(ctx);
            return;
        }

        if (WebAuthService.IsPublicPath(ctx.Request.Path))
        {
            await next(ctx);
            return;
        }

        if (!auth.TryGetSession(ctx.Request, out _))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.CacheControl = "no-store";
            await ctx.Response.WriteAsJsonAsync(new { error = "Unauthorized", webAuthRequired = true });
            return;
        }

        if (auth.MustChangePassword && !WebAuthService.IsAllowedWhilePasswordChangePending(ctx.Request.Path))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Response.Headers.CacheControl = "no-store";
            await ctx.Response.WriteAsJsonAsync(new { error = "passwordChangeRequired" });
            return;
        }

        await next(ctx);
    }
}

public static class WebAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseWebAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<WebAuthMiddleware>();
}
