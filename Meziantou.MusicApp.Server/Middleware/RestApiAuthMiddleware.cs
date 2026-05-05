namespace Meziantou.MusicApp.Server.Middleware;

public class RestApiAuthMiddleware(RequestDelegate next, ILogger<RestApiAuthMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Only check auth for REST API endpoints
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        logger.LogInformation("REST API request: {Method} {Path}", context.Request.Method, path);

        await next(context);
    }
}
