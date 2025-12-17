using System.Diagnostics;
using System.Security.Cryptography;
using Meziantou.MusicApp.Server.Models;
using Microsoft.Extensions.Options;

namespace Meziantou.MusicApp.Server.Services;

public sealed class TranscodingService : IDisposable
{
    private readonly ILogger<TranscodingService> _logger;
    private readonly string _ffmpegPath;
    private readonly SemaphoreSlim _transcodingSemaphore;
    private readonly MusicServerSettings _settings;

    public TranscodingService(ILogger<TranscodingService> logger, IConfiguration configuration, IOptions<MusicServerSettings> settings)
    {
        _logger = logger;
        _ffmpegPath = configuration["FFmpeg:Path"] ?? "ffmpeg";
        var maxConcurrent = configuration.GetValue<int?>("FFmpeg:MaxConcurrentTranscodes") ?? 5;
        _transcodingSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        _settings = settings.Value;
    }

    /// <summary>Transcode audio file to specified format and bitrate</summary>
    [SuppressMessage("Security", "CA3006:Review code for process command injection vulnerabilities")]
    public async Task<Stream> TranscodeToStreamAsync(
        string inputPath,
        string? outputFormat = null,
        int? maxBitRate = null,
        int? timeOffset = null,
        CancellationToken cancellationToken = default)
    {
        string? cacheFilePath = null;
        if (_settings.EnableTranscodingCache && (timeOffset == null || timeOffset == 0) && !string.IsNullOrEmpty(_settings.CachePath))
        {
            cacheFilePath = GetCacheFilePath(inputPath, outputFormat, maxBitRate);
            if (File.Exists(cacheFilePath))
            {
                _logger.LogInformation("Serving from cache: {CachePath}", cacheFilePath);
                try
                {
                    // File.SetLastAccessTimeUtc(cacheFilePath, DateTime.UtcNow);
                    return new FileStream(cacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to serve from cache");
                }
            }
        }

        await _transcodingSemaphore.WaitAsync(cancellationToken);

        try
        {
            var arguments = BuildFFmpegArguments(inputPath, outputFormat, maxBitRate, timeOffset);

            _logger.LogInformation("Transcoding file: {InputPath} with format: {Format}, bitrate: {BitRate}, offset: {Offset}",
                inputPath, outputFormat ?? "original", maxBitRate, timeOffset);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            // Start the process
            if (!process.Start())
            {
                _transcodingSemaphore.Release();
                throw new InvalidOperationException("Failed to start FFmpeg process");
            }

            // Log errors in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var errors = await process.StandardError.ReadToEndAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(errors))
                    {
                        _logger.LogDebug("FFmpeg stderr: {Errors}", errors);
                    }
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
                {
                    _logger.LogWarning(ex, "Error reading FFmpeg stderr");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading FFmpeg stderr");
                }
            }, cancellationToken);

            // Create a stream that will clean up the process when disposed
            var outputStream = new TranscodedStream(process, _transcodingSemaphore, _logger, cacheFilePath);

            return outputStream;
        }
        catch
        {
            _transcodingSemaphore.Release();
            throw;
        }
    }

    private string GetCacheFilePath(string inputPath, string? outputFormat, int? maxBitRate)
    {
        var key = $"{inputPath}|{outputFormat}|{maxBitRate?.ToString(CultureInfo.InvariantCulture)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var fileName = $"{hash}.{outputFormat ?? "mp3"}";
        return Path.Combine(_settings.CachePath, fileName);
    }

    private static string BuildFFmpegArguments(
        string inputPath,
        string? outputFormat,
        int? maxBitRate,
        int? timeOffset)
    {
        var args = new List<string>();

        // Global options
        args.Add("-hide_banner");
        args.Add("-loglevel error");

        // Input file (with seeking if specified)
        if (timeOffset.HasValue && timeOffset.Value > 0)
        {
            args.Add($"-ss {timeOffset.Value.ToString(CultureInfo.InvariantCulture)}");
        }
        args.Add($"-i \"{inputPath}\"");

        // Determine output format and codec
        var format = (outputFormat?.ToLowerInvariant()) switch
        {
            "mp3" => "mp3",
            "opus" => "opus",
            "ogg" => "ogg",
            "m4a" => "ipod",
            "flac" => "flac",
            _ => "mp3", // Default to MP3
        };

        var codec = format switch
        {
            "mp3" => "libmp3lame",
            "opus" => "libopus",
            "ogg" => "libvorbis",
            "ipod" => "aac",
            "flac" => "flac",
            _ => "libmp3lame",
        };

        args.Add($"-c:a {codec}");

        // Bitrate
        if (maxBitRate.HasValue && maxBitRate.Value > 0)
        {
            args.Add($"-b:a {maxBitRate.Value.ToString(CultureInfo.InvariantCulture)}k");
        }
        else if (format == "mp3")
        {
            // Default quality for MP3
            args.Add("-q:a 2"); // VBR ~190 kbps
        }
        else if (format == "opus")
        {
            args.Add("-b:a 128k"); // Default opus bitrate
        }

        // Additional options
        args.Add("-vn"); // No video
        args.Add("-sn"); // No subtitles
        args.Add("-map_metadata 0"); // Preserve metadata
        args.Add("-map 0:a:0"); // Only first audio stream

        // Output format
        args.Add($"-f {format}");

        // Output to stdout (pipe)
        args.Add("pipe:1");

        return string.Join(' ', args);
    }

    /// <summary>Get the content type for the transcoded format</summary>
    public static string GetContentType(string? format)
    {
        return (format?.ToLowerInvariant()) switch
        {
            "mp3" => "audio/mpeg",
            "opus" => "audio/opus",
            "ogg" => "audio/ogg",
            "m4a" => "audio/mp4",
            "flac" => "audio/flac",
            _ => "audio/mpeg",
        };
    }

    /// <summary>Estimate the transcoded file size</summary>
    public static long? EstimateSize(int durationSeconds, int? bitRateKbps)
    {
        if (!bitRateKbps.HasValue || bitRateKbps.Value <= 0)
            return null;

        // Size = (bitrate in kbps * duration in seconds * 1024) / 8
        return (bitRateKbps.Value * durationSeconds * 1024L) / 8;
    }

    /// <summary>Generate HLS playlist for the specified song</summary>
    public string GenerateHlsPlaylist(
        string songId,
        int duration,
        int? bitRate = null,
        string? audioCodec = null,
        int segmentDuration = 10)
    {
        var bitRateValue = bitRate ?? 128;
        var codec = audioCodec ?? "mp3";
        
        // Calculate number of segments
        var segmentCount = (int)Math.Ceiling((double)duration / segmentDuration);
        
        var playlist = new System.Text.StringBuilder();
        playlist.AppendLine("#EXTM3U");
        playlist.AppendLine("#EXT-X-VERSION:3");
        playlist.AppendLine(CultureInfo.InvariantCulture, $"#EXT-X-TARGETDURATION:{segmentDuration}");
        playlist.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
        
        // Add segments
        for (var i = 0; i < segmentCount; i++)
        {
            var segmentLength = Math.Min(segmentDuration, duration - (i * segmentDuration));
            playlist.AppendLine(CultureInfo.InvariantCulture, $"#EXTINF:{segmentLength}.0,");
            playlist.AppendLine(CultureInfo.InvariantCulture, $"./hls/{songId}/{i}.{codec}?bitRate={bitRateValue}");
        }
        
        playlist.AppendLine("#EXT-X-ENDLIST");
        return playlist.ToString();
    }

    /// <summary>Transcode a specific HLS segment</summary>
    [SuppressMessage("Security", "CA3006:Review code for process command injection vulnerabilities")]
    public async Task<Stream> TranscodeHlsSegmentAsync(
        string inputPath,
        int segmentIndex,
        int segmentDuration,
        string? outputFormat = null,
        int? maxBitRate = null,
        CancellationToken cancellationToken = default)
    {
        var timeOffset = segmentIndex * segmentDuration;
        
        await _transcodingSemaphore.WaitAsync(cancellationToken);

        try
        {
            var arguments = BuildHlsSegmentFFmpegArguments(
                inputPath, 
                timeOffset, 
                segmentDuration, 
                outputFormat, 
                maxBitRate);

            _logger.LogInformation(
                "Transcoding HLS segment {Index} for file: {InputPath} with format: {Format}, bitrate: {BitRate}",
                segmentIndex, inputPath, outputFormat ?? "mp3", maxBitRate);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                _transcodingSemaphore.Release();
                throw new InvalidOperationException("Failed to start FFmpeg process");
            }

            // Log errors in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var errors = await process.StandardError.ReadToEndAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(errors))
                    {
                        _logger.LogDebug("FFmpeg stderr (HLS segment): {Errors}", errors);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading FFmpeg stderr for HLS segment");
                }
            }, cancellationToken);

            var outputStream = new TranscodedStream(process, _transcodingSemaphore, _logger, cacheFilePath: null);
            return outputStream;
        }
        catch
        {
            _transcodingSemaphore.Release();
            throw;
        }
    }

    private static string BuildHlsSegmentFFmpegArguments(
        string inputPath,
        int timeOffset,
        int segmentDuration,
        string? outputFormat,
        int? maxBitRate)
    {
        var args = new List<string>();

        // Global options
        args.Add("-hide_banner");
        args.Add("-loglevel error");

        // Seek to segment start
        args.Add($"-ss {timeOffset.ToString(CultureInfo.InvariantCulture)}");
        
        // Input file
        args.Add($"-i \"{inputPath}\"");

        // Limit duration to segment length
        args.Add($"-t {segmentDuration.ToString(CultureInfo.InvariantCulture)}");

        // Determine output format and codec
        var format = (outputFormat?.ToLowerInvariant()) switch
        {
            "mp3" => "mp3",
            "opus" => "opus",
            "ogg" => "ogg",
            "m4a" => "ipod",
            "aac" => "adts",
            _ => "mp3",
        };

        var codec = format switch
        {
            "mp3" => "libmp3lame",
            "opus" => "libopus",
            "ogg" => "libvorbis",
            "ipod" => "aac",
            "adts" => "aac",
            _ => "libmp3lame",
        };

        args.Add($"-c:a {codec}");

        // Bitrate
        if (maxBitRate.HasValue && maxBitRate.Value > 0)
        {
            args.Add($"-b:a {maxBitRate.Value.ToString(CultureInfo.InvariantCulture)}k");
        }
        else if (format == "mp3")
        {
            args.Add("-q:a 2"); // VBR ~190 kbps
        }
        else
        {
            args.Add("-b:a 128k"); // Default bitrate for opus and others
        }

        // Additional options
        args.Add("-vn"); // No video
        args.Add("-sn"); // No subtitles
        args.Add("-map_metadata 0");
        args.Add("-map 0:a:0");

        // Output format
        args.Add($"-f {format}");

        // Output to stdout
        args.Add("pipe:1");

        return string.Join(' ', args);
    }

    public void Dispose()
    {
        _transcodingSemaphore.Dispose();
    }

    private sealed class TranscodedStream : Stream
    {
        private readonly Process _process;
        private readonly Stream _baseStream;
        private readonly SemaphoreSlim _semaphore;
        private readonly ILogger _logger;
        private bool _disposed;

        private readonly string? _cacheFilePath;
        private readonly string? _tempCacheFilePath;
        private FileStream? _cacheStream;
        private bool _isComplete;

        public TranscodedStream(Process process, SemaphoreSlim semaphore, ILogger logger, string? cacheFilePath)
        {
            _process = process;
            _baseStream = process.StandardOutput.BaseStream;
            _semaphore = semaphore;
            _logger = logger;
            _cacheFilePath = cacheFilePath;

            if (!string.IsNullOrEmpty(_cacheFilePath))
            {
                try
                {
                    var cacheDir = Path.GetDirectoryName(_cacheFilePath);
                    if (cacheDir != null)
                        Directory.CreateDirectory(cacheDir);

                    _tempCacheFilePath = _cacheFilePath + ".tmp";
                    _cacheStream = new FileStream(_tempCacheFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize cache stream");
                    _cacheStream = null;
                }
            }
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => false; // Transcoded streams cannot seek
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _baseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _baseStream.Read(buffer, offset, count);
            if (read > 0 && _cacheStream != null)
            {
                try
                {
                    _cacheStream.Write(buffer, offset, read);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write to cache");
                    CloseCache(success: false);
                }
            }
            else if (read == 0)
            {
                _isComplete = true;
            }
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _baseStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            if (read > 0 && _cacheStream != null)
            {
                try
                {
                    await _cacheStream.WriteAsync(buffer.AsMemory(offset, read), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write to cache");
                    await CloseCacheAsync(success: false);
                }
            }
            else if (read == 0)
            {
                _isComplete = true;
            }
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _baseStream.ReadAsync(buffer, cancellationToken);
            if (read > 0 && _cacheStream != null)
            {
                try
                {
                    await _cacheStream.WriteAsync(buffer.Slice(0, read), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write to cache");
                    await CloseCacheAsync(success: false);
                }
            }
            else if (read == 0)
            {
                _isComplete = true;
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        private void CloseCache(bool success)
        {
            if (_cacheStream != null)
            {
                try
                {
                    _cacheStream.Dispose();
                }
                catch { }
                _cacheStream = null;

                if (success && _tempCacheFilePath != null && _cacheFilePath != null)
                {
                    try
                    {
                        File.Move(_tempCacheFilePath, _cacheFilePath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to move temp cache file to final destination");
                        try { File.Delete(_tempCacheFilePath); } catch { }
                    }
                }
                else if (_tempCacheFilePath != null)
                {
                    try { File.Delete(_tempCacheFilePath); } catch { }
                }
            }
        }

        private async Task CloseCacheAsync(bool success)
        {
            if (_cacheStream != null)
            {
                try
                {
                    await _cacheStream.DisposeAsync();
                }
                catch { }
                _cacheStream = null;

                if (success && _tempCacheFilePath != null && _cacheFilePath != null)
                {
                    try
                    {
                        File.Move(_tempCacheFilePath, _cacheFilePath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to move temp cache file to final destination");
                        try { File.Delete(_tempCacheFilePath); } catch { }
                    }
                }
                else if (_tempCacheFilePath != null)
                {
                    try { File.Delete(_tempCacheFilePath); } catch { }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                CloseCache(_isComplete);

                try
                {
                    _baseStream.Dispose();

                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                    }

                    _process.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing transcoded stream");
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            _disposed = true;
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            await CloseCacheAsync(_isComplete);

            try
            {
                await _baseStream.DisposeAsync();

                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }

                _process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing transcoded stream");
            }
            finally
            {
                _semaphore.Release();
            }

            _disposed = true;
            await base.DisposeAsync();
        }
    }
}
