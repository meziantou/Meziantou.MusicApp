namespace Meziantou.MusicApp.Server.Services;

/// <summary>Helper class providing common functionality for serving cached images</summary>
public static class ImageCacheHelper
{
    /// <summary>Sets HTTP cache headers for image responses based on file modification time</summary>
    /// <param name="response">The HTTP response object</param>
    /// <param name="lastModified">The last modified date of the image file</param>
    public static void SetImageCacheHeaders(HttpResponse response, DateTimeOffset lastModified)
    {
        response.Headers.LastModified = lastModified.ToString("R");
        response.Headers.CacheControl = "public, max-age=2592000"; // 30 days
    }

    /// <summary>Checks if the client's cached version is still valid based on If-Modified-Since header</summary>
    /// <param name="request">The HTTP request object</param>
    /// <param name="lastModified">The last modified date of the resource</param>
    /// <returns>True if the resource has not been modified since the client's cached version</returns>
    public static bool IsNotModified(HttpRequest request, DateTimeOffset lastModified)
    {
        var ifModifiedSince = request.Headers.IfModifiedSince;
        if (string.IsNullOrEmpty(ifModifiedSince))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(ifModifiedSince, CultureInfo.InvariantCulture, out var clientDate))
        {
            return false;
        }

        // Truncate to seconds for comparison (HTTP dates don't include milliseconds)
        var serverTime = new DateTimeOffset(
            lastModified.Year, lastModified.Month, lastModified.Day,
            lastModified.Hour, lastModified.Minute, lastModified.Second,
            lastModified.Offset);
        
        var clientTime = new DateTimeOffset(
            clientDate.Year, clientDate.Month, clientDate.Day,
            clientDate.Hour, clientDate.Minute, clientDate.Second,
            clientDate.Offset);
        
        return serverTime <= clientTime;
    }
}
