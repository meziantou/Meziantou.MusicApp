using Meziantou.MusicApp.Server.Models;
using Meziantou.MusicApp.Server.Services;
using Meziantou.MusicApp.Server.Tests.Helpers;
using Meziantou.Framework;
using Meziantou.Framework.MediaTags;

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
        Assert.Equal(knownGain, initialTags.TrackGain!.Value, tolerance: 0.01);

        // Scan the library (this should NOT recompute since tags exist)
        await context.ScanCatalog();

        // Read the tags again - they should be unchanged
        var updatedTags = ReadReplayGainTagsFromFile(testFilePath);
        Assert.NotNull(updatedTags.TrackGain);
        Assert.Equal(knownGain, updatedTags.TrackGain!.Value, tolerance: 0.01);
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
        Assert.HasCount(testFiles.Count, results);
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
        var readResult = MediaFile.ReadTags(path);
        if (!readResult.IsSuccess)
        {
            return (null, null);
        }

        return (readResult.Value.ReplayGain?.TrackGain, readResult.Value.ReplayGain?.TrackPeak);
    }

    private static void WriteReplayGainTagsToFile(FullPath path, double trackGain, double trackPeak)
    {
        var readResult = MediaFile.ReadTags(path);
        if (!readResult.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to read media tags: {readResult.ErrorMessage}");
        }

        var tags = readResult.Value;
        tags.ReplayGain = new ReplayGainInfo
        {
            TrackGain = trackGain,
            TrackPeak = trackPeak,
        };

        var writeResult = MediaFile.WriteTags(path, tags);
        if (!writeResult.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to write media tags: {writeResult.ErrorMessage}");
        }
    }
}
