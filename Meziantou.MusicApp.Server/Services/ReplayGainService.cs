using System.Diagnostics;
using System.Text.Json;
using Meziantou.Framework;
using Meziantou.MusicApp.Server.Models;

namespace Meziantou.MusicApp.Server.Services;

public sealed class ReplayGainService : IDisposable
{
    private readonly ILogger<ReplayGainService> _logger;
    private readonly string _ffmpegPath;
    private readonly SemaphoreSlim _semaphore;

    public ReplayGainService(ILogger<ReplayGainService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _ffmpegPath = configuration["FFmpeg:Path"] ?? "ffmpeg";
        var maxConcurrent = configuration.GetValue<int?>("FFmpeg:MaxConcurrentReplayGainAnalysis") ?? 2;
        _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    /// <summary>
    /// Analyzes a file to compute ReplayGain values using FFmpeg's loudnorm filter.
    /// Returns the track gain and peak values.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3006:Review code for process command injection vulnerabilities")]
    public async Task<ReplayGainResult?> AnalyzeTrackAsync(FullPath filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found for ReplayGain analysis: {Path}", filePath);
            return null;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Analyzing ReplayGain for: {Path}", filePath);

            // Use FFmpeg's loudnorm filter in measurement mode
            // This uses EBU R128 standard which is what ReplayGain 2.0 is based on
            var arguments = $"-hide_banner -i \"{filePath}\" -af loudnorm=I=-18:TP=-1:LRA=11:print_format=json -f null -";

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                _logger.LogError("Failed to start FFmpeg for ReplayGain analysis");
                return null;
            }

            // FFmpeg outputs loudnorm info to stderr
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg ReplayGain analysis failed for {Path} with exit code {ExitCode}", filePath, process.ExitCode);
                return null;
            }

            return ParseLoudnormOutput(stderr, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing ReplayGain for: {Path}", filePath);
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private ReplayGainResult? ParseLoudnormOutput(string output, FullPath filePath)
    {
        try
        {
            var json = ExtractLoudnormJson(output);
            if (json is null)
            {
                _logger.LogWarning("Could not find loudnorm JSON output for {Path}", filePath);
                return null;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var inputI = GetJsonDouble(root, "input_i");
            var inputTp = GetJsonDouble(root, "input_tp");

            if (!inputI.HasValue)
            {
                _logger.LogWarning("Could not parse integrated loudness from loudnorm output for {Path}", filePath);
                return null;
            }

            const double ReferenceLufs = -18.0;
            var trackGain = ReferenceLufs - inputI.Value;

            double? trackPeak = null;
            if (inputTp.HasValue)
            {
                trackPeak = Math.Pow(10, inputTp.Value / 20.0);
            }

            _logger.LogDebug("ReplayGain for {Path}: Gain={Gain:F2}dB, Peak={Peak:F6}",
                filePath, trackGain, trackPeak);

            return new ReplayGainResult
            {
                TrackGain = Math.Round(trackGain, 2, MidpointRounding.AwayFromZero),
                TrackPeak = trackPeak.HasValue ? Math.Round(trackPeak.Value, 6, MidpointRounding.AwayFromZero) : null,
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid loudnorm JSON output for {Path}", filePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing loudnorm output for {Path}", filePath);
            return null;
        }
    }

    private static string? ExtractLoudnormJson(string output)
    {
        // FFmpeg's loudnorm filter outputs JSON to stderr at the end
        // Find the last occurrence of the opening brace
        var span = output.AsSpan();
        var lastBraceIndex = span.LastIndexOf('{');

        if (lastBraceIndex < 0)
            return null;

        // Verify this looks like loudnorm output by checking for a key field
        var searchSpan = span[lastBraceIndex..];
        if (!searchSpan.Contains("input_i", StringComparison.Ordinal))
            return null;

        // Find the matching closing brace
        var depth = 0;
        for (var i = lastBraceIndex; i < span.Length; i++)
        {
            var c = span[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return output[lastBraceIndex..(i + 1)];
                }
            }
        }

        return null;
    }

    private static double? GetJsonDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d))
            return d;

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
        {
            return s;
        }

        return null;
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
