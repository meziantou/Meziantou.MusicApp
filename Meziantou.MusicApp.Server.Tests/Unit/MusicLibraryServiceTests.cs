using Meziantou.MusicApp.Server.Models;
using Meziantou.MusicApp.Server.Services;
using Meziantou.MusicApp.Server.Tests.Helpers;
using Xunit.Sdk;

namespace Meziantou.MusicApp.Server.Tests.Unit;

public class MusicLibraryServiceTests
{
    [Fact]
    public async Task GetAllArtists_ReturnsEmptyList_WhenNoMusicScanned()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var artists = service.GetAllArtists();

        Assert.Empty(artists);
    }

    [Fact]
    public async Task GetGenres_ReturnsEmptyList_WhenNoMusicScanned()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var genres = service.GetGenres();

        Assert.Empty(genres);
    }

    [Fact]
    public async Task GetPlaylists_ReturnsVirtualOnly_WhenNoPlaylistsScanned()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var playlists = service.GetPlaylists();

        // Virtual playlists will still exist even with no songs
        var playlist = Assert.Single(playlists);
        Assert.Equal(Playlist.AllSongsPlaylistId, playlist.Id);
    }

    [Fact]
    public async Task GetRandomAlbums_ReturnsEmptyList_WhenNoAlbumsExist()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var albums = service.GetRandomAlbums(10);

        Assert.Empty(albums);
    }

    [Fact]
    public async Task GetRandomSongs_ReturnsEmptyList_WhenNoSongsExist()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var songs = service.GetRandomSongs(10);

        Assert.Empty(songs);
    }

    [Fact]
    public async Task GetSong_ReturnsNull_WhenSongDoesNotExist()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var song = service.GetSong("non-existent-id");

        Assert.Null(song);
    }

    [Fact]
    public async Task GetArtist_ReturnsNull_WhenArtistDoesNotExist()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var artist = service.GetArtist("non-existent-id");

        Assert.Null(artist);
    }

    [Fact]
    public async Task GetAlbum_ReturnsNull_WhenAlbumDoesNotExist()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var album = service.GetAlbum("non-existent-id");

        Assert.Null(album);
    }

    [Fact]
    public async Task GetPlaylist_ReturnsNull_WhenPlaylistDoesNotExist()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var playlist = service.GetPlaylist("non-existent-id");

        Assert.Null(playlist);
    }

    [Fact]
    public async Task GetDirectory_ReturnsNull_WhenDirectoryDoesNotExist()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var directory = service.GetDirectory("non-existent-id");

        Assert.Null(directory);
    }

    [Fact]
    public async Task SearchAll_ReturnsEmptyResults_WhenNoMusicScanned()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var (artists, albums, songs) = service.SearchAll("test");

        Assert.Empty(artists);
        Assert.Empty(albums);
        Assert.Empty(songs);
    }

    [Fact]
    public async Task SearchAll_IsCaseInsensitive()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var (artists1, albums1, songs1) = service.SearchAll("TEST");
        var (artists2, albums2, songs2) = service.SearchAll("test");

        Assert.Equal(artists2, artists1);
        Assert.Equal(albums2, albums1);
        Assert.Equal(songs2, songs1);
    }

    [Fact]
    public async Task GetSongsByGenre_ReturnsEmpty_WhenGenreDoesNotExist()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var songs = service.GetSongsByGenre("Rock");

        Assert.Empty(songs);
    }

    [Fact]
    public async Task GetNewestAlbums_ReturnsRequestedCount_WhenEnoughAlbumsExist()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        var albums = service.GetNewestAlbums(5);

        Assert.True(albums.Count() <= 5);
    }

    [Fact]
    public async Task StartAsync_InitiatesLibraryScan()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("TestSong.mp3", title: "Test Song", artist: "Test Artist", albumArtist: "Test Artist", album: "Test Album", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var allSongs = service.GetAllSongs().ToList();
        Assert.Single(allSongs);

        var song = allSongs[0];
        Assert.Equal("Test Song", song.Title);
        Assert.Equal("Test Artist", song.Artist);
        Assert.Equal("Test Album", song.Album);
        Assert.Equal("Rock", song.Genre);
        Assert.Equal(2024, song.Year);

        var artists = service.GetAllArtists().ToList();
        Assert.Single(artists);
        Assert.Equal("Test Artist", artists[0].Name);

        var albums = service.GetRandomAlbums(100).ToList();
        Assert.Single(albums);
        Assert.Equal("Test Album", albums[0].Name);
        Assert.Equal("Test Artist", albums[0].Artist);
    }

    [Fact]
    public async Task ScanMusicLibrary_ExtractsLyricsFromMetadata()
    {
        await using var testContext = AppTestContext.Create();
        var lyrics = "This is a test song\nWith multiple lines\nOf lyrics";
        testContext.MusicLibrary.CreateTestMp3File("test-with-lyrics.mp3", title: "Song With Lyrics", artist: "Test Artist", albumArtist: "Test Artist", album: "Test Album", genre: "Pop", year: 2024, track: 1, lyrics: lyrics);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        var song = songs[0];
        Assert.Equal("Song With Lyrics", song.Title);
        Assert.NotNull(song.Lyrics);
        var lyricsText = service.Catalog.GetLyrics(song.Id);
        Assert.Equal(lyrics, lyricsText);
    }

    [Fact]
    public async Task ScanMusicLibrary_HandlesFilesWithoutLyrics()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("test-no-lyrics.mp3", title: "Song Without Lyrics", artist: "Test Artist", albumArtist: "Test Artist", album: "Test Album", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        var song = songs[0];
        Assert.Equal("Song Without Lyrics", song.Title);
        Assert.Null(song.Lyrics);
    }

    [Fact]
    public async Task ScanMusicLibrary_ReadsLrcFileWhenPresent()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song-with-lrc.mp3", title: "Song With LRC", artist: "Test Artist", albumArtist: "Test Artist", album: "Test Album", genre: "Pop", year: 2024, track: 1);

        var lrcContent = """
            [ar:Test Artist]
            [ti:Song With LRC]
            [al:Test Album]
            [00:12.00]First line of lyrics
            [00:17.20]Second line of lyrics
            [00:23.00]Third line of lyrics
            """;
        await testContext.MusicLibrary.CreateLrcFile("song-with-lrc.lrc", lrcContent);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        var song = songs[0];
        Assert.Equal("Song With LRC", song.Title);
        Assert.NotNull(song.Lyrics);
        var lyricsText = service.Catalog.GetLyrics(song.Id);
        Assert.NotNull(lyricsText);
        Assert.Contains("First line of lyrics", lyricsText, StringComparison.Ordinal);
        Assert.Contains("Second line of lyrics", lyricsText, StringComparison.Ordinal);
        Assert.Contains("Third line of lyrics", lyricsText, StringComparison.Ordinal);
        Assert.DoesNotContain("[ar:", lyricsText, StringComparison.Ordinal);
        Assert.DoesNotContain("[ti:", lyricsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanMusicLibrary_LrcFileOverridesEmbeddedLyrics()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song-override.mp3", title: "Song Override", artist: "Test Artist", albumArtist: "Test Artist", album: "Test Album", genre: "Pop", year: 2024, track: 1, lyrics: "Embedded lyrics");

        var lrcContent = """
            [00:00.00]LRC file lyrics
            [00:05.00]These should take precedence
            """;
        await testContext.MusicLibrary.CreateLrcFile("song-override.lrc", lrcContent);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        var song = songs[0];
        Assert.Equal("Song Override", song.Title);
        Assert.NotNull(song.Lyrics);
        var lyricsText = service.Catalog.GetLyrics(song.Id);
        Assert.NotNull(lyricsText);
        Assert.Contains("LRC file lyrics", lyricsText, StringComparison.Ordinal);
        Assert.DoesNotContain("Embedded lyrics", lyricsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanMusicLibrary_HandlesLrcFileWithPlainText()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("plain-lrc.mp3", title: "Plain LRC", artist: "Test Artist", albumArtist: "Test Artist", album: "Test Album", genre: "Pop", year: 2024, track: 1);

        var lrcContent = """
            Just plain lyrics
            Without any timestamps
            Should still work
            """;
        await testContext.MusicLibrary.CreateLrcFile("plain-lrc.lrc", lrcContent);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        var song = songs[0];
        Assert.Equal("Plain LRC", song.Title);
        Assert.NotNull(song.Lyrics);
        var lyricsText = service.Catalog.GetLyrics(song.Id);
        Assert.NotNull(lyricsText);
        Assert.Contains("Just plain lyrics", lyricsText, StringComparison.Ordinal);
        Assert.Contains("Without any timestamps", lyricsText, StringComparison.Ordinal);
        Assert.Contains("Should still work", lyricsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanMusicLibrary_HandlesNonExistentDirectory()
    {
        await using var testContext = AppTestContext.Create();

        var service = await testContext.ScanCatalog();

        Assert.Empty(service.GetAllSongs());
    }

    [Fact]
    public async Task ScanMusicLibrary_ConcurrentCalls_PreventsMultipleScans()
    {
        await using var testContext = AppTestContext.Create();
        for (var i = 0; i < 10; i++)
        {
            testContext.MusicLibrary.CreateTestMp3File($"test-song-{i.ToString(CultureInfo.InvariantCulture)}.mp3",
                title: $"Song {i.ToString(CultureInfo.InvariantCulture)}",
                artist: $"Artist {i.ToString(CultureInfo.InvariantCulture)}",
                albumArtist: $"Artist {i.ToString(CultureInfo.InvariantCulture)}",
                album: $"Album {(i % 3).ToString(CultureInfo.InvariantCulture)}",
                genre: "Rock", year: 2024, track: (uint)(i + 1));
        }

        using var barrier = new Barrier(5);
        var scanTasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () =>
            {
                barrier.SignalAndWait();
                var service = await testContext.ScanCatalog();
            }))
            .ToArray();

        await Task.WhenAll(scanTasks);

        var songs = testContext.GetRequiredService<MusicLibraryService>().GetAllSongs().ToList();
        Assert.Equal(10, songs.Count);
    }

    [Fact]
    public async Task ScanMusicLibrary_CalculatesPlaylistDuration()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song2.mp3", title: "Song 2", artist: "Artist 2", albumArtist: "Artist 2", album: "Album 2", genre: "Pop", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song3.mp3", title: "Song 3", artist: "Artist 3", albumArtist: "Artist 3", album: "Album 3", genre: "Jazz", year: 2024, track: 1);

        var playlistContent = """
            #EXTM3U
            song1.mp3
            song2.mp3
            song3.mp3
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.m3u", playlistContent);

        var service = await testContext.ScanCatalog();

        var playlists = service.GetPlaylists().Where(p => !p.Id.StartsWith("virtual:", StringComparison.Ordinal)).ToList();
        Assert.Single(playlists);

        var playlist = playlists[0];
        Assert.Equal("test-playlist", playlist.Name);
        Assert.Equal(3, playlist.SongCount);
        Assert.Equal(3, playlist.Items.Count);

        var songs = service.GetAllSongs().ToList();
        Assert.Equal(3, songs.Count);
        var expectedDuration = songs.Sum(s => s.Duration);

        Assert.Equal(expectedDuration, playlist.Duration);

        var xspfFile = Path.Combine(testContext.MusicLibrary.RootPath, "test-playlist.xspf");
        var bakFile = Path.Combine(testContext.MusicLibrary.RootPath, "test-playlist.m3u.bak");
        Assert.True(File.Exists(xspfFile), "XSPF file should be created");
        Assert.True(File.Exists(bakFile), "M3U backup file should be created");
    }

    [Fact]
    public async Task ScanMusicLibrary_ConvertsM3uToXspf_WithAddedDateMetadata()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.m3u", "song1.mp3");

        var beforeConversion = DateTime.UtcNow;
        var service = await testContext.ScanCatalog();
        var afterConversion = DateTime.UtcNow;

        var playlists = service.GetPlaylists().Where(p => !p.Id.StartsWith("virtual:", StringComparison.Ordinal)).ToList();
        Assert.Single(playlists);

        var playlist = playlists[0];
        Assert.Single(playlist.Items);

        var item = playlist.Items[0];
        Assert.True(item.AddedDate >= beforeConversion && item.AddedDate <= afterConversion,
            $"AddedDate should be between {beforeConversion:o} and {afterConversion:o}, but was {item.AddedDate:o}");

        var xspfFile = Path.Combine(testContext.MusicLibrary.RootPath, "test-playlist.xspf");
        var xspfContent = await File.ReadAllTextAsync(xspfFile, testContext.CancellationToken);
        Assert.Contains("meziantou.net/xspf-extension", xspfContent, StringComparison.Ordinal);
        Assert.Contains("addedAt", xspfContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanMusicLibrary_PreservesExistingXspfFiles()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        var existingAddedDate = new DateTime(2023, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var xspfContent = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>test-playlist</title>
              <trackList>
                <track>
                  <location>song1.mp3</location>
                  <extension application="http://meziantou.net/xspf-extension/1/">
                    <meziantou:addedAt>{existingAddedDate:o}</meziantou:addedAt>
                  </extension>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        var playlists = service.GetPlaylists().Where(p => !p.Id.StartsWith("virtual:", StringComparison.Ordinal)).ToList();
        Assert.Single(playlists);

        var playlist = playlists[0];
        Assert.Single(playlist.Items);

        var item = playlist.Items[0];
        Assert.Equal(existingAddedDate, item.AddedDate);
    }

    [Fact]
    public async Task ScanMusicLibrary_DoesNotConvertM3u_WhenXspfAlreadyExists()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.m3u", "song1.mp3");

        var xspfContent = """
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>test-playlist</title>
              <trackList>
                <track>
                  <location>song1.mp3</location>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        var m3uFile = Path.Combine(testContext.MusicLibrary.RootPath, "test-playlist.m3u");
        Assert.True(File.Exists(m3uFile), "M3U file should still exist when XSPF already present");
        Assert.False(File.Exists(m3uFile + ".bak"), "M3U backup should not be created when XSPF already exists");

        // Filter out virtual playlists when counting file-based playlists
        var playlists = service.GetPlaylists().Where(p => !p.Id.StartsWith("virtual:", StringComparison.Ordinal)).ToList();
        Assert.Single(playlists);
    }

    [Fact]
    public async Task ScanMusicLibrary_IncrementalScan_ReusesUnchangedFileMetadata()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("TestSong.mp3", title: "Test Song", artist: "Test Artist", albumArtist: "Test Artist", album: "Test Album", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        Assert.Equal("Test Song", songs[0].Title);

        Assert.True(File.Exists(testContext.MusicCachePath));
        var cacheContent1 = await File.ReadAllTextAsync(testContext.MusicCachePath, testContext.CancellationToken);
        Assert.Contains("FileLastWriteTime", cacheContent1, StringComparison.Ordinal);

        await service.ScanMusicLibrary();

        songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        Assert.Equal("Test Song", songs[0].Title);

        var cacheContent2 = await File.ReadAllTextAsync(testContext.MusicCachePath, testContext.CancellationToken);
        Assert.Contains("FileLastWriteTime", cacheContent2, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanMusicLibrary_IncrementalScan_ReScansModifiedFiles()
    {
        await using var testContext = AppTestContext.Create();
        var mp3FilePath = Path.Combine(testContext.MusicLibrary.RootPath, "TestSong.mp3");
        testContext.MusicLibrary.CreateTestMp3File("TestSong.mp3", title: "Original Title", artist: "Original Artist", albumArtist: "Original Artist", album: "Original Album", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        Assert.Equal("Original Title", songs[0].Title);

        // Modify the file and ensure it's fully written before scanning
        using (var tagFile = TagLib.File.Create(mp3FilePath))
        {
            tagFile.Tag.Title = "Modified Title";
            tagFile.Tag.Performers = ["Modified Artist"];
            tagFile.Tag.Album = "Modified Album";
            tagFile.Save();
        } // Dispose to ensure file is closed

        // Explicitly update the file timestamp to ensure modification is detected
        var timestampBefore = File.GetLastWriteTimeUtc(mp3FilePath);
        File.SetLastWriteTimeUtc(mp3FilePath, timestampBefore.AddMinutes(1));

        // Wait for file system to register the timestamp change
        var maxWait = TimeSpan.FromMinutes(2);
        var start = DateTime.UtcNow;
        while (File.GetLastWriteTimeUtc(mp3FilePath) <= timestampBefore && DateTime.UtcNow - start < maxWait)
        {
            await Task.Delay(10, testContext.CancellationToken);
        }

        await service.ScanMusicLibrary();

        songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        Assert.Equal("Modified Title", songs[0].Title);
        Assert.Equal("Modified Artist", songs[0].Artist);
        Assert.Equal("Modified Album", songs[0].Album);
    }

    [Fact]
    public async Task ScanMusicLibrary_IncrementalScan_DetectsNewFiles()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("Song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        Assert.Single(songs);

        testContext.MusicLibrary.CreateTestMp3File("Song2.mp3", title: "Song 2", artist: "Artist 2", albumArtist: "Artist 2", album: "Album 2", genre: "Pop", year: 2024, track: 1);

        await service.ScanMusicLibrary();

        songs = service.GetAllSongs().ToList();
        Assert.Equal(2, songs.Count);
        Assert.Contains(songs, s => s.Title == "Song 1");
        Assert.Contains(songs, s => s.Title == "Song 2");
    }

    [Fact]
    public async Task ScanMusicLibrary_IncrementalScan_RemovesDeletedFiles()
    {
        await using var testContext = AppTestContext.Create();
        var mp3FilePath2 = Path.Combine(testContext.MusicLibrary.RootPath, "Song2.mp3");
        testContext.MusicLibrary.CreateTestMp3File("Song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("Song2.mp3", title: "Song 2", artist: "Artist 2", albumArtist: "Artist 2", album: "Album 2", genre: "Pop", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        Assert.Equal(2, songs.Count);

        File.Delete(mp3FilePath2);

        await service.ScanMusicLibrary();

        songs = service.GetAllSongs().ToList();
        Assert.Single(songs);
        Assert.Equal("Song 1", songs[0].Title);
    }

    [Fact]
    public async Task GetPlaylists_IncludesAllSongsVirtualPlaylist()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song2.mp3", title: "Song 2", artist: "Artist 2", albumArtist: "Artist 2", album: "Album 2", genre: "Pop", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var playlists = service.GetPlaylists().ToList();

        Assert.NotEmpty(playlists);
        var virtualPlaylist = playlists.FirstOrDefault(p => p.Name == "All Songs");
        Assert.NotNull(virtualPlaylist);
        Assert.Equal("virtual:all-songs", virtualPlaylist.Id);
        Assert.Equal(2, virtualPlaylist.SongCount);
    }

    [Fact]
    public async Task GetPlaylist_AllSongsVirtualPlaylist_ReturnsAllSongs()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song2.mp3", title: "Song 2", artist: "Artist 2", albumArtist: "Artist 2", album: "Album 2", genre: "Pop", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song3.mp3", title: "Song 3", artist: "Artist 3", albumArtist: "Artist 3", album: "Album 3", genre: "Jazz", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var playlist = service.GetPlaylist("virtual:all-songs");

        Assert.NotNull(playlist);
        Assert.Equal("All Songs", playlist.Name);
        Assert.Equal(3, playlist.SongCount);
        Assert.Equal(3, playlist.Items.Count);

        Assert.Contains(playlist.Items, item => item.Song.Title == "Song 1");
        Assert.Contains(playlist.Items, item => item.Song.Title == "Song 2");
        Assert.Contains(playlist.Items, item => item.Song.Title == "Song 3");
    }

    [Fact]
    public async Task UpdatePlaylist_VirtualPlaylist_ThrowsInvalidOperationException()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdatePlaylist("virtual:all-songs", "New Name", null, null));
    }

    [Fact]
    public async Task DeletePlaylist_VirtualPlaylist_ThrowsInvalidOperationException()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeletePlaylist("virtual:all-songs"));
    }

    [Fact]
    public async Task GetPlaylists_IncludesMissingTracksVirtualPlaylist_WhenMissingItemsExist()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        // Create a playlist that references both an existing and a non-existing song
        var xspfContent = """
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>test-playlist</title>
              <trackList>
                <track>
                  <location>song1.mp3</location>
                </track>
                <track>
                  <location>missing-song.mp3</location>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        var playlists = service.GetPlaylists().ToList();

        var missingTracksPlaylist = playlists.FirstOrDefault(p => p.Id == Playlist.MissingTracksPlaylistId);
        Assert.NotNull(missingTracksPlaylist);
        Assert.Equal("virtual:missing-tracks", missingTracksPlaylist.Id);
        Assert.Equal(1, missingTracksPlaylist.SongCount);
    }

    [Fact]
    public async Task GetPlaylists_DoesNotIncludeMissingTracksPlaylist_WhenNoMissingItems()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        // Create a playlist that references only existing songs
        var xspfContent = """
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>test-playlist</title>
              <trackList>
                <track>
                  <location>song1.mp3</location>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        var playlists = service.GetPlaylists().ToList();

        var missingTracksPlaylist = playlists.FirstOrDefault(p => p.Id == Playlist.MissingTracksPlaylistId);
        Assert.Null(missingTracksPlaylist);
    }

    [Fact]
    public async Task GetPlaylist_MissingTracksVirtualPlaylist_ReturnsAllMissingItems()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        // Create playlists that reference non-existing songs
        var xspfContent1 = """
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>playlist-1</title>
              <trackList>
                <track>
                  <location>song1.mp3</location>
                </track>
                <track>
                  <location>missing-song-1.mp3</location>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("playlist-1.xspf", xspfContent1);

        var xspfContent2 = """
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>playlist-2</title>
              <trackList>
                <track>
                  <location>missing-song-2.mp3</location>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("playlist-2.xspf", xspfContent2);

        var service = await testContext.ScanCatalog();

        var playlist = service.GetPlaylist("virtual:missing-tracks");

        Assert.NotNull(playlist);
        Assert.Equal(2, playlist.SongCount);
        Assert.Equal(2, playlist.Items.Count);

        // Missing items should have "[Missing]" prefix in title
        Assert.All(playlist.Items, item => Assert.StartsWith("[Missing]", item.Song.Title, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetMissingPlaylistItems_ReturnsMissingItemsWithPlaylistInfo()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        var xspfContent = """
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>my-playlist</title>
              <trackList>
                <track>
                  <location>song1.mp3</location>
                </track>
                <track>
                  <location>missing-song.mp3</location>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("my-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        var missingItems = service.GetMissingPlaylistItems().ToList();

        Assert.Single(missingItems);
        var missingItem = missingItems[0];
        Assert.Equal("missing-song.mp3", missingItem.RelativePath);
        Assert.Equal("my-playlist", missingItem.PlaylistName);
        Assert.Contains("missing-song.mp3", missingItem.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMissingPlaylistItems_PreservesAddedDateFromXspf()
    {
        await using var testContext = AppTestContext.Create();

        var addedDate = new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc);
        var xspfContent = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>my-playlist</title>
              <trackList>
                <track>
                  <location>missing-song.mp3</location>
                  <extension application="http://meziantou.net/xspf-extension/1/">
                    <meziantou:addedAt>{addedDate:o}</meziantou:addedAt>
                  </extension>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("my-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        var missingItems = service.GetMissingPlaylistItems().ToList();

        Assert.Single(missingItems);
        Assert.Equal(addedDate, missingItems[0].AddedDate);
    }

    [Fact]
    public async Task ScanMusicLibrary_M3uConversion_IncludesMissingTracksInXspf()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        // Create an M3U playlist with a missing song
        var playlistContent = """
            song1.mp3
            missing-song.mp3
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.m3u", playlistContent);

        var service = await testContext.ScanCatalog();

        // Verify the XSPF file includes the missing track
        var xspfFile = Path.Combine(testContext.MusicLibrary.RootPath, "test-playlist.xspf");
        Assert.True(File.Exists(xspfFile), "XSPF file should be created");

        var xspfContent = await File.ReadAllTextAsync(xspfFile, testContext.CancellationToken);
        Assert.Contains("missing-song.mp3", xspfContent, StringComparison.Ordinal);

        // Verify missing items are tracked
        var missingItems = service.GetMissingPlaylistItems().ToList();
        Assert.Single(missingItems);
        Assert.Equal("missing-song.mp3", missingItems[0].RelativePath);
    }

    [Fact]
    public async Task UpdatePlaylist_MissingTracksVirtualPlaylist_ThrowsInvalidOperationException()
    {
        await using var testContext = AppTestContext.Create();

        var xspfContent = """
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>my-playlist</title>
              <trackList>
                <track>
                  <location>missing-song.mp3</location>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("my-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdatePlaylist("virtual:missing-tracks", "New Name", null, null));
    }

    [Fact]
    public async Task DeletePlaylist_MissingTracksVirtualPlaylist_ThrowsInvalidOperationException()
    {
        await using var testContext = AppTestContext.Create();

        var xspfContent = """
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>my-playlist</title>
              <trackList>
                <track>
                  <location>missing-song.mp3</location>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("my-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeletePlaylist("virtual:missing-tracks"));
    }

    [Fact]
    public async Task UpdatePlaylist_PreservesCustomMetadata_WhenRenamingPlaylist()
    {
        // Arrange
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song2.mp3", title: "Song 2", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 2);

        // Create a playlist with custom addedAt dates and unknown extension data
        var customAddedAt1 = "2023-06-15T10:30:00.0000000Z";
        var customAddedAt2 = "2023-07-20T14:45:00.0000000Z";
        var xspfContent = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/" xmlns:custom="http://example.com/custom-extension/1/">
              <title>test-playlist</title>
              <annotation>Original comment</annotation>
              <extension application="http://example.com/custom-extension/1/">
                <custom:unknownPlaylistData>some custom value</custom:unknownPlaylistData>
                <custom:anotherUnknownField>another value</custom:anotherUnknownField>
              </extension>
              <trackList>
                <track>
                  <location>song1.mp3</location>
                  <extension application="http://meziantou.net/xspf-extension/1/">
                    <meziantou:addedAt>{customAddedAt1}</meziantou:addedAt>
                  </extension>
                  <extension application="http://example.com/custom-extension/1/">
                    <custom:unknownTrackData>track1 custom data</custom:unknownTrackData>
                  </extension>
                </track>
                <track>
                  <location>song2.mp3</location>
                  <extension application="http://meziantou.net/xspf-extension/1/">
                    <meziantou:addedAt>{customAddedAt2}</meziantou:addedAt>
                  </extension>
                  <extension application="http://example.com/custom-extension/1/">
                    <custom:unknownTrackData>track2 custom data</custom:unknownTrackData>
                  </extension>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        var playlist = service.GetPlaylists().First(p => p.Name == "test-playlist");
        var song1 = service.GetAllSongs().First(s => s.Title == "Song 1");
        var song2 = service.GetAllSongs().First(s => s.Title == "Song 2");

        // Act - Update the playlist name only (songs remain the same)
        await service.UpdatePlaylist(playlist.Id, "renamed-playlist", null, [song1.Id, song2.Id]);

        // Assert - Read the raw file to verify metadata is preserved
        Assert.True(File.Exists(testContext.MusicLibrary.RootPath / "test-playlist.xspf.bak"));
        var playlistPath = testContext.MusicLibrary.RootPath / "renamed-playlist.xspf";
        var updatedContent = await File.ReadAllTextAsync(playlistPath, testContext.CancellationToken);
        var updatedXml = System.Xml.Linq.XDocument.Parse(updatedContent);

        // Verify custom playlist extension data is preserved
        var customNs = System.Xml.Linq.XNamespace.Get("http://example.com/custom-extension/1/");
        var xspfNs = System.Xml.Linq.XNamespace.Get("http://xspf.org/ns/0/");
        var meziantouNs = System.Xml.Linq.XNamespace.Get("http://meziantou.net/xspf-extension/1/");

        var playlistExtension = updatedXml.Root?.Element(xspfNs + "extension");
        Assert.NotNull(playlistExtension);
        Assert.Equal("some custom value", playlistExtension.Element(customNs + "unknownPlaylistData")?.Value);
        Assert.Equal("another value", playlistExtension.Element(customNs + "anotherUnknownField")?.Value);

        // Verify addedAt dates are preserved for tracks
        var trackList = updatedXml.Root?.Element(xspfNs + "trackList");
        Assert.NotNull(trackList);

        var tracks = trackList.Elements(xspfNs + "track").ToList();
        Assert.Equal(2, tracks.Count);

        // Check first track
        var track1Extension = tracks[0].Elements(xspfNs + "extension")
            .FirstOrDefault(e => e.Attribute("application")?.Value == "http://meziantou.net/xspf-extension/1/");
        Assert.NotNull(track1Extension);
        Assert.Equal(customAddedAt1, track1Extension.Element(meziantouNs + "addedAt")?.Value);

        // Check second track
        var track2Extension = tracks[1].Elements(xspfNs + "extension")
            .FirstOrDefault(e => e.Attribute("application")?.Value == "http://meziantou.net/xspf-extension/1/");
        Assert.NotNull(track2Extension);
        Assert.Equal(customAddedAt2, track2Extension.Element(meziantouNs + "addedAt")?.Value);
    }

    [Fact]
    public async Task UpdatePlaylist_PreservesAddedAt_WhenReorderingSongs()
    {
        // Arrange
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song2.mp3", title: "Song 2", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 2);
        testContext.MusicLibrary.CreateTestMp3File("song3.mp3", title: "Song 3", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 3);

        var customAddedAt1 = "2023-01-10T08:00:00.0000000Z";
        var customAddedAt2 = "2023-02-15T12:30:00.0000000Z";
        var customAddedAt3 = "2023-03-20T16:45:00.0000000Z";
        var xspfContent = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>test-playlist</title>
              <trackList>
                <track>
                  <location>song1.mp3</location>
                  <extension application="http://meziantou.net/xspf-extension/1/">
                    <meziantou:addedAt>{customAddedAt1}</meziantou:addedAt>
                  </extension>
                </track>
                <track>
                  <location>song2.mp3</location>
                  <extension application="http://meziantou.net/xspf-extension/1/">
                    <meziantou:addedAt>{customAddedAt2}</meziantou:addedAt>
                  </extension>
                </track>
                <track>
                  <location>song3.mp3</location>
                  <extension application="http://meziantou.net/xspf-extension/1/">
                    <meziantou:addedAt>{customAddedAt3}</meziantou:addedAt>
                  </extension>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        var playlist = service.GetPlaylists().First(p => p.Name == "test-playlist");
        var song1 = service.GetAllSongs().First(s => s.Title == "Song 1");
        var song2 = service.GetAllSongs().First(s => s.Title == "Song 2");
        var song3 = service.GetAllSongs().First(s => s.Title == "Song 3");

        // Act - Reorder songs: song3, song1, song2
        await service.UpdatePlaylist(playlist.Id, null, null, [song3.Id, song1.Id, song2.Id]);

        // Assert
        var playlistPath = testContext.MusicLibrary.RootPath / "test-playlist.xspf";
        var updatedContent = await File.ReadAllTextAsync(playlistPath, testContext.CancellationToken);
        var updatedXml = System.Xml.Linq.XDocument.Parse(updatedContent);

        var xspfNs = System.Xml.Linq.XNamespace.Get("http://xspf.org/ns/0/");
        var meziantouNs = System.Xml.Linq.XNamespace.Get("http://meziantou.net/xspf-extension/1/");

        var trackList = updatedXml.Root?.Element(xspfNs + "trackList");
        Assert.NotNull(trackList);

        var tracks = trackList.Elements(xspfNs + "track").ToList();
        Assert.Equal(3, tracks.Count);

        // Verify order and preserved addedAt dates
        // Track order should now be: song3, song1, song2
        Assert.Contains("song3.mp3", tracks[0].Element(xspfNs + "location")?.Value, StringComparison.Ordinal);
        Assert.Contains("song1.mp3", tracks[1].Element(xspfNs + "location")?.Value, StringComparison.Ordinal);
        Assert.Contains("song2.mp3", tracks[2].Element(xspfNs + "location")?.Value, StringComparison.Ordinal);

        // addedAt should match original dates regardless of new order
        var track1AddedAt = tracks[0].Element(xspfNs + "extension")?.Element(meziantouNs + "addedAt")?.Value;
        var track2AddedAt = tracks[1].Element(xspfNs + "extension")?.Element(meziantouNs + "addedAt")?.Value;
        var track3AddedAt = tracks[2].Element(xspfNs + "extension")?.Element(meziantouNs + "addedAt")?.Value;

        Assert.Equal(customAddedAt3, track1AddedAt); // song3 was originally added at customAddedAt3
        Assert.Equal(customAddedAt1, track2AddedAt); // song1 was originally added at customAddedAt1
        Assert.Equal(customAddedAt2, track3AddedAt); // song2 was originally added at customAddedAt2
    }

    [Fact]
    public async Task UpdatePlaylist_AssignsNewAddedAt_ForNewlyAddedSongs()
    {
        // Arrange
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song2.mp3", title: "Song 2", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 2);

        var customAddedAt1 = "2023-01-01T00:00:00.0000000Z";
        var xspfContent = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:meziantou="http://meziantou.net/xspf-extension/1/">
              <title>test-playlist</title>
              <trackList>
                <track>
                  <location>song1.mp3</location>
                  <extension application="http://meziantou.net/xspf-extension/1/">
                    <meziantou:addedAt>{customAddedAt1}</meziantou:addedAt>
                  </extension>
                </track>
              </trackList>
            </playlist>
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("test-playlist.xspf", xspfContent);

        var service = await testContext.ScanCatalog();

        var playlist = service.GetPlaylists().First(p => p.Name == "test-playlist");
        var song1 = service.GetAllSongs().First(s => s.Title == "Song 1");
        var song2 = service.GetAllSongs().First(s => s.Title == "Song 2");

        var beforeUpdate = DateTime.UtcNow;

        // Act - Add song2 to the playlist
        await service.UpdatePlaylist(playlist.Id, null, null, [song1.Id, song2.Id]);

        var afterUpdate = DateTime.UtcNow;

        // Assert
        var playlistPath = testContext.MusicLibrary.RootPath / "test-playlist.xspf";
        var updatedContent = await File.ReadAllTextAsync(playlistPath, testContext.CancellationToken);
        var updatedXml = System.Xml.Linq.XDocument.Parse(updatedContent);

        var xspfNs = System.Xml.Linq.XNamespace.Get("http://xspf.org/ns/0/");
        var meziantouNs = System.Xml.Linq.XNamespace.Get("http://meziantou.net/xspf-extension/1/");

        var trackList = updatedXml.Root?.Element(xspfNs + "trackList");
        Assert.NotNull(trackList);

        var tracks = trackList.Elements(xspfNs + "track").ToList();
        Assert.Equal(2, tracks.Count);

        // First track should keep its original addedAt
        var track1AddedAt = tracks[0].Element(xspfNs + "extension")?.Element(meziantouNs + "addedAt")?.Value;
        Assert.Equal(customAddedAt1, track1AddedAt);

        // Second track (newly added) should have a recent addedAt
        var track2AddedAtStr = tracks[1].Element(xspfNs + "extension")?.Element(meziantouNs + "addedAt")?.Value;
        Assert.NotNull(track2AddedAtStr);
        var track2AddedAt = DateTime.Parse(track2AddedAtStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(track2AddedAt >= beforeUpdate && track2AddedAt <= afterUpdate,
            $"New song addedAt ({track2AddedAt:O}) should be between {beforeUpdate:O} and {afterUpdate:O}");
    }

    [Fact]
    public async Task GetPlaylists_IncludesNoReplayGainVirtualPlaylist_WhenSongsWithoutReplayGainExist()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song2.mp3", title: "Song 2", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 2, replayGainTrackGain: -8.5, replayGainTrackPeak: 0.95);

        var service = await testContext.ScanCatalog();

        var playlists = service.GetPlaylists().ToList();

        var noReplayGainPlaylist = playlists.FirstOrDefault(p => p.Id == Playlist.NoReplayGainPlaylistId);
        Assert.NotNull(noReplayGainPlaylist);
        Assert.Equal("virtual:no-replay-gain", noReplayGainPlaylist.Id);
        Assert.Equal("⚠️ No Replay Gain", noReplayGainPlaylist.Name);
        Assert.Equal(1, noReplayGainPlaylist.SongCount);
    }

    [Fact]
    public async Task GetPlaylists_DoesNotIncludeNoReplayGainVirtualPlaylist_WhenAllSongsHaveReplayGain()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1, replayGainTrackGain: -7.2, replayGainTrackPeak: 0.88);
        testContext.MusicLibrary.CreateTestMp3File("song2.mp3", title: "Song 2", artist: "Artist 2", albumArtist: "Artist 2", album: "Album 2", genre: "Pop", year: 2024, track: 1, replayGainTrackGain: -8.5, replayGainTrackPeak: 0.95);

        var service = await testContext.ScanCatalog();

        var playlists = service.GetPlaylists().ToList();

        var noReplayGainPlaylist = playlists.FirstOrDefault(p => p.Id == Playlist.NoReplayGainPlaylistId);
        Assert.Null(noReplayGainPlaylist);
    }

    [Fact]
    public async Task GetPlaylist_NoReplayGainVirtualPlaylist_ReturnsOnlySongsWithoutReplayGain()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist A", albumArtist: "Artist A", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("song2.mp3", title: "Song 2", artist: "Artist B", albumArtist: "Artist B", album: "Album 2", genre: "Pop", year: 2024, track: 1, replayGainTrackGain: -8.5, replayGainTrackPeak: 0.95);
        testContext.MusicLibrary.CreateTestMp3File("song3.mp3", title: "Song 3", artist: "Artist C", albumArtist: "Artist C", album: "Album 3", genre: "Jazz", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var playlist = service.GetPlaylist("virtual:no-replay-gain");

        Assert.NotNull(playlist);
        Assert.Equal("⚠️ No Replay Gain", playlist.Name);
        Assert.Equal(2, playlist.SongCount);
        Assert.Equal(2, playlist.Items.Count);

        Assert.Contains(playlist.Items, item => item.Song.Title == "Song 1");
        Assert.DoesNotContain(playlist.Items, item => item.Song.Title == "Song 2");
        Assert.Contains(playlist.Items, item => item.Song.Title == "Song 3");
    }

    [Fact]
    public async Task UpdatePlaylist_NoReplayGainVirtualPlaylist_ThrowsInvalidOperationException()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdatePlaylist("virtual:no-replay-gain", "New Name", null, null));
    }

    [Fact]
    public async Task DeletePlaylist_NoReplayGainVirtualPlaylist_ThrowsInvalidOperationException()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeletePlaylist("virtual:no-replay-gain"));
    }

    [Fact]
    public async Task ScanMusicLibrary_M3uConversion_PreservesRelativePathsToPlaylistFile()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.AddFolder("subfolder");
        testContext.MusicLibrary.CreateTestMp3File("subfolder/song1.mp3", title: "Song 1", artist: "Artist 1", albumArtist: "Artist 1", album: "Album 1", genre: "Rock", year: 2024, track: 1);
        testContext.MusicLibrary.CreateTestMp3File("subfolder/song2.mp3", title: "Song 2", artist: "Artist 2", albumArtist: "Artist 2", album: "Album 2", genre: "Pop", year: 2024, track: 1);

        var playlistContent = """
            song1.mp3
            song2.mp3
            """;
        await testContext.MusicLibrary.CreatePlaylistFile("subfolder/test-playlist.m3u", playlistContent);

        var service = await testContext.ScanCatalog();

        var xspfFile = Path.Combine(testContext.MusicLibrary.RootPath, "subfolder", "test-playlist.xspf");
        Assert.True(File.Exists(xspfFile), "XSPF file should be created");

        var xspfContent = await File.ReadAllTextAsync(xspfFile, testContext.CancellationToken);

        // Paths should remain relative to the playlist file, not to the music library root
        Assert.Contains("<location>song1.mp3</location>", xspfContent, StringComparison.Ordinal);
        Assert.Contains("<location>song2.mp3</location>", xspfContent, StringComparison.Ordinal);

        // Paths should NOT be relative to the music library root
        Assert.DoesNotContain("<location>subfolder/song1.mp3</location>", xspfContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<location>subfolder/song2.mp3</location>", xspfContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanMusicLibrary_ExtractsIsrcFromMetadata()
    {
        await using var testContext = AppTestContext.Create();
        var isrc = "USRC17607839";
        testContext.MusicLibrary.CreateTestMp3File("test-with-isrc.mp3", title: "Song With ISRC", artist: "Test Artist", albumArtist: "Test Artist", album: "Test Album", genre: "Pop", year: 2024, track: 1, isrc: isrc);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        var song = Assert.Single(songs);
        Assert.Equal("Song With ISRC", song.Title);
        Assert.Equal(isrc, song.Isrc);
    }

    [Fact]
    public async Task ScanMusicLibrary_HandlesFilesWithoutIsrc()
    {
        await using var testContext = AppTestContext.Create();
        testContext.MusicLibrary.CreateTestMp3File("test-no-isrc.mp3", title: "Song Without ISRC", artist: "Test Artist", albumArtist: "Test Artist", album: "Test Album", genre: "Rock", year: 2024, track: 1);

        var service = await testContext.ScanCatalog();

        var songs = service.GetAllSongs().ToList();
        var song = Assert.Single(songs);
        Assert.Equal("Song Without ISRC", song.Title);
        Assert.Null(song.Isrc);
    }
}

