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
}
