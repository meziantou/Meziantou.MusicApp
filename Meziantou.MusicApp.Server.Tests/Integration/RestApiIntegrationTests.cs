using Meziantou.MusicApp.Server.Tests.Helpers;
using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.MusicApp.Server.Tests.Integration;

public class RestApiIntegrationTests
{
    [Fact]
    public async Task TriggerScan_WithValidAuth_ReturnsOk()
    {
        // Act
        await using var app = AppTestContext.Create();
        using var response = await app.Client.PostAsync("/api/scan.json", content: null, app.CancellationToken);
        InlineSnapshot
            .WithSerializer(serializer => serializer.ScrubJsonValue("$.isScanning", node => "[redacted]"))
            .Validate(response, """
                StatusCode: 200 (OK)
                Content:
                  Headers:
                    Content-Type: application/json; charset=utf-8
                  Value:
                    {
                      "isScanning": "[redacted]",
                      "isInitialScanCompleted": true,
                      "scanCount": 0
                    }
                """);
    }

    [Fact]
    public async Task GetScanStatus_WithValidAuth_ReturnsStatus()
    {
        // Act
        await using var app = AppTestContext.Create();
        using var response = await app.Client.GetAsync("/api/scan/status.json", app.CancellationToken);
        InlineSnapshot
            .WithSerializer(serializer => serializer.ScrubJsonValue("$.isScanning", node => "[redacted]"))
            .Validate(response, """
                StatusCode: 200 (OK)
                Content:
                  Headers:
                    Content-Type: application/json; charset=utf-8
                  Value:
                    {
                      "isScanning": "[redacted]",
                      "isInitialScanCompleted": true,
                      "scanCount": 0
                    }
                """);
    }

    [Fact]
    public async Task Health_IsAccessibleWithoutAuthentication()
    {
        await using var app = AppTestContext.Create();
        using var response = await app.Client.GetAsync("/health", app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-store, no-cache
              Pragma: no-cache
            Content:
              Headers:
                Expires: Thu, 01 Jan 1970 00:00:00 GMT
                Content-Type: text/plain
              Value: Healthy
            """);
    }

    [Fact]
    public async Task Cors_AllowsAnyOrigin_WithCredentials()
    {
        await using var app = AppTestContext.Create();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/scan/status.json");
        request.Headers.Add("Origin", "http://localhost:3000");

        using var response = await app.Client.SendAsync(request, app.CancellationToken);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOriginValues));
        Assert.Equal("http://localhost:3000", allowOriginValues.Single());

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCredentialsValues));
        Assert.Equal("true", allowCredentialsValues.Single());
    }

    [Fact]
    public async Task GetSongLyrics_WithEmbeddedLyrics_ReturnsLyrics()
    {
        await using var app = AppTestContext.Create();
        var expectedLyrics = "This is a test song\nWith multiple lines\nOf lyrics";
        app.MusicLibrary.CreateTestMp3File("song-with-lyrics.mp3", title: "Song With Lyrics", artist: "Test Artist", lyrics: expectedLyrics);

        var library = await app.ScanCatalog();
        var song = library.GetAllSongs().First();

        using var response = await app.Client.GetAsync($"/api/songs/{song.Id}/lyrics.json", app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: application/json; charset=utf-8
              Value:
                {
                  "lyrics": "This is a test song\nWith multiple lines\nOf lyrics"
                }
            """);
    }

    [Fact]
    public async Task GetSongLyrics_WithoutLyrics_ReturnsNull()
    {
        await using var app = AppTestContext.Create();
        app.MusicLibrary.CreateTestMp3File("song-no-lyrics.mp3", title: "Song Without Lyrics", artist: "Test Artist");

        var library = await app.ScanCatalog();
        var song = library.GetAllSongs().First();

        using var response = await app.Client.GetAsync($"/api/songs/{song.Id}/lyrics.json", app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: application/json; charset=utf-8
              Value: {}
            """);
    }

    [Fact]
    public async Task GetSongLyrics_WithLrcFile_ReturnsLyrics()
    {
        await using var app = AppTestContext.Create();
        app.MusicLibrary.CreateTestMp3File("song-with-lrc.mp3", title: "Song With LRC", artist: "Test Artist");
        await app.MusicLibrary.CreateLrcFile("song-with-lrc.lrc", "[00:00.00]First line\n[00:05.00]Second line");

        var library = await app.ScanCatalog();
        var song = library.GetAllSongs().First();

        using var response = await app.Client.GetAsync($"/api/songs/{song.Id}/lyrics.json", app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: application/json; charset=utf-8
              Value:
                {
                  "lyrics": "First line\r\nSecond line"
                }
            """);
    }

    [Fact]
    public async Task GetSongLyrics_WithNonExistentSong_ReturnsNotFound()
    {
        await using var app = AppTestContext.Create();

        using var response = await app.Client.GetAsync("/api/songs/non-existent-id/lyrics.json", app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 404 (NotFound)
            Content:
              Headers:
                Content-Type: application/json; charset=utf-8
              Value:
                {
                  "error": "Song not found"
                }
            """);
    }
}
