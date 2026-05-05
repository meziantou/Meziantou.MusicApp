namespace Meziantou.MusicApp.Server.Services;

/// <summary>Helper class providing common functionality for serving cached images</summary>
public static class ImageCacheHelper
{
    public static string GetImageContentType(ReadOnlySpan<byte> imageData)
    {
        if (IsPng(imageData))
            return "image/png";

        if (IsJpeg(imageData))
            return "image/jpeg";

        if (IsWebP(imageData))
            return "image/webp";

        if (IsAvif(imageData))
            return "image/avif";

        return "image/jpeg";
    }

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

    private static bool IsPng(ReadOnlySpan<byte> data)
    {
        return data.Length >= 8 &&
               data[0] == 0x89 &&
               data[1] == 0x50 &&
               data[2] == 0x4E &&
               data[3] == 0x47 &&
               data[4] == 0x0D &&
               data[5] == 0x0A &&
               data[6] == 0x1A &&
               data[7] == 0x0A;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> data)
    {
        return data.Length >= 3 &&
               data[0] == 0xFF &&
               data[1] == 0xD8 &&
               data[2] == 0xFF;
    }

    private static bool IsWebP(ReadOnlySpan<byte> data)
    {
        return data.Length >= 12 &&
               data[0] == (byte)'R' &&
               data[1] == (byte)'I' &&
               data[2] == (byte)'F' &&
               data[3] == (byte)'F' &&
               data[8] == (byte)'W' &&
               data[9] == (byte)'E' &&
               data[10] == (byte)'B' &&
               data[11] == (byte)'P';
    }

    private static bool IsAvif(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            return false;

        if (data[4] != (byte)'f' || data[5] != (byte)'t' || data[6] != (byte)'y' || data[7] != (byte)'p')
            return false;

        for (var i = 8; i <= data.Length - 4; i += 4)
        {
            if ((data[i] == (byte)'a' && data[i + 1] == (byte)'v' && data[i + 2] == (byte)'i' && data[i + 3] == (byte)'f') ||
                (data[i] == (byte)'a' && data[i + 1] == (byte)'v' && data[i + 2] == (byte)'i' && data[i + 3] == (byte)'s'))
            {
                return true;
            }
        }

        return false;
    }
}
