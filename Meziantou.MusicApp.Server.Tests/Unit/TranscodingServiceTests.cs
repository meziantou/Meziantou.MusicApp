using System.Reflection;
using Meziantou.Framework;
using Meziantou.MusicApp.Server.Models;
using Meziantou.MusicApp.Server.Services;
using Meziantou.MusicApp.Server.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meziantou.MusicApp.Server.Tests.Unit;

public class TranscodingServiceTests
{
    [Fact]
    public async Task TranscodeToStreamAsync_WithFreshCache_ReturnsCachedFile()
    {
        using var tempDir = TemporaryDirectory.Create();
        var cachePath = tempDir / "cache";
        var sourcePath = tempDir / "source.mp3";
        Directory.CreateDirectory(cachePath);

        using var service = CreateTranscodingService(cachePath);
        var cacheFilePath = GetCacheFilePath(service, sourcePath, "mp3", 128);
        var expectedContent = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        await File.WriteAllBytesAsync(cacheFilePath, expectedContent, TestContext.Current.CancellationToken);

        var sourceLastWriteTimeUtc = DateTime.UtcNow.AddMinutes(-2);
        File.SetLastWriteTimeUtc(cacheFilePath, DateTime.UtcNow.AddMinutes(-1));

        await using var stream = await service.TranscodeToStreamAsync(sourcePath, "mp3", 128, sourceLastWriteTimeUtc: sourceLastWriteTimeUtc, cancellationToken: TestContext.Current.CancellationToken);
        var fileStream = Assert.IsType<FileStream>(stream);

        using var content = new MemoryStream();
        await fileStream.CopyToAsync(content, TestContext.Current.CancellationToken);
        Assert.Equal(expectedContent, content.ToArray());
    }

    [Fact]
    public async Task TranscodeToStreamAsync_WithStaleCache_DoesNotServeCachedFile()
    {
        using var tempDir = TemporaryDirectory.Create();
        var cachePath = tempDir / "cache";
        var sourcePath = tempDir / "source.mp3";
        Directory.CreateDirectory(cachePath);

        using var service = CreateTranscodingService(cachePath);
        var cacheFilePath = GetCacheFilePath(service, sourcePath, "mp3", 128);
        await File.WriteAllBytesAsync(cacheFilePath, [0x11, 0x22, 0x33, 0x44], TestContext.Current.CancellationToken);

        var sourceLastWriteTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        File.SetLastWriteTimeUtc(cacheFilePath, DateTime.UtcNow.AddMinutes(-2));

        _ = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await service.TranscodeToStreamAsync(sourcePath, "mp3", 128, sourceLastWriteTimeUtc: sourceLastWriteTimeUtc, cancellationToken: TestContext.Current.CancellationToken);
        });
    }

    private static TranscodingService CreateTranscodingService(string cachePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["FFmpeg:Path"] = "ffmpeg-does-not-exist",
                ["FFmpeg:MaxConcurrentTranscodes"] = "1",
            })
            .Build();

        var settings = Options.Create(new MusicServerSettings
        {
            CachePath = cachePath,
            EnableTranscodingCache = true,
        });

        return new TranscodingService(NullLogger<TranscodingService>.Instance, configuration, settings);
    }

    private static string GetCacheFilePath(TranscodingService service, string inputPath, string? outputFormat, int? maxBitRate)
    {
        var method = typeof(TranscodingService).GetMethod("GetCacheFilePath", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var value = method.Invoke(service, [inputPath, outputFormat, maxBitRate]);
        return Assert.IsType<string>(value);
    }
}
