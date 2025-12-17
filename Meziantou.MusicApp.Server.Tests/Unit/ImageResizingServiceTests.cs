using Meziantou.MusicApp.Server.Services;
using Meziantou.MusicApp.Server.Tests.Helpers;

namespace Meziantou.MusicApp.Server.Tests.Unit;

public class ImageResizingServiceTests
{
    private static async Task<byte[]> ResizeImage(byte[] content, int? size = null)
    {
        await using var context = AppTestContext.Create();
        var service = context.GetRequiredService<ImageResizingService>();
        return await service.ResizeImageAsync(content, size, context.CancellationToken);
    }

    [Fact]
    public async Task ResizeImageAsync_WithNullSize_ReturnsOriginalImage()
    {
        var originalImage = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        var result = await ResizeImage(originalImage);
        Assert.Equal(originalImage, result);
    }

    [Fact]
    public async Task ResizeImageAsync_WithZeroSize_ReturnsOriginalImage()
    {
        var originalImage = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        var result = await ResizeImage(originalImage, 0);
        Assert.Equal(originalImage, result);
    }

    [Fact]
    public async Task ResizeImageAsync_WithNegativeSize_ReturnsOriginalImage()
    {
        var originalImage = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        var result = await ResizeImage(originalImage, -100);
        Assert.Equal(originalImage, result);
    }

    [Fact]
    public async Task ResizeImageAsync_WithValidSize_ReturnsResizedImage()
    {
        // Create a simple test image (1x1 red PNG)
        var originalImage = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");
        var result = await ResizeImage(originalImage, 100);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }
}
