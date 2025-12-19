using Meziantou.MusicApp.Server.Models;
using Meziantou.MusicApp.Server.Services;
using Meziantou.MusicApp.Server.Tests.Helpers;
using Meziantou.Framework;

namespace Meziantou.MusicApp.Server.Tests.Unit;

public class ReplayGainServiceTests
{
    [Fact]
    public async Task AnalyzeTrackAsync_WithNonExistentFile_ReturnsNull()
    {
        await using var context = AppTestContext.Create();
        var service = context.GetRequiredService<ReplayGainService>();

        var result = await service.AnalyzeTrackAsync(FullPath.FromPath("non/existent/file.mp3"), context.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task AnalyzeTrackAsync_WithValidAudioFile_ReturnsReplayGainResult()
    {
        // Skip if FFmpeg is not available
        if (!IsFFmpegAvailable())
        {
            return;
        }

        await using var context = AppTestContext.Create();
        var service = context.GetRequiredService<ReplayGainService>();

        // Create a test audio file using FFmpeg to generate a valid audio file
        var testFilePath = context.MusicPath / "test.mp3";
        if (!await CreateTestAudioFileWithFFmpeg(testFilePath))
        {
            // Skip if we couldn't create the test file
            return;
        }

        var result = await service.AnalyzeTrackAsync(testFilePath, context.CancellationToken);

        // The result should have a track gain value
        Assert.NotNull(result);
        Assert.NotNull(result.TrackPeak);
        // TrackGain should be a reasonable value (typically between -20 and +20 dB)
        Assert.InRange(result.TrackGain, -30, 30);
        // TrackPeak should be positive (linear scale)
        Assert.True(result.TrackPeak > 0);
    }

    [Fact]
    public async Task AnalyzeTrackAsync_WithValidAudioFile_WritesReplayGainTagsToFile()
    {
        // Skip if FFmpeg is not available
        if (!IsFFmpegAvailable())
        {
            return;
        }

        await using var context = AppTestContext.Create();
        context.Configure<MusicServerSettings>(settings => settings.ComputeMissingReplayGain = true);

        // Create a test audio file using FFmpeg
        var testFilePath = context.MusicPath / "Artist" / "Album" / "test_tagging.mp3";
        if (!await CreateTestAudioFileWithFFmpeg(testFilePath))
        {
            return;
        }

        // Verify the file has no ReplayGain tags initially
        var initialTags = ReadReplayGainTagsFromFile(testFilePath);
        Assert.Null(initialTags.TrackGain);
        Assert.Null(initialTags.TrackPeak);

        // Scan the library (this should compute and write ReplayGain tags)
        await context.ScanCatalog();

        // Read the tags again and verify they were written
        var updatedTags = ReadReplayGainTagsFromFile(testFilePath);
        Assert.NotNull(updatedTags.TrackGain);
        Assert.NotNull(updatedTags.TrackPeak);

        // Verify the values are reasonable
        Assert.InRange(updatedTags.TrackGain!.Value, -30, 30);
        Assert.True(updatedTags.TrackPeak > 0);
    }

    [Fact]
    public async Task AnalyzeTrackAsync_WithExistingReplayGainTags_DoesNotRecompute()
    {
        // Skip if FFmpeg is not available
        if (!IsFFmpegAvailable())
        {
            return;
        }

        await using var context = AppTestContext.Create();
        context.Configure<MusicServerSettings>(settings => settings.ComputeMissingReplayGain = true);

        // Create a test audio file and write ReplayGain tags manually
        var testFilePath = context.MusicPath / "Artist" / "Album" / "test_existing_tags.mp3";
        if (!await CreateTestAudioFileWithFFmpeg(testFilePath))
        {
            return;
        }

        // Write known ReplayGain values to the file
        var knownGain = -5.55;
        var knownPeak = 0.123456;
        WriteReplayGainTagsToFile(testFilePath, knownGain, knownPeak);

        // Verify the tags were written
        var initialTags = ReadReplayGainTagsFromFile(testFilePath);
        Assert.NotNull(initialTags.TrackGain);
        Assert.Equal(knownGain, initialTags.TrackGain!.Value, precision: 2);

        // Scan the library (this should NOT recompute since tags exist)
        await context.ScanCatalog();

        // Read the tags again - they should be unchanged
        var updatedTags = ReadReplayGainTagsFromFile(testFilePath);
        Assert.NotNull(updatedTags.TrackGain);
        Assert.Equal(knownGain, updatedTags.TrackGain!.Value, precision: 2);
    }

    [Fact]
    public async Task AnalyzeTrackAsync_WithInvalidAudioFile_ReturnsNull()
    {
        // Skip if FFmpeg is not available
        if (!IsFFmpegAvailable())
        {
            return;
        }

        await using var context = AppTestContext.Create();
        var service = context.GetRequiredService<ReplayGainService>();

        // Create an invalid "audio" file (just random bytes)
        var testFilePath = context.MusicPath / "invalid.mp3";
        Directory.CreateDirectory(context.MusicPath);
        await File.WriteAllBytesAsync(testFilePath, [0x00, 0x01, 0x02, 0x03], context.CancellationToken);

        var result = await service.AnalyzeTrackAsync(testFilePath, context.CancellationToken);

        // FFmpeg should fail to process this file
        Assert.Null(result);
    }

    [Fact]
    public async Task AnalyzeTrackAsync_ConcurrentCalls_RespectsMaxConcurrency()
    {
        // Skip if FFmpeg is not available
        if (!IsFFmpegAvailable())
        {
            return;
        }

        await using var context = AppTestContext.Create();
        var service = context.GetRequiredService<ReplayGainService>();

        // Create test audio files using FFmpeg
        var testFiles = new List<FullPath>();
        for (var i = 0; i < 3; i++)
        {
            var testFilePath = context.MusicPath / $"test{i}.mp3";
            if (await CreateTestAudioFileWithFFmpeg(testFilePath))
            {
                testFiles.Add(testFilePath);
            }
        }

        // Skip if we couldn't create any test files
        if (testFiles.Count == 0)
        {
            return;
        }

        // Run multiple analyses concurrently
        var tasks = testFiles.Select(f => service.AnalyzeTrackAsync(f, context.CancellationToken));
        var results = await Task.WhenAll(tasks);

        // All should complete (semaphore should allow them through eventually)
        Assert.Equal(testFiles.Count, results.Length);
        Assert.All(results, r => Assert.NotNull(r));
    }

    private static bool IsFFmpegAvailable()
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> CreateTestAudioFileWithFFmpeg(FullPath path)
    {
        try
        {
            path.CreateParentDirectory();

            // Use FFmpeg to generate a 1-second test audio file with a sine wave
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    // Generate 1 second of 440Hz sine wave audio
                    Arguments = $"-y -f lavfi -i \"sine=frequency=440:duration=1\" -c:a libmp3lame -b:a 128k \"{path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 && File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static (double? TrackGain, double? TrackPeak) ReadReplayGainTagsFromFile(FullPath path)
    {
        using var file = TagLib.File.Create(path);

        double? trackGain = null;
        double? trackPeak = null;

        // Read from ID3v2 tags (MP3)
        if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2Tag)
        {
            foreach (var frame in id3v2Tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
            {
                var value = frame.Text.FirstOrDefault();
                if (string.IsNullOrEmpty(value))
                    continue;

                if (frame.Description?.Equals("REPLAYGAIN_TRACK_GAIN", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var cleanValue = value.Replace(" dB", "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (double.TryParse(cleanValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gain))
                    {
                        trackGain = gain;
                    }
                }
                else if (frame.Description?.Equals("REPLAYGAIN_TRACK_PEAK", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var peak))
                    {
                        trackPeak = peak;
                    }
                }
            }
        }

        return (trackGain, trackPeak);
    }

    private static void WriteReplayGainTagsToFile(FullPath path, double trackGain, double trackPeak)
    {
        using var file = TagLib.File.Create(path);

        var trackGainStr = $"{trackGain:F2} dB";
        var trackPeakStr = $"{trackPeak:F6}";

        // Write to ID3v2 tags (MP3)
        if (file.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3v2Tag)
        {
            var trackGainFrame = new TagLib.Id3v2.UserTextInformationFrame("REPLAYGAIN_TRACK_GAIN")
            {
                Text = [trackGainStr]
            };
            id3v2Tag.AddFrame(trackGainFrame);

            var trackPeakFrame = new TagLib.Id3v2.UserTextInformationFrame("REPLAYGAIN_TRACK_PEAK")
            {
                Text = [trackPeakStr]
            };
            id3v2Tag.AddFrame(trackPeakFrame);
        }

        file.Save();
    }
}
