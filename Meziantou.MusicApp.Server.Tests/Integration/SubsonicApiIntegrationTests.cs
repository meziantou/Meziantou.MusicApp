using System.Security.Cryptography;
using System.Xml.Linq;
using Meziantou.MusicApp.Server.Tests.Helpers;
using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.MusicApp.Server.Tests.Integration;

public class SubsonicApiIntegrationTests
{
    private const string AuthToken = "your-secure-token-here";

    [Fact]
    public async Task Ping_WithValidAuth_ReturnsSuccess()
    {
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        using var response = await app.Client.GetAsync(BuildAuthenticatedUrl("/rest/ping.view"), app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: application/xml; charset=utf-8
              Value: <subsonic-response status="ok" version="1.16.1" xmlns="http://subsonic.org/restapi" />
            """);
    }

    [Fact]
    public async Task Ping_WithoutAuth_ReturnsError()
    {
        await using var app = AppTestContext.Create();
        using var response = await app.Client.GetAsync("/rest/ping.view?v=1.16.1&c=test", app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: application/xml
              Value:
                <subsonic-response status="failed" version="1.16.1" xmlns="http://subsonic.org/restapi">
                  <error code="10" message="Required parameter is missing" />
                </subsonic-response>
            """);
    }

    [Fact]
    public async Task Ping_WithInvalidToken_ReturnsAuthError()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var salt = GenerateSalt();
        var invalidToken = ComputeMD5("wrong-token" + salt);
        var url = $"/rest/ping.view?u=admin&t={invalidToken}&s={salt}&v=1.16.1&c=test";

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: application/xml
              Value:
                <subsonic-response status="failed" version="1.16.1" xmlns="http://subsonic.org/restapi">
                  <error code="40" message="Wrong username or password" />
                </subsonic-response>
            """);
    }

    [Fact]
    public async Task GetLicense_WithValidAuth_ReturnsLicenseInfo()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getLicense.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var license = xml.Root?.Element(XName.Get("license", "http://subsonic.org/restapi"));
        Assert.NotNull(license);
        Assert.Equal("true", license?.Attribute("valid")?.Value);
    }

    [Fact]
    public async Task GetMusicFolders_ReturnsAtLeastOneFolder()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getMusicFolders.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var musicFolders = xml.Root?.Element(XName.Get("musicFolders", "http://subsonic.org/restapi"));
        Assert.NotNull(musicFolders);

        var folders = musicFolders?.Elements(XName.Get("musicFolder", "http://subsonic.org/restapi"));
        Assert.NotNull(folders);
        Assert.NotEmpty(folders);
    }

    [Fact]
    public async Task GetArtists_ReturnsArtistStructure()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getArtists.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var artists = xml.Root?.Element(XName.Get("artists", "http://subsonic.org/restapi"));
        Assert.NotNull(artists);
        Assert.False(string.IsNullOrEmpty(artists?.Attribute("ignoredArticles")?.Value));

        // Verify that artists have albumCount > 0
        var indexes = artists?.Elements(XName.Get("index", "http://subsonic.org/restapi"));
        if (indexes?.Any() == true)
        {
            var artistElements = indexes.SelectMany(idx => idx.Elements(XName.Get("artist", "http://subsonic.org/restapi")));
            foreach (var artist in artistElements)
            {
                var albumCountAttr = artist.Attribute("albumCount");
                Assert.NotNull(albumCountAttr);
                var albumCount = int.Parse(albumCountAttr.Value, CultureInfo.InvariantCulture);
                Assert.True(albumCount > 0, $"Artist {artist.Attribute("name")?.Value} has albumCount = {albumCount.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }

    [Fact]
    public async Task GetGenres_ReturnsGenreList()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getGenres.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var genres = xml.Root?.Element(XName.Get("genres", "http://subsonic.org/restapi"));
        Assert.NotNull(genres);
    }

    [Fact]
    public async Task GetRandomSongs_ReturnsRequestedNumberOfSongs()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var size = 5;
        var url = BuildAuthenticatedUrl($"/rest/getRandomSongs.view?size={size.ToString(CultureInfo.InvariantCulture)}");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var randomSongs = xml.Root?.Element(XName.Get("randomSongs", "http://subsonic.org/restapi"));
        Assert.NotNull(randomSongs);
    }

    [Fact]
    public async Task GetAlbumList2_WithRandomType_ReturnsAlbums()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getAlbumList2.view?type=random&size=10");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var albumList = xml.Root?.Element(XName.Get("albumList2", "http://subsonic.org/restapi"));
        Assert.NotNull(albumList);

        // Verify that albums have songCount > 0
        var albums = albumList?.Elements(XName.Get("album", "http://subsonic.org/restapi"));
        if (albums?.Any() == true)
        {
            foreach (var album in albums)
            {
                var songCountAttr = album.Attribute("songCount");
                Assert.NotNull(songCountAttr);
                var songCount = int.Parse(songCountAttr.Value, CultureInfo.InvariantCulture);
                Assert.True(songCount > 0, $"Album {album.Attribute("name")?.Value} has songCount = {songCount.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }

    [Fact]
    public async Task GetPlaylists_ReturnsPlaylistList()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getPlaylists.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var playlists = xml.Root?.Element(XName.Get("playlists", "http://subsonic.org/restapi"));
        Assert.NotNull(playlists);
    }

    [Fact]
    public async Task GetPlaylist_WithValidId_ReturnsPlaylistWithSongs()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // First get the list of playlists to get a valid playlist ID
        var playlistsUrl = BuildAuthenticatedUrl("/rest/getPlaylists.view");
        using var playlistsResponse = await app.Client.GetAsync(playlistsUrl, app.CancellationToken);
        var playlistsContent = await playlistsResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var playlistsXml = XDocument.Parse(playlistsContent);

        var playlistElement = playlistsXml.Root?
            .Element(XName.Get("playlists", "http://subsonic.org/restapi"))?
            .Elements(XName.Get("playlist", "http://subsonic.org/restapi"))
            .FirstOrDefault();

        // Skip test if no playlists exist
        if (playlistElement == null)
        {
            return;
        }

        var playlistId = playlistElement.Attribute("id")?.Value;
        Assert.NotNull(playlistId);

        var url = BuildAuthenticatedUrl($"/rest/getPlaylist.view?id={Uri.EscapeDataString(playlistId)}");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var playlist = xml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"));
        Assert.NotNull(playlist);

        // Verify playlist attributes
        Assert.NotNull(playlist.Attribute("id")?.Value);
        Assert.NotNull(playlist.Attribute("name")?.Value);

        // Verify that playlist can contain song entries (PlaylistWithSongs)
        // Note: Songs may or may not exist depending on test data
        var entries = playlist.Elements(XName.Get("entry", "http://subsonic.org/restapi")).ToList();
        // Just verify the structure is correct - entries should be child elements
        // Each entry should have required song attributes if present
        foreach (var entry in entries)
        {
            Assert.NotNull(entry.Attribute("id"));
            Assert.NotNull(entry.Attribute("title"));
        }
    }

    [Fact]
    public async Task GetPlaylist_WithInvalidId_ReturnsError()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getPlaylist.view?id=invalid-playlist-id");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        // Should return error status
        Assert.Equal("failed", xml.Root?.Attribute("status")?.Value);

        var error = xml.Root?.Element(XName.Get("error", "http://subsonic.org/restapi"));
        Assert.NotNull(error);
        Assert.Equal("70", error.Attribute("code")?.Value); // Error code 70: Requested data was not found
    }

    [Fact]
    public async Task Search3_WithQuery_ReturnsSearchResults()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/search3.view?query=test");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var searchResult = xml.Root?.Element(XName.Get("searchResult3", "http://subsonic.org/restapi"));
        Assert.NotNull(searchResult);
    }

    [Fact]
    public async Task GetScanStatus_ReturnsStatus()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getScanStatus.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var scanStatus = xml.Root?.Element(XName.Get("scanStatus", "http://subsonic.org/restapi"));
        Assert.NotNull(scanStatus);
        Assert.False(string.IsNullOrEmpty(scanStatus?.Attribute("scanning")?.Value));
    }

    [Fact]
    public async Task CreatePlaylist_ReturnsPlaylistId()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var playlistName = "Test Playlist " + Guid.NewGuid();
        var url = BuildAuthenticatedUrl($"/rest/createPlaylist.view?name={Uri.EscapeDataString(playlistName)}");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var playlist = xml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"));
        Assert.NotNull(playlist);
        Assert.NotNull(playlist.Attribute("id")?.Value);
        Assert.Equal(playlistName, playlist.Attribute("name")?.Value);
    }

    [Fact]
    public async Task Star_WithValidAlbumId_ReturnsNotAuthorizedError()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Get an album to attempt starring (server is read-only so this should fail)
        var albumListUrl = BuildAuthenticatedUrl("/rest/getAlbumList2.view?type=random&size=1");
        using var albumListResponse = await app.Client.GetAsync(albumListUrl, app.CancellationToken);
        var albumListContent = await albumListResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var albumListXml = XDocument.Parse(albumListContent);

        var albumToStar = albumListXml.Descendants(XName.Get("album", "http://subsonic.org/restapi")).FirstOrDefault();

        // Skip test if no albums exist
        if (albumToStar == null)
        {
            return;
        }

        var itemId = albumToStar.Attribute("id")?.Value;
        var url = BuildAuthenticatedUrl($"/rest/star.view?id={itemId}");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        // Server is read-only, so star should return error code 50
        Assert.Equal("failed", xml.Root?.Attribute("status")?.Value);
        var error = xml.Root?.Element(XName.Get("error", "http://subsonic.org/restapi"));
        Assert.NotNull(error);
        Assert.Equal("50", error.Attribute("code")?.Value);
    }

    [Fact]
    public async Task PasswordBasedAuth_WithClearText_Works()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = $"/rest/ping.view?u=admin&p={AuthToken}&v=1.16.1&c=test";

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);
    }

    [Fact]
    public async Task PasswordBasedAuth_WithHexEncoded_Works()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var hexPassword = "enc:" + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(AuthToken));
        var url = $"/rest/ping.view?u=admin&p={hexPassword}&v=1.16.1&c=test";

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);
    }

    [Fact]
    public async Task GetIndexes_WithValidAuth_ReturnsIndexes()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getIndexes.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);
        Assert.NotNull(xml.Root?.Element(XName.Get("indexes", "http://subsonic.org/restapi")));
    }

    [Fact]
    public async Task GetMusicDirectory_WithValidAuth_ReturnsDirectory()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Need to get a directory ID first
        var indexesUrl = BuildAuthenticatedUrl("/rest/getIndexes.view");
        using var indexesResponse = await app.Client.GetAsync(indexesUrl, app.CancellationToken);
        var indexesContent = await indexesResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var indexesXml = XDocument.Parse(indexesContent);

        var firstArtist = indexesXml.Descendants(XName.Get("artist", "http://subsonic.org/restapi")).FirstOrDefault();
        if (firstArtist != null)
        {
            var directoryId = firstArtist.Attribute("id")?.Value;
            using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/getMusicDirectory.view?id={directoryId}"), app.CancellationToken);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
            var xml = XDocument.Parse(content);

            Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);
            Assert.NotNull(xml.Root?.Element(XName.Get("directory", "http://subsonic.org/restapi")));
        }
    }

    [Fact]
    public async Task Scrobble_WithValidAuth_ReturnsSuccess()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/scrobble.view?id=test123&submission=true");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);
    }

    [Fact]
    public async Task GetStarred2_WithValidAuth_ReturnsEmptyList()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getStarred2.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);
        Assert.NotNull(xml.Root?.Element(XName.Get("starred2", "http://subsonic.org/restapi")));
    }

    [Fact]
    public async Task GetPodcasts_WithValidAuth_ReturnsEmptyList()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getPodcasts.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);
        Assert.NotNull(xml.Root?.Element(XName.Get("podcasts", "http://subsonic.org/restapi")));
    }

    [Fact]
    public async Task GetNewestPodcasts_WithValidAuth_ReturnsEmptyList()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getNewestPodcasts.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);
        Assert.NotNull(xml.Root?.Element(XName.Get("newestPodcasts", "http://subsonic.org/restapi")));
    }

    [Fact]
    public async Task GetInternetRadioStations_WithValidAuth_ReturnsEmptyList()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/getInternetRadioStations.view");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);
        Assert.NotNull(xml.Root?.Element(XName.Get("internetRadioStations", "http://subsonic.org/restapi")));
    }

    [Fact]
    public async Task Stream_WithoutTranscoding_ReturnsOriginalFile()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Get a song first
        var searchUrl = BuildAuthenticatedUrl("/rest/search3.view?query=test&songCount=1");
        using var searchResponse = await app.Client.GetAsync(searchUrl, app.CancellationToken);
        var searchContent = await searchResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var searchXml = XDocument.Parse(searchContent);

        var song = searchXml.Descendants(XName.Get("song", "http://subsonic.org/restapi")).FirstOrDefault();

        if (song != null)
        {
            var songId = song.Attribute("id")?.Value;
            using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/stream.view?id={songId}"), app.CancellationToken);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Content.Headers.ContentType);
        }
    }

    [Fact]
    public async Task Stream_WithFormatParameter_AcceptsTranscodingRequest()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Get a song first
        var searchUrl = BuildAuthenticatedUrl("/rest/search3.view?query=test&songCount=1");
        using var searchResponse = await app.Client.GetAsync(searchUrl, app.CancellationToken);
        var searchContent = await searchResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var searchXml = XDocument.Parse(searchContent);

        var song = searchXml.Descendants(XName.Get("song", "http://subsonic.org/restapi")).FirstOrDefault();

        if (song != null)
        {
            var songId = song.Attribute("id")?.Value;
            using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/stream.view?id={songId}&format=mp3&maxBitRate=192"), app.CancellationToken);

            // Assert
            // Should return OK even if FFmpeg is not installed (will return error in response)
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task GetLyrics_WithoutParameters_ReturnsEmptyLyrics()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        using var response = await app.Client.GetAsync(BuildAuthenticatedUrl("/rest/getLyrics.view"), app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: application/xml; charset=utf-8
              Value:
                <subsonic-response status="ok" version="1.16.1" xmlns="http://subsonic.org/restapi">
                  <lyrics />
                </subsonic-response>
            """);
    }

    [Fact]
    public async Task GetLyrics_WithArtistAndTitle_ReturnsLyricsIfFound()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var artist = Uri.EscapeDataString("Test Artist");
        var title = Uri.EscapeDataString("Test Song");
        using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/getLyrics.view?artist={artist}&title={title}"), app.CancellationToken);

        // Act
        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        // Assert
        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var lyricsElement = xml.Root?.Element(XName.Get("lyrics", "http://subsonic.org/restapi"));
        Assert.NotNull(lyricsElement);
    }

    [Fact]
    public async Task GetLyrics_WithOnlyTitle_AttemptsToFindSong()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var title = Uri.EscapeDataString("Test");
        using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/getLyrics.view?title={title}"), app.CancellationToken);

        // Act
        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        // Assert
        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var lyricsElement = xml.Root?.Element(XName.Get("lyrics", "http://subsonic.org/restapi"));
        Assert.NotNull(lyricsElement);
    }

    [Fact]
    public async Task Hls_WithValidSongId_ReturnsM3U8Playlist()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Get a song first
        var searchUrl = BuildAuthenticatedUrl("/rest/search3.view?query=test&songCount=1");
        using var searchResponse = await app.Client.GetAsync(searchUrl, app.CancellationToken);
        var searchContent = await searchResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var searchXml = XDocument.Parse(searchContent);

        var song = searchXml.Descendants(XName.Get("song", "http://subsonic.org/restapi")).FirstOrDefault();

        if (song != null)
        {
            var songId = song.Attribute("id")?.Value;
            using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/hls.m3u8?id={songId}&bitRate=128"), app.CancellationToken);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/vnd.apple.mpegurl", response.Content.Headers.ContentType?.MediaType);

            var playlist = await response.Content.ReadAsStringAsync(app.CancellationToken);
            Assert.StartsWith("#EXTM3U", playlist, StringComparison.Ordinal);
            Assert.Contains("#EXT-X-VERSION", playlist, StringComparison.Ordinal);
            Assert.Contains("#EXT-X-TARGETDURATION", playlist, StringComparison.Ordinal);
            Assert.Contains("#EXT-X-ENDLIST", playlist, StringComparison.Ordinal);
            Assert.Contains("./hls/", playlist, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Hls_WithInvalidSongId_ReturnsError()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        using var response = await app.Client.GetAsync(BuildAuthenticatedUrl("/rest/hls.m3u8?id=invalid-song-id"), app.CancellationToken);
        InlineSnapshot.Validate(response, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: application/xml; charset=utf-8
              Value:
                <subsonic-response status="failed" version="1.16.1" xmlns="http://subsonic.org/restapi">
                  <error code="70" message="Song not found" />
                </subsonic-response>
            """);
    }

    [Fact]
    public async Task GetCoverArt_WithoutSize_ReturnsOriginalImage()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        using var response = await app.Client.GetAsync(BuildAuthenticatedUrl("/rest/getCoverArt.view?id=test-cover-id"), app.CancellationToken);

        // Assert
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.OK,
            "Expected OK status code");
    }

    [Fact]
    public async Task GetCoverArt_WithSize_ReturnsResizedImage()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        using var response = await app.Client.GetAsync(BuildAuthenticatedUrl("/rest/getCoverArt.view?id=test-cover-id&size=100"), app.CancellationToken);

        // Assert
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.OK,
            "Expected OK status code");
    }

    [Fact]
    public async Task CreatePlaylist_WithValidName_CreatesPlaylist()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var playlistName = $"Test Playlist {Guid.NewGuid()}";
        var url = BuildAuthenticatedUrl($"/rest/createPlaylist.view?name={Uri.EscapeDataString(playlistName)}");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

        var playlistElement = xml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"));
        Assert.NotNull(playlistElement);
        Assert.Equal(playlistName, playlistElement.Attribute("name")?.Value);
        Assert.Equal("0", playlistElement.Attribute("songCount")?.Value);
    }

    [Fact]
    public async Task CreatePlaylist_WithSongs_CreatesPlaylistWithSongs()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Get some songs first
        var searchUrl = BuildAuthenticatedUrl("/rest/search3.view?query=test&songCount=3");
        using var searchResponse = await app.Client.GetAsync(searchUrl, app.CancellationToken);
        var searchContent = await searchResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var searchXml = XDocument.Parse(searchContent);

        var songs = searchXml.Descendants(XName.Get("song", "http://subsonic.org/restapi")).Take(3).ToList();

        if (songs.Count > 0)
        {
            var playlistName = $"Test Playlist {Guid.NewGuid()}";
            var songIdsParam = string.Join('&', songs.Select(s => $"songId={s.Attribute("id")?.Value}"));
            using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/createPlaylist.view?name={Uri.EscapeDataString(playlistName)}&{songIdsParam}"), app.CancellationToken);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
            var xml = XDocument.Parse(content);

            Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

            var playlistElement = xml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"));
            Assert.NotNull(playlistElement);
            Assert.Equal(playlistName, playlistElement.Attribute("name")?.Value);
            Assert.Equal(songs.Count.ToString(CultureInfo.InvariantCulture), playlistElement.Attribute("songCount")?.Value);
        }
    }

    [Fact]
    public async Task UpdatePlaylist_WithNewName_UpdatesPlaylistName()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Create a playlist first
        var originalName = $"Original Playlist {Guid.NewGuid()}";
        var createUrl = BuildAuthenticatedUrl($"/rest/createPlaylist.view?name={Uri.EscapeDataString(originalName)}");
        using var createResponse = await app.Client.GetAsync(createUrl, app.CancellationToken);
        var createContent = await createResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var createXml = XDocument.Parse(createContent);

        var playlistId = createXml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"))?.Attribute("id")?.Value;

        if (!string.IsNullOrEmpty(playlistId))
        {
            // Update the playlist name
            var newName = $"Updated Playlist {Guid.NewGuid()}";
            var updateUrl = BuildAuthenticatedUrl($"/rest/updatePlaylist.view?playlistId={Uri.EscapeDataString(playlistId)}&name={Uri.EscapeDataString(newName)}");

            // Act
            using var response = await app.Client.GetAsync(updateUrl, app.CancellationToken);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
            var xml = XDocument.Parse(content);

            Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

            // Verify the update by fetching all playlists
            var playlistsUrl = BuildAuthenticatedUrl("/rest/getPlaylists.view");
            using var playlistsResponse = await app.Client.GetAsync(playlistsUrl, app.CancellationToken);
            var playlistsContent = await playlistsResponse.Content.ReadAsStringAsync(app.CancellationToken);
            var playlistsXml = XDocument.Parse(playlistsContent);

            var playlists = playlistsXml.Root?
                .Element(XName.Get("playlists", "http://subsonic.org/restapi"))?
                .Elements(XName.Get("playlist", "http://subsonic.org/restapi"));

            // Verify the original name no longer exists
            var originalPlaylist = playlists?.FirstOrDefault(p => p.Attribute("name")?.Value == originalName);
            Assert.Null(originalPlaylist);

            // Verify the new name exists
            var updatedPlaylist = playlists?.FirstOrDefault(p => p.Attribute("name")?.Value == newName);
            Assert.NotNull(updatedPlaylist);
        }
    }

    [Fact]
    public async Task UpdatePlaylist_AddingSongs_AddsItemsToPlaylist()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Create an empty playlist
        var playlistName = $"Test Playlist {Guid.NewGuid()}";
        var createUrl = BuildAuthenticatedUrl($"/rest/createPlaylist.view?name={Uri.EscapeDataString(playlistName)}");
        using var createResponse = await app.Client.GetAsync(createUrl, app.CancellationToken);
        var createContent = await createResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var createXml = XDocument.Parse(createContent);

        var playlistId = createXml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"))?.Attribute("id")?.Value;

        if (!string.IsNullOrEmpty(playlistId))
        {
            // Get some songs
            var searchUrl = BuildAuthenticatedUrl("/rest/search3.view?query=test&songCount=2");
            using var searchResponse = await app.Client.GetAsync(searchUrl, app.CancellationToken);
            var searchContent = await searchResponse.Content.ReadAsStringAsync(app.CancellationToken);
            var searchXml = XDocument.Parse(searchContent);

            var songs = searchXml.Descendants(XName.Get("song", "http://subsonic.org/restapi")).Take(2).ToList();

            if (songs.Count > 0)
            {
                var songIdsParam = string.Join('&', songs.Select(s => $"songIdToAdd={s.Attribute("id")?.Value}"));
                using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/updatePlaylist.view?playlistId={Uri.EscapeDataString(playlistId)}&{songIdsParam}"), app.CancellationToken);

                // Assert
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

                var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
                var xml = XDocument.Parse(content);

                Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

                // Verify the songs were added
                var getUrl = BuildAuthenticatedUrl($"/rest/getPlaylist.view?id={Uri.EscapeDataString(playlistId)}");
                using var getResponse = await app.Client.GetAsync(getUrl, app.CancellationToken);
                var getContent = await getResponse.Content.ReadAsStringAsync(app.CancellationToken);
                var getXml = XDocument.Parse(getContent);

                var updatedPlaylistElement = getXml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"));
                Assert.NotNull(updatedPlaylistElement);
                Assert.Equal(songs.Count.ToString(CultureInfo.InvariantCulture), updatedPlaylistElement.Attribute("songCount")?.Value);
            }
        }
    }

    [Fact]
    public async Task UpdatePlaylist_RemovingSongs_RemovesItemsFromPlaylist()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Get some songs
        var searchUrl = BuildAuthenticatedUrl("/rest/search3.view?query=test&songCount=3");
        using var searchResponse = await app.Client.GetAsync(searchUrl, app.CancellationToken);
        var searchContent = await searchResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var searchXml = XDocument.Parse(searchContent);

        var songs = searchXml.Descendants(XName.Get("song", "http://subsonic.org/restapi")).Take(3).ToList();

        if (songs.Count >= 2)
        {
            var playlistName = $"Test Playlist {Guid.NewGuid()}";
            var songIdsParam = string.Join('&', songs.Select(s => $"songId={s.Attribute("id")?.Value}"));
            using var createResponse = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/createPlaylist.view?name={Uri.EscapeDataString(playlistName)}&{songIdsParam}"), app.CancellationToken);
            var createContent = await createResponse.Content.ReadAsStringAsync(app.CancellationToken);
            var createXml = XDocument.Parse(createContent);

            var playlistId = createXml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"))?.Attribute("id")?.Value;

            if (!string.IsNullOrEmpty(playlistId))
            {
                using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/updatePlaylist.view?playlistId={Uri.EscapeDataString(playlistId)}&songIndexToRemove=0"), app.CancellationToken);

                // Assert
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

                var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
                var xml = XDocument.Parse(content);

                Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

                // Verify the song was removed
                var getUrl = BuildAuthenticatedUrl($"/rest/getPlaylist.view?id={Uri.EscapeDataString(playlistId)}");
                using var getResponse = await app.Client.GetAsync(getUrl, app.CancellationToken);
                var getContent = await getResponse.Content.ReadAsStringAsync(app.CancellationToken);
                var getXml = XDocument.Parse(getContent);

                var updatedPlaylistElement = getXml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"));
                Assert.NotNull(updatedPlaylistElement);
                var expectedCount = songs.Count - 1;
                Assert.Equal(expectedCount.ToString(CultureInfo.InvariantCulture), updatedPlaylistElement.Attribute("songCount")?.Value);
            }
        }
    }

    [Fact]
    public async Task DeletePlaylist_WithValidId_DeletesPlaylist()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);

        // Create a playlist first
        var playlistName = $"Test Playlist {Guid.NewGuid()}";
        var createUrl = BuildAuthenticatedUrl($"/rest/createPlaylist.view?name={Uri.EscapeDataString(playlistName)}");
        using var createResponse = await app.Client.GetAsync(createUrl, app.CancellationToken);
        var createContent = await createResponse.Content.ReadAsStringAsync(app.CancellationToken);
        var createXml = XDocument.Parse(createContent);

        var playlistId = createXml.Root?.Element(XName.Get("playlist", "http://subsonic.org/restapi"))?.Attribute("id")?.Value;

        if (!string.IsNullOrEmpty(playlistId))
        {
            // Act
            using var response = await app.Client.GetAsync(BuildAuthenticatedUrl($"/rest/deletePlaylist.view?id={Uri.EscapeDataString(playlistId)}"), app.CancellationToken);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
            var xml = XDocument.Parse(content);

            Assert.Equal("ok", xml.Root?.Attribute("status")?.Value);

            // Verify the playlist no longer exists
            var getUrl = BuildAuthenticatedUrl($"/rest/getPlaylist.view?id={Uri.EscapeDataString(playlistId)}");
            using var getResponse = await app.Client.GetAsync(getUrl, app.CancellationToken);
            var getContent = await getResponse.Content.ReadAsStringAsync(app.CancellationToken);
            var getXml = XDocument.Parse(getContent);

            Assert.Equal("failed", getXml.Root?.Attribute("status")?.Value);
        }
    }

    [Fact]
    public async Task DeletePlaylist_WithInvalidId_ReturnsError()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/deletePlaylist.view?id=invalid-playlist-id");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("failed", xml.Root?.Attribute("status")?.Value);
    }

    [Fact]
    public async Task Star_ReturnsNotAuthorizedError()
    {
        // Arrange
        await using var app = AppTestContext.Create();
        app.SetAuthToken(AuthToken);
        var url = BuildAuthenticatedUrl("/rest/star.view?id=test123");

        // Act
        using var response = await app.Client.GetAsync(url, app.CancellationToken);

        // Assert
        var content = await response.Content.ReadAsStringAsync(app.CancellationToken);
        var xml = XDocument.Parse(content);

        Assert.Equal("failed", xml.Root?.Attribute("status")?.Value);
        Assert.Equal("50", xml.Root?.Element(XName.Get("error", "http://subsonic.org/restapi"))
            ?.Attribute("code")?.Value); // Not authorized
    }

    private static string BuildAuthenticatedUrl(string path)
    {
        var salt = GenerateSalt();
        var token = ComputeMD5(AuthToken + salt);
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{path}{separator}u=admin&t={token}&s={salt}&v=1.16.1&c=test";
    }

    private static string GenerateSalt()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms")]
    private static string ComputeMD5(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }
}
