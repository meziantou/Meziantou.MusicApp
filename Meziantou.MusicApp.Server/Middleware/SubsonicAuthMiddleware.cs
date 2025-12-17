using System.Security.Cryptography;
using System.Xml.Linq;
using Meziantou.MusicApp.Server.Models;
using Microsoft.Extensions.Options;

namespace Meziantou.MusicApp.Server.Middleware;

[ExcludeFromDescription]
public class SubsonicAuthMiddleware(RequestDelegate next, IOptions<MusicServerSettings> commonSettings, ILogger<SubsonicAuthMiddleware> logger)
{
    private const string SubsonicServerVersion = "1.16.1";
    private readonly MusicServerSettings _commonSettings = commonSettings.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for non-REST API paths
        if (!context.Request.Path.StartsWithSegments("/rest", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        // Check for required parameters
        var query = context.Request.Query;
        var username = query["u"].FirstOrDefault();
        var token = query["t"].FirstOrDefault();
        var salt = query["s"].FirstOrDefault();
        var password = query["p"].FirstOrDefault();
        var version = query["v"].FirstOrDefault();
        var client = query["c"].FirstOrDefault();

        logger.LogInformation("Subsonic request: {Method} {URL}", context.Request.Method, context.Request.Path + context.Request.QueryString);
        logger.LogInformation("Subsonic login parameters: u={Username}, v={Version}, c={Client}", username, version, client);

        // Validate required parameters
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(version) || string.IsNullOrEmpty(client))
        {
            await WriteError(context, 10, "Required parameter is missing");
            return;
        }

        // Simple authentication: either token+salt or password
        var authenticated = false;

        if (string.IsNullOrEmpty(_commonSettings.AuthToken))
        {
            // No authentication required
            authenticated = true;
        }
        else if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(salt))
        {
            // Token-based auth (recommended)
            var expectedToken = ComputeMD5(_commonSettings.AuthToken + salt);
            authenticated = token.Equals(expectedToken, StringComparison.OrdinalIgnoreCase);
        }
        else if (!string.IsNullOrEmpty(password))
        {
            // Password-based auth (legacy)
            if (password.StartsWith("enc:", StringComparison.Ordinal))
            {
                var hexPassword = password.Substring(4);
                var decodedPassword = Encoding.UTF8.GetString(Convert.FromHexString(hexPassword));
                authenticated = decodedPassword == _commonSettings.AuthToken;
            }
            else
            {
                authenticated = password == _commonSettings.AuthToken;
            }
        }

        if (!authenticated)
        {
            logger.LogWarning("Failed Subsonic authentication for user: {Username}", username);
            await WriteError(context, 40, "Wrong username or password");
            return;
        }

        await next(context);
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms")]
    private static string ComputeMD5(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    private static async Task WriteError(HttpContext context, int code, string message)
    {
        context.Response.ContentType = "application/xml";
        context.Response.StatusCode = 200; // Subsonic always returns 200

        XNamespace ns = "http://subsonic.org/restapi";
        var xml = new XDocument(
            new XElement(ns + "subsonic-response",
                new XAttribute("status", "failed"),
                new XAttribute("version", SubsonicServerVersion),
                new XElement(ns + "error",
                    new XAttribute("code", code),
                    new XAttribute("message", message)
                )
            )
        );

        await context.Response.WriteAsync(xml.ToString());
    }
}
