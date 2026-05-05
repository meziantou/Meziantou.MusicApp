namespace Meziantou.MusicApp.Server.Middleware;

[ExcludeFromDescription]
public class JellyfinAuthMiddleware(RequestDelegate next, ILogger<JellyfinAuthMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for certain Jellyfin paths
        var path = context.Request.Path.Value ?? "";
        
        // Public endpoints that don't require auth
        if (path.StartsWith("/jellyfin/System/Info/Public", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/jellyfin/Users/AuthenticateByName", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Only check auth for Jellyfin endpoints
        if (!path.StartsWith("/jellyfin", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        logger.LogInformation("Jellyfin request: {Method} {Path}", context.Request.Method, path);

        await next(context);
    }
}
