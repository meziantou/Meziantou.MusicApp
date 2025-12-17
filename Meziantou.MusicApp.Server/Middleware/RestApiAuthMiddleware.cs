using System.Text.Json;
using Meziantou.MusicApp.Server.Models;
using Microsoft.Extensions.Options;

namespace Meziantou.MusicApp.Server.Middleware;

public class RestApiAuthMiddleware(RequestDelegate next, IOptions<MusicServerSettings> commonSettings, ILogger<RestApiAuthMiddleware> logger)
{
    private readonly MusicServerSettings _commonSettings = commonSettings.Value;

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

        // Check for authentication token
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

        if (string.IsNullOrEmpty(_commonSettings.AuthToken))
        {
            // No authentication required
            await next(context);
            return;
        }

        if (string.IsNullOrEmpty(authHeader))
        {
            await WriteUnauthorized(context);
            return;
        }

        // Parse Bearer token
        var authenticated = false;
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.Substring(7);
            authenticated = token == _commonSettings.AuthToken;
        }

        if (!authenticated)
        {
            logger.LogWarning("Failed REST API authentication");
            await WriteUnauthorized(context);
            return;
        }

        await next(context);
    }

    private static async Task WriteUnauthorized(HttpContext context)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = "Unauthorized",
        }));
    }
}
