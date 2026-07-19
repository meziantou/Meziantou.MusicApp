using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Meziantou.MusicApp.Server.Tests.Helpers;
using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.MusicApp.Server.Tests.Integration;

public class RestApiIntegrationTests
{
    [Fact]
    public async Task GetPlaylists_IncludesPlaylistSize()
    {
        await using var app = AppTestContext.Create();
        app.MusicLibrary.CreateTestMp3File("song-1.mp3", title: "Song 1", artist: "Artist", albumArtist: "Artist", album: "Album", genre: "Rock", year: 2024, track: 1);
        app.MusicLibrary.CreateTestMp3File("song-2.mp3", title: "Song 2", artist: "Artist", albumArtist: "Artist", album: "Album", genre: "Rock", year: 2024, track: 2);
        await app.MusicLibrary.CreatePlaylistFile("test-playlist.m3u", "song-1.mp3\nsong-2.mp3");
        var service = await app.ScanCatalog();
        var expectedSize = service.GetAllSongs().Sum(s => s.Size);

        using var response = await app.Client.GetAsync("/api/playlists.json", app.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(app.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        var playlists = payload["playlists"]?.AsArray();
        Assert.NotNull(playlists);
        var playlist = playlists
            .Select(node => node?.AsObject())
            .FirstOrDefault(node => node?["name"]?.GetValue<string>() == "test-playlist");
        Assert.NotNull(playlist);
        Assert.Equal(expectedSize, playlist["size"]?.GetValue<long>());
    }

    [Fact]
    public async Task GetPlaylistTracks_IncludesPlaylistSize()
    {
        await using var app = AppTestContext.Create();
        app.MusicLibrary.CreateTestMp3File("song-1.mp3", title: "Song 1", artist: "Artist", albumArtist: "Artist", album: "Album", genre: "Rock", year: 2024, track: 1);
        app.MusicLibrary.CreateTestMp3File("song-2.mp3", title: "Song 2", artist: "Artist", albumArtist: "Artist", album: "Album", genre: "Rock", year: 2024, track: 2);
        await app.MusicLibrary.CreatePlaylistFile("test-playlist.m3u", "song-1.mp3\nsong-2.mp3");
        var service = await app.ScanCatalog();
        var playlist = service.GetPlaylists().Single(p => p.Name == "test-playlist");
        var expectedSize = playlist.Items.Sum(item => item.Song.Size);

        using var response = await app.Client.GetAsync($"/api/playlists/{Uri.EscapeDataString(playlist.Id)}.json", app.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(app.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(expectedSize, payload["size"]?.GetValue<long>());
    }

    [Fact]
    public async Task TriggerScan_WithValidAuth_ReturnsOk()
    {
        // Act
        await using var app = AppTestContext.Create();
        using var response = await app.Client.PostAsync("/api/scan.json", content: null, app.CancellationToken);
        InlineSnapshot
            .WithSerializer(serializer =>
            {
                serializer.ScrubJsonValue("$.isScanning", node => "[redacted]");
                serializer.ScrubJsonValue("$.activeScanGeneration", node => "[redacted]");
                serializer.ScrubJsonValue("$.lastCompletedScanGeneration", node => "[redacted]");
            })
            .Validate(response, """
                StatusCode: 200 (OK)
                Headers:
                  Cache-Control: no-store, must-revalidate, no-cache
                Content:
                  Headers:
                    Content-Type: application/json; charset=utf-8
                  Value:
                    {
                      "isScanning": "[redacted]",
                      "isInitialScanCompleted": true,
                      "scanCount": 0,
                      "activeScanGeneration": "[redacted]",
                      "lastCompletedScanGeneration": "[redacted]",
                      "invalidPlaylists": []
                    }
                """);
    }

    [Fact]
    public async Task TriggerScan_WithForceQuery_WithValidAuth_ReturnsOk()
    {
        await using var app = AppTestContext.Create();
        using var response = await app.Client.PostAsync("/api/scan.json?force=true", content: null, app.CancellationToken);
        InlineSnapshot
            .WithSerializer(serializer =>
            {
                serializer.ScrubJsonValue("$.isScanning", node => "[redacted]");
                serializer.ScrubJsonValue("$.activeScanGeneration", node => "[redacted]");
                serializer.ScrubJsonValue("$.lastCompletedScanGeneration", node => "[redacted]");
            })
            .Validate(response, """
                StatusCode: 200 (OK)
                Headers:
                  Cache-Control: no-store, must-revalidate, no-cache
                Content:
                  Headers:
                    Content-Type: application/json; charset=utf-8
                  Value:
                    {
                      "isScanning": "[redacted]",
                      "isInitialScanCompleted": true,
                      "scanCount": 0,
                      "activeScanGeneration": "[redacted]",
                      "lastCompletedScanGeneration": "[redacted]",
                      "invalidPlaylists": []
                    }
                """);
    }

    [Fact]
    public async Task ScrobbleRoute_IsNotAvailable()
    {
        await using var app = AppTestContext.Create();
        using var response = await app.Client.PostAsJsonAsync("/api/scrobble.json", new { id = "song-1", submission = true }, app.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CleanupTranscodingCache_DeletesOnlyTranscodingFiles()
    {
        await using var app = AppTestContext.Create();

        app.MusicCachePath.CreateParentDirectory();
        await File.WriteAllTextAsync(app.MusicCachePath, "{}", app.CancellationToken);

        var coverFilePath = app.CachePath / "cover" / "cover.jpg";
        coverFilePath.CreateParentDirectory();
        await File.WriteAllBytesAsync(coverFilePath, [0x01, 0x02], app.CancellationToken);

        var transcodingCacheFile = app.CachePath / $"{new string('a', 64)}.mp3";
        var transcodingCacheTempFile = app.CachePath / $"{new string('b', 64)}.opus.tmp";
        await File.WriteAllBytesAsync(transcodingCacheFile, [0x03], app.CancellationToken);
        await File.WriteAllBytesAsync(transcodingCacheTempFile, [0x04], app.CancellationToken);

        using var response = await app.Client.PostAsync("/api/cache/transcoding/cleanup.json", content: null, app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-store, must-revalidate, no-cache
            Content:
              Headers:
                Content-Type: application/json; charset=utf-8
              Value:
                {
                  "deletedFileCount": 2,
                  "failedFileCount": 0
                }
            """);

        Assert.False(File.Exists(transcodingCacheFile));
        Assert.False(File.Exists(transcodingCacheTempFile));
        Assert.True(File.Exists(app.MusicCachePath));
        Assert.True(File.Exists(coverFilePath));
    }

    [Fact]
    public async Task GetScanStatus_WithValidAuth_ReturnsStatus()
    {
        // Act
        await using var app = AppTestContext.Create();
        using var response = await app.Client.GetAsync("/api/scan/status.json", app.CancellationToken);
        InlineSnapshot
            .WithSerializer(serializer =>
            {
                serializer.ScrubJsonValue("$.isScanning", node => "[redacted]");
                serializer.ScrubJsonValue("$.activeScanGeneration", node => "[redacted]");
                serializer.ScrubJsonValue("$.lastCompletedScanGeneration", node => "[redacted]");
            })
            .Validate(response, """
                StatusCode: 200 (OK)
                Headers:
                  Cache-Control: no-store, must-revalidate, no-cache
                Content:
                  Headers:
                    Content-Type: application/json; charset=utf-8
                  Value:
                    {
                      "isScanning": "[redacted]",
                      "isInitialScanCompleted": true,
                      "scanCount": 0,
                      "activeScanGeneration": "[redacted]",
                      "lastCompletedScanGeneration": "[redacted]",
                      "invalidPlaylists": []
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
            Headers:
              Cache-Control: no-store, must-revalidate, no-cache
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
            Headers:
              Cache-Control: no-store, must-revalidate, no-cache
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
            Headers:
              Cache-Control: no-store, must-revalidate, no-cache
            Content:
              Headers:
                Content-Type: application/json; charset=utf-8
              Value:
                {
                  "lyrics": "First line\nSecond line"
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
            Headers:
              Cache-Control: no-store, must-revalidate, no-cache
            Content:
              Headers:
                Content-Type: application/json; charset=utf-8
              Value:
                {
                  "error": "Song not found"
                }
            """);
    }

    [Theory]
    [InlineData("song", "cover.png", "image/png")]
    [InlineData("song", "cover.jpg", "image/jpeg")]
    [InlineData("song", "cover.webp", "image/webp")]
    [InlineData("song", "cover.avif", "image/avif")]
    [InlineData("album", "cover.png", "image/png")]
    [InlineData("album", "cover.jpg", "image/jpeg")]
    [InlineData("album", "cover.webp", "image/webp")]
    [InlineData("album", "cover.avif", "image/avif")]
    [InlineData("artist", "cover.png", "image/png")]
    [InlineData("artist", "cover.jpg", "image/jpeg")]
    [InlineData("artist", "cover.webp", "image/webp")]
    [InlineData("artist", "cover.avif", "image/avif")]
    public async Task GetCover_SupportsMultipleFormats(string entityType, string coverFileName, string expectedContentType)
    {
        await using var app = AppTestContext.Create();
        app.MusicLibrary.CreateTestMp3File("Artist/Album/song.mp3", title: "Song", artist: "Artist", album: "Album");
        var coverData = GetCoverData(coverFileName);
        app.MusicLibrary.AddFile($"Artist/Album/{coverFileName}", coverData);

        var library = await app.ScanCatalog();
        var id = entityType switch
        {
            "song" => library.GetAllSongs().Single().Id,
            "album" => library.GetAllAlbums().Single().Id,
            "artist" => library.GetAllArtists().Single().Id,
            _ => throw new InvalidOperationException($"Unsupported entity type: {entityType}"),
        };

        var endpoint = entityType switch
        {
            "song" => $"/api/songs/{id}/cover",
            "album" => $"/api/albums/{id}/cover",
            "artist" => $"/api/artists/{id}/cover",
            _ => throw new InvalidOperationException($"Unsupported entity type: {entityType}"),
        };

        using var response = await app.Client.GetAsync(endpoint, app.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedContentType, response.Content.Headers.ContentType?.MediaType);
        var returnedCoverData = await response.Content.ReadAsByteArrayAsync(app.CancellationToken);
        Assert.Equal(coverData, returnedCoverData);
    }

    private static byte[] GetCoverData(string coverFileName)
    {
        return Path.GetExtension(coverFileName).ToLowerInvariant() switch
        {
            ".png" =>
            [
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            ],
            ".jpg" or ".jpeg" =>
            [
                0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46,
                0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9,
            ],
            ".webp" =>
            [
                0x52, 0x49, 0x46, 0x46, 0x18, 0x00, 0x00, 0x00,
                0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20,
                0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            ],
            ".avif" =>
            [
                0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70,
                0x61, 0x76, 0x69, 0x66, 0x00, 0x00, 0x00, 0x00,
                0x61, 0x76, 0x69, 0x66, 0x6D, 0x69, 0x66, 0x31,
            ],
            _ => throw new InvalidOperationException($"Unsupported cover extension for test: {coverFileName}"),
        };
    }
}
