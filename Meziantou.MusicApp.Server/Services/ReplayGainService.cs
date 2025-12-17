using System.Diagnostics;
using System.Text.RegularExpressions;
using Meziantou.MusicApp.Server.Models;

namespace Meziantou.MusicApp.Server.Services;

public sealed partial class ReplayGainService : IDisposable
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
    public async Task<ReplayGainResult?> AnalyzeTrackAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found for ReplayGain analysis: {Path}", filePath);
            return null;
        }

        await _semaphore.WaitAsync(cancellationToken);
        Process? process = null;
        try
        {
            _logger.LogDebug("Analyzing ReplayGain for: {Path}", filePath);

            // Use FFmpeg's loudnorm filter in measurement mode
            // This uses EBU R128 standard which is what ReplayGain 2.0 is based on
            var arguments = $"-hide_banner -i \"{filePath}\" -af loudnorm=I=-18:TP=-1:LRA=11:print_format=json -f null -";

            process = new Process
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
            process?.Dispose();
            _semaphore.Release();
        }
    }

    private ReplayGainResult? ParseLoudnormOutput(string output, string filePath)
    {
        try
        {
            // Extract JSON block from FFmpeg output
            // The loudnorm filter outputs JSON at the end of stderr
            var jsonMatch = LoudnormJsonRegex().Match(output);
            if (!jsonMatch.Success)
            {
                _logger.LogWarning("Could not find loudnorm JSON output for {Path}", filePath);
                return null;
            }

            var json = jsonMatch.Value;

            // Parse individual values from the JSON
            var inputI = ExtractDoubleValue(json, "input_i");
            var inputTp = ExtractDoubleValue(json, "input_tp");

            if (inputI is null)
            {
                _logger.LogWarning("Could not parse integrated loudness from loudnorm output for {Path}", filePath);
                return null;
            }

            // Calculate ReplayGain value
            // ReplayGain reference level is -18 LUFS (same as EBU R128)
            // Gain = reference level - measured loudness
            const double ReferenceLufs = -18.0;
            var trackGain = ReferenceLufs - inputI.Value;

            // Convert true peak from dBTP to linear scale for peak value
            // Peak in ReplayGain is typically stored as linear (0.0 to 1.0+)
            double? trackPeak = null;
            if (inputTp.HasValue)
            {
                // Convert dB to linear: linear = 10^(dB/20)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing loudnorm output for {Path}", filePath);
            return null;
        }
    }

    private static double? ExtractDoubleValue(string json, string key)
    {
        var pattern = $"\"{key}\"\\s*:\\s*\"([^\"]+)\"";
        var match = Regex.Match(json, pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        return null;
    }

    [GeneratedRegex(@"\{[^{}]*""input_i""[^{}]*\}", RegexOptions.Singleline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex LoudnormJsonRegex();

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
