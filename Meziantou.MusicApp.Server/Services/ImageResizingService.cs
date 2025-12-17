using System.Diagnostics;

namespace Meziantou.MusicApp.Server.Services;

public sealed class ImageResizingService : IDisposable
{
    private readonly ILogger<ImageResizingService> _logger;
    private readonly string _ffmpegPath;
    private readonly SemaphoreSlim _resizingSemaphore;

    public ImageResizingService(ILogger<ImageResizingService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _ffmpegPath = configuration["FFmpeg:Path"] ?? "ffmpeg";
        var maxConcurrent = configuration.GetValue<int?>("FFmpeg:MaxConcurrentResizes") ?? 5;
        _resizingSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    /// <summary>Resize image using ffmpeg</summary>
    [SuppressMessage("Security", "CA3006:Review code for process command injection vulnerabilities")]
    public async Task<byte[]> ResizeImageAsync(byte[] imageData, int? size = null, CancellationToken cancellationToken = default)
    {
        // If no size specified, return original
        if (!size.HasValue || size.Value <= 0)
        {
            return imageData;
        }

        await _resizingSemaphore.WaitAsync(cancellationToken);

        Process? process = null;
        try
        {
            var arguments = BuildFFmpegArguments(size.Value);

            _logger.LogInformation("Resizing image to size: {Size}", size.Value);

            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            // Start the process
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start FFmpeg process");
            }

            // Write image data to stdin asynchronously
            var writeTask = Task.Run(async () =>
            {
                await process.StandardInput.BaseStream.WriteAsync(imageData, cancellationToken);
                process.StandardInput.Close();
            }, cancellationToken);

            // Read output asynchronously
            var outputTask = Task.Run(async () =>
            {
                using var memoryStream = new MemoryStream();
                await process.StandardOutput.BaseStream.CopyToAsync(memoryStream, cancellationToken);
                return memoryStream.ToArray();
            }, cancellationToken);

            // Read errors asynchronously
            var errorTask = Task.Run(async () =>
            {
                var errors = await process.StandardError.ReadToEndAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(errors))
                {
                    _logger.LogDebug("FFmpeg stderr (resize): {Errors}", errors);
                }
                return errors;
            }, cancellationToken);

            // Wait for all tasks to complete
            await writeTask;
            var resizedImage = await outputTask;
            _ = await errorTask;

            // Wait for process to exit
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg exited with code {ExitCode}", process.ExitCode);
                // Return original image if resize failed
                return imageData;
            }

            _logger.LogInformation("Image resized successfully to {Size}. Original size: {OriginalSize} bytes, Resized size: {ResizedSize} bytes",
                size.Value, imageData.Length, resizedImage.Length);

            return resizedImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resizing image");
            // Return original image if resize failed
            return imageData;
        }
        finally
        {
            process?.Dispose();
            _resizingSemaphore.Release();
        }
    }

    private static string BuildFFmpegArguments(int size)
    {
        var args = new List<string>();

        // Global options
        args.Add("-hide_banner");
        args.Add("-loglevel error");

        // Input from stdin (image)
        args.Add("-i pipe:0");

        // Resize filter - maintain aspect ratio, fit within size x size box
        args.Add($"-vf \"scale='min({size.ToString(CultureInfo.InvariantCulture)},iw)':'min({size.ToString(CultureInfo.InvariantCulture)},ih)':force_original_aspect_ratio=decrease\"");

        // Output format - JPEG with high quality
        args.Add("-q:v 2");

        // Output to stdout (JPEG format)
        args.Add("-f image2");
        args.Add("pipe:1");

        return string.Join(' ', args);
    }

    public void Dispose()
    {
        _resizingSemaphore.Dispose();
    }
}
