using System.Xml.Linq;

namespace Meziantou.MusicApp.Server.Middleware;

[ExcludeFromDescription]
public class SubsonicAuthMiddleware(RequestDelegate next, ILogger<SubsonicAuthMiddleware> logger)
{
    private const string SubsonicServerVersion = "1.16.1";

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

        await next(context);
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
