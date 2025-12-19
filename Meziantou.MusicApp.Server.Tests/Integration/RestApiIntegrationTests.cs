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
        using var response = await app.Client.PostAsync("/api/scan", content: null, app.CancellationToken);
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
        using var response = await app.Client.GetAsync("/api/scan/status", app.CancellationToken);
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

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/scan/status");
        request.Headers.Add("Origin", "http://localhost:3000");

        using var response = await app.Client.SendAsync(request, app.CancellationToken);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOriginValues));
        Assert.Equal("http://localhost:3000", allowOriginValues.Single());

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCredentialsValues));
        Assert.Equal("true", allowCredentialsValues.Single());
    }

    [Fact]
    public async Task ComputeReplayGain_WithValidSong_ReturnsResponse()
    {
        await using var app = AppTestContext.Create();
        app.MusicLibrary.CreateTestMp3File("test.mp3", "Test Song", "Test Artist", "Test Artist", "Test Album", "Rock", 2024, 1);
        var service = await app.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        var song = songs.First();

        var requestBody = System.Text.Json.JsonSerializer.Serialize(new { id = song.Id });
        using var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

        using var response = await app.Client.PostAsync("/api/songs/compute-replay-gain", content, app.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(app.CancellationToken);
        Assert.Contains("\"id\":", responseBody, StringComparison.Ordinal);
        Assert.Contains("\"title\":", responseBody, StringComparison.Ordinal);
        Assert.Contains("\"success\":", responseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeReplayGain_WithInvalidSongId_ReturnsNotFound()
    {
        await using var app = AppTestContext.Create();
        await app.ScanCatalog();

        var requestBody = System.Text.Json.JsonSerializer.Serialize(new { id = "invalid-song-id" });
        using var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

        using var response = await app.Client.PostAsync("/api/songs/compute-replay-gain", content, app.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
