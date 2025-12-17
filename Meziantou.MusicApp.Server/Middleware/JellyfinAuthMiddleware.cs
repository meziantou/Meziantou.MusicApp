using System.Text.Json;
using Meziantou.MusicApp.Server.Models;
using Microsoft.Extensions.Options;

namespace Meziantou.MusicApp.Server.Middleware;

[ExcludeFromDescription]
public class JellyfinAuthMiddleware(RequestDelegate next, IOptions<MusicServerSettings> commonSettings, ILogger<JellyfinAuthMiddleware> logger)
{
    private readonly MusicServerSettings _commonSettings = commonSettings.Value;

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

        // Check for authentication token
        var authHeader = context.Request.Headers["X-Emby-Authorization"].FirstOrDefault()
                      ?? context.Request.Headers["Authorization"].FirstOrDefault();

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

        // Parse Emby/Jellyfin auth header format:
        // MediaBrowser Client="...", Device="...", DeviceId="...", Version="...", Token="..."
        var authenticated = false;
        
        if (authHeader.StartsWith("MediaBrowser ", StringComparison.OrdinalIgnoreCase))
        {
            var token = ExtractTokenFromHeader(authHeader);
            if (!string.IsNullOrEmpty(token))
            {
                authenticated = token == _commonSettings.AuthToken;
            }
        }
        else if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.Substring(7);
            authenticated = token == _commonSettings.AuthToken;
        }

        if (!authenticated)
        {
            logger.LogWarning("Failed Jellyfin authentication");
            await WriteUnauthorized(context);
            return;
        }

        await next(context);
    }

    private static string? ExtractTokenFromHeader(string header)
    {
        // Parse format: MediaBrowser Token="abc123", Client="...", ...
        var tokenPrefix = "Token=\"";
        var tokenIndex = header.IndexOf(tokenPrefix, StringComparison.OrdinalIgnoreCase);
        if (tokenIndex < 0)
            return null;

        var startIndex = tokenIndex + tokenPrefix.Length;
        var endIndex = header.IndexOf('"', startIndex);
        if (endIndex < 0)
            return null;

        return header.Substring(startIndex, endIndex - startIndex);
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
