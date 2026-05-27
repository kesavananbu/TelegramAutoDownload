using Microsoft.AspNetCore.Http;

namespace TelegramAutoDownload.Headless;

public sealed class WebAuthMiddleware(RequestDelegate next, WebAuthService auth)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!auth.Enabled || WebAuthService.IsPublicPath(ctx.Request.Path))
        {
            await next(ctx);
            return;
        }

        if (auth.TryGetSession(ctx.Request, out _))
        {
            await next(ctx);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.Headers.CacheControl = "no-store";
        await ctx.Response.WriteAsJsonAsync(new { error = "Unauthorized", webAuthRequired = true });
    }
}

public static class WebAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseWebAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<WebAuthMiddleware>();
}
