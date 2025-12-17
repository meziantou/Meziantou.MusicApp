using Meziantou.MusicApp.Server.Tests.Helpers;

namespace Meziantou.MusicApp.Server.Tests.Unit;

public class MusicCatalogTests
{
    [Fact]
    public async Task ScanMusicLibrary_HandlesArtistNamesWithTrailingWhitespace()
    {
        // This test reproduces the bug where "Mozart " (with trailing space) would cause KeyNotFoundException
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("mozart/symphony1.mp3", "Symphony No. 1", "Mozart ", "Mozart ", "Symphonies Vol 1", "Classical", 1785, 1);
        testContext.MusicLibrary.CreateTestMp3File("mozart/symphony2.mp3", "Symphony No. 2", " Mozart", " Mozart", "Symphonies Vol 1", "Classical", 1785, 2);
        testContext.MusicLibrary.CreateTestMp3File("mozart/symphony3.mp3", "Symphony No. 3", "Mozart", "Mozart", "Symphonies Vol 2", "Classical", 1786, 1);

        var catalog = await testContext.ScanCatalog();

        // Assert
        var artists = catalog.GetAllArtists().ToList();
        Assert.Single(artists); // All songs should be grouped under single "Mozart" artist
        Assert.Equal("Mozart", artists[0].Name); // Name should be normalized without whitespace

        var albums = catalog.GetRandomAlbums(100).ToList();
        Assert.Equal(2, albums.Count); // Should have 2 albums

        // Both albums should be attributed to Mozart
        Assert.All(albums, album => Assert.Equal("Mozart", album.Artist));

        var songs = catalog.GetAllSongs().ToList();
        Assert.Equal(3, songs.Count);

        // All songs should have the same ArtistId (referencing the normalized "Mozart")
        var artistIds = songs.Select(s => s.ArtistId).Distinct(StringComparer.Ordinal).ToList();
        Assert.Single(artistIds);
    }

    [Fact]
    public async Task ScanMusicLibrary_HandlesAlbumNamesWithWhitespace()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("artist/song1.mp3", "Song 1", "Test Artist", "Test Artist", "Test Album ", "Rock", 2024, 1);
        testContext.MusicLibrary.CreateTestMp3File("artist/song2.mp3", "Song 2", "Test Artist", "Test Artist", " Test Album", "Rock", 2024, 2);
        
        var catalog = await testContext.ScanCatalog();

        var albums = catalog.GetRandomAlbums(100).ToList();
        Assert.Single(albums); // Should be grouped into single album despite whitespace
        Assert.Equal("Test Album", albums[0].Name);
        Assert.Equal(2, albums[0].SongCount);
    }

    [Fact]
    public async Task ScanMusicLibrary_HandlesMultipleArtistsWithVariousWhitespacePatterns()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("artist1/song.mp3", "Song A", "Artist One", "Artist One", "Album A", "Rock", 2024, 1);
        testContext.MusicLibrary.CreateTestMp3File("artist2/song.mp3", "Song B", " Artist Two ", " Artist Two ", "Album B", "Pop", 2024, 1);
        testContext.MusicLibrary.CreateTestMp3File("artist3/song.mp3", "Song C", "Artist Three\t", "Artist Three\t", "Album C", "Jazz", 2024, 1);

        var catalog = await testContext.ScanCatalog();

        var artists = catalog.GetAllArtists().ToList();
        Assert.Equal(3, artists.Count);

        // Verify artist names are normalized
        Assert.Contains(artists, a => a.Name == "Artist One");
        Assert.Contains(artists, a => a.Name == "Artist Two");
        Assert.Contains(artists, a => a.Name == "Artist Three");

        // Each artist should have 1 album
        Assert.All(artists, artist => Assert.Equal(1, artist.AlbumCount));
    }

    [Fact]
    public async Task ScanMusicLibrary_HandlesEmptyAndNullArtistNames()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("unknown/song1.mp3", "Song 1", null, null, "Unknown Album", "Pop", 2024, 1);
        testContext.MusicLibrary.CreateTestMp3File("unknown/song2.mp3", "Song 2", "", "", "Unknown Album", "Pop", 2024, 2);
        testContext.MusicLibrary.CreateTestMp3File("unknown/song3.mp3", "Song 3", "   ", "   ", "Unknown Album", "Pop", 2024, 3);

        var catalog = await testContext.ScanCatalog();

        var artists = catalog.GetAllArtists().ToList();
        Assert.Single(artists);
        Assert.Equal("Unknown Artist", artists[0].Name);

        var albums = catalog.GetRandomAlbums(100).ToList();
        Assert.Single(albums);
        Assert.Equal("Unknown Album", albums[0].Name);
        Assert.Equal("Unknown Artist", albums[0].Artist);
        Assert.Equal(3, albums[0].SongCount);
    }

    [Fact]
    public async Task ScanMusicLibrary_AssignsSongReferencesCorrectly()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("test/song1.mp3", "Test Song 1", " Test Artist ", " Test Artist ", " Test Album ", "Rock", 2024, 1);
        testContext.MusicLibrary.CreateTestMp3File("test/song2.mp3", "Test Song 2", "Test Artist", "Test Artist", "Test Album", "Rock", 2024, 2);

        var catalog = await testContext.ScanCatalog();

        var songs = catalog.GetAllSongs().ToList();
        Assert.Equal(2, songs.Count);

        // All songs should have AlbumId and ArtistId set
        Assert.All(songs, song =>
        {
            Assert.NotNull(song.AlbumId);
            Assert.NotNull(song.ArtistId);
        });

        // All songs should reference the same album and artist
        Assert.Equal(songs[0].AlbumId, songs[1].AlbumId);
        Assert.Equal(songs[0].ArtistId, songs[1].ArtistId);

        // Verify we can retrieve the artist and album by ID
        var artist = catalog.GetArtist(songs[0].ArtistId!);
        Assert.NotNull(artist);
        Assert.Equal("Test Artist", artist.Name);

        var album = catalog.GetAlbum(songs[0].AlbumId!);
        Assert.NotNull(album);
        Assert.Equal("Test Album", album.Name);
        Assert.Equal("Test Artist", album.Artist);
    }
}
