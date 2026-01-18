using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Meziantou.Framework;
using Meziantou.MusicApp.Server.Telemetry;

namespace Meziantou.MusicApp.Server.Models;

public sealed class MusicCatalog
{
    private readonly Dictionary<string, Song> _songsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Album> _albumsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Artist> _artistsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MusicDirectory> _directoriesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _genreIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CoverArt> _coverArtsById = new(StringComparer.Ordinal);

    public FullPath RootPath { get; }

    public ImmutableList<Song> Songs { get; private set; } = [];
    public ImmutableDictionary<string, Playlist> Playlists { get; private set; } = [];
    public ImmutableList<Artist> Artists { get; private set; } = [];
    public ImmutableList<Album> Albums { get; private set; } = [];
    public ImmutableList<MusicDirectory> Directories { get; private set; } = [];
    public ImmutableList<MissingPlaylistItem> MissingPlaylistItems { get; private set; } = [];
    public ImmutableList<InvalidPlaylist> InvalidPlaylists { get; private set; } = [];
    public DateTime? LastScanDate { get; private set; }

    internal MusicCatalog(FullPath rootPath)
    {
        RootPath = rootPath;
    }

    internal static async Task<MusicCatalog> Create(SerializableMusicCatalog serializableCatalog, FullPath rootPath, FullPath coverArtCachePath)
    {
        using var activity = MusicLibraryActivitySource.Instance.StartActivity("MusicCatalog.Create");
        activity?.SetTag("music.catalog.total_songs", serializableCatalog.Songs.Count);
        activity?.SetTag("music.catalog.total_playlists", serializableCatalog.Playlist.Count);
        activity?.SetTag("music.catalog.root_path", rootPath.Value);

        var result = new MusicCatalog(rootPath);
        var songsBuilder = ImmutableList.CreateBuilder<Song>();

        // Create songs
        using (var songsActivity = MusicLibraryActivitySource.Instance.StartActivity("CreateSongs"))
        {
            songsActivity?.SetTag("music.catalog.song_count", serializableCatalog.Songs.Count);

            foreach (var serializableSong in serializableCatalog.Songs)
            {
                var fullPath = Path.Combine(rootPath, serializableSong.RelativePath);
                var songId = ItemId.CreateSongId(serializableSong.RelativePath, serializableSong.FileLastWriteTime);

                // Create Lyrics object if available
                // External LRC files take precedence over embedded lyrics
                Lyrics? lyrics = null;
                if (!string.IsNullOrEmpty(serializableSong.ExternalLyricsPath))
                {
                    lyrics = new Lyrics
                    {
                        Id = ItemId.CreateLyricsId(serializableSong.ExternalLyricsPath),
                        FilePath = Path.Combine(rootPath, serializableSong.ExternalLyricsPath),
                        IsMetadata = false,
                    };
                }
                else if (!string.IsNullOrEmpty(serializableSong.Lyrics))
                {
                    lyrics = new Lyrics
                    {
                        Id = ItemId.CreateLyricsId(serializableSong.RelativePath),
                        FilePath = fullPath,
                        IsMetadata = true,
                    };
                }

                // Create CoverArt object if available
                CoverArt? coverArt = null;
                var sourceLastWriteTimeUtc = serializableSong.FileLastWriteTime;

                if (serializableSong.HasEmbeddedCover)
                {
                    var coverId = ItemId.CreateCoverId(serializableSong.RelativePath);
                    coverArt = new CoverArt
                    {
                        Id = coverId,
                        FilePath = fullPath,
                        IsMetadata = true,
                        SourceLastWriteTimeUtc = sourceLastWriteTimeUtc,
                        CachedFilePath = GetCachedCoverArtPath(coverArtCachePath, coverId),
                    };
                }
                else if (!string.IsNullOrEmpty(serializableSong.ExternalCoverArtPath))
                {
                    var externalCoverPath = Path.Combine(rootPath, serializableSong.ExternalCoverArtPath);
                    var coverId = ItemId.CreateCoverId(serializableSong.ExternalCoverArtPath);
                    var externalLastWriteTimeUtc = File.Exists(externalCoverPath) ? File.GetLastWriteTimeUtc(externalCoverPath) : DateTime.MinValue;

                    coverArt = new CoverArt
                    {
                        Id = coverId,
                        FilePath = externalCoverPath,
                        IsMetadata = false,
                        SourceLastWriteTimeUtc = externalLastWriteTimeUtc,
                        CachedFilePath = GetCachedCoverArtPath(coverArtCachePath, coverId),
                    };
                }

                // Cache cover art if configured and needed
                if (coverArt is not null && !string.IsNullOrEmpty(coverArt.CachedFilePath))
                {
                    await EnsureCoverArtCached(coverArt);
                }

                var song = new Song
                {
                    Id = songId,
                    Title = serializableSong.Title ?? Path.GetFileNameWithoutExtension(serializableSong.RelativePath),
                    Path = fullPath,
                    Album = serializableSong.AlbumName ?? string.Empty,
                    Artist = serializableSong.Artist ?? string.Empty,
                    AlbumArtist = serializableSong.AlbumArtist ?? serializableSong.Artist ?? string.Empty,
                    Genre = serializableSong.Genre ?? string.Empty,
                    Year = serializableSong.Year == 0 ? null : serializableSong.Year,
                    Track = serializableSong.Track == 0 ? null : serializableSong.Track,
                    Duration = (int)serializableSong.Duration.TotalSeconds,
                    BitRate = serializableSong.BitRate == 0 ? null : serializableSong.BitRate,
                    Size = serializableSong.FileSize,
                    Created = serializableSong.FileCreatedAt,
                    Suffix = Path.GetExtension(serializableSong.RelativePath).TrimStart('.').ToLowerInvariant(),
                    ContentType = GetContentType(Path.GetExtension(serializableSong.RelativePath)),
                    Lyrics = lyrics,
                    CoverArt = coverArt,
                    Isrc = serializableSong.Isrc,
                    ReplayGainTrackGain = serializableSong.ReplayGainTrackGain,
                    ReplayGainTrackPeak = serializableSong.ReplayGainTrackPeak,
                    ReplayGainAlbumGain = serializableSong.ReplayGainAlbumGain,
                    ReplayGainAlbumPeak = serializableSong.ReplayGainAlbumPeak,
                };

                songsBuilder.Add(song);
                result._songsById[song.Id] = song;

                // Add cover art to cache
                if (coverArt is not null)
                {
                    result._coverArtsById[coverArt.Id] = coverArt;
                }
            }
        }

        result.Songs = songsBuilder.ToImmutable();

        // Build albums and artists
        await result.BuildAlbumsAndArtists();

        // Build playlists
        await result.BuildPlaylists(serializableCatalog.Playlist);

        // Build missing playlist items
        result.BuildMissingPlaylistItems(serializableCatalog.MissingPlaylistItems);

        // Build invalid playlists
        result.BuildInvalidPlaylists(serializableCatalog.InvalidPlaylists);

        // Build genre index
        result.BuildGenreIndex();

        // Build directory structure
        result.BuildDirectoryStructure();

        result.LastScanDate = DateTime.UtcNow;

        activity?.SetTag("music.catalog.final_songs", result.Songs.Count);
        activity?.SetTag("music.catalog.final_artists", result.Artists.Count);
        activity?.SetTag("music.catalog.final_albums", result.Albums.Count);
        activity?.SetTag("music.catalog.final_playlists", result.Playlists.Count);

        return result;
    }

    private async Task BuildAlbumsAndArtists()
    {
        using var activity = MusicLibraryActivitySource.Instance.StartActivity("BuildAlbumsAndArtists");

        var albumDict = new Dictionary<string, List<Song>>(StringComparer.OrdinalIgnoreCase);
        var artistDict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var song in Songs)
        {
            var artistKey = string.IsNullOrWhiteSpace(song.AlbumArtist) ? "Unknown Artist" : song.AlbumArtist.Trim();
            var albumName = string.IsNullOrWhiteSpace(song.Album) ? "Unknown Album" : song.Album.Trim();
            var albumKey = $"{artistKey}|{albumName}";

            if (!albumDict.TryGetValue(albumKey, out var value))
            {
                value = [];
                albumDict[albumKey] = value;
            }

            value.Add(song);

            if (!artistDict.ContainsKey(artistKey))
            {
                artistDict[artistKey] = [];
            }
        }

        var albumsBuilder = ImmutableList.CreateBuilder<Album>();
        var artistsBuilder = ImmutableList.CreateBuilder<Artist>();

        foreach (var (albumKey, songs) in albumDict)
        {
            var parts = albumKey.Split('|', 2);
            if (parts.Length != 2)
                continue;

            var artistName = parts[0];
            var albumName = parts[1];
            var artistId = ItemId.CreateArtistId(artistName);
            var albumId = ItemId.CreateAlbumId(albumKey);

            var firstSong = songs[0];

            // Use the cover art from the first song that has one
            var albumCoverArt = songs.Select(s => s.CoverArt).FirstOrDefault(c => c is not null);

            var album = new Album
            {
                Id = albumId,
                Name = albumName,
                Artist = artistName,
                ArtistId = artistId,
                Year = firstSong.Year,
                Genre = firstSong.Genre,
                Songs = [.. songs.OrderBy(s => s.Track ?? 0)],
                SongCount = songs.Count,
                Duration = songs.Sum(s => s.Duration),
                Created = songs.Min(s => s.Created),
                CoverArt = albumCoverArt,
            };

            // Update song references
            foreach (var song in songs)
            {
                song.AlbumId = albumId;
                song.ArtistId = artistId;
            }

            albumsBuilder.Add(album);
            _albumsById[albumId] = album;

            // Use artistName (which is already normalized from albumKey split) to ensure consistency
            if (!artistDict.TryGetValue(artistName, out var albumIdsSet))
            {
                albumIdsSet = [];
                artistDict[artistName] = albumIdsSet;
            }
            albumIdsSet.Add(albumId);
        }

        Albums = albumsBuilder.ToImmutable();

        foreach (var (artistName, albumIds) in artistDict)
        {
            var artistId = ItemId.CreateArtistId(artistName);
            var artistAlbums = albumIds.Select(id => _albumsById[id]).ToList();

            var artist = new Artist
            {
                Id = artistId,
                Name = artistName,
                Albums = artistAlbums,
            };
            artist.AlbumCount = artistAlbums.Count;
            artist.CoverArt = artistAlbums.FirstOrDefault()?.CoverArt;

            artistsBuilder.Add(artist);
            _artistsById[artistId] = artist;
        }

        Artists = artistsBuilder.ToImmutable();

        activity?.SetTag("music.catalog.albums_built", Albums.Count);
        activity?.SetTag("music.catalog.artists_built", Artists.Count);

        await Task.CompletedTask;
    }

    private async Task BuildPlaylists(List<SerializablePlaylist> serializablePlaylists)
    {
        using var activity = MusicLibraryActivitySource.Instance.StartActivity("BuildPlaylists");
        activity?.SetTag("music.catalog.playlist_count", serializablePlaylists.Count);

        var playlistsBuilder = ImmutableDictionary.CreateBuilder<string, Playlist>(StringComparer.OrdinalIgnoreCase);

        foreach (var serializablePlaylist in serializablePlaylists)
        {
            var fullPath = Path.Combine(RootPath, serializablePlaylist.RelativePath);
            var fileInfo = new FileInfo(fullPath);

            var playlist = new Playlist
            {
                Id = ItemId.CreatePlaylistId(serializablePlaylist.RelativePath),
                Name = serializablePlaylist.Name,
                Path = fullPath,
                Created = fileInfo.Exists ? fileInfo.CreationTimeUtc : DateTime.UtcNow,
                Changed = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.UtcNow,
                Comment = serializablePlaylist.Comment,
            };

            var items = new List<PlaylistItem>();
            foreach (var item in serializablePlaylist.Items)
            {
                var songId = ItemId.CreateSongId(item.RelativePath, item.FileLastWriteTime);
                if (_songsById.TryGetValue(songId, out var song))
                {
                    items.Add(new PlaylistItem
                    {
                        Song = song,
                        AddedDate = item.AddedDate ?? DateTime.UtcNow,
                    });
                }
            }

            playlist.Items = items;
            playlist.SongCount = items.Count;
            playlist.Duration = items.Sum(i => i.Song.Duration);

            // Set cover art from first song if available
            if (items.Count > 0)
            {
                playlist.CoverArt = items[0].Song.CoverArt;
            }

            playlistsBuilder.Add(playlist.Id, playlist);
        }

        Playlists = playlistsBuilder.ToImmutable();
        activity?.SetTag("music.catalog.playlists_built", Playlists.Count);
        await Task.CompletedTask;
    }

    private void BuildMissingPlaylistItems(List<SerializableMissingPlaylistItem> serializableMissingItems)
    {
        var missingItemsBuilder = ImmutableList.CreateBuilder<MissingPlaylistItem>();

        foreach (var item in serializableMissingItems)
        {
            var fullPath = Path.Combine(RootPath, item.RelativePath);
            var playlistId = ItemId.CreatePlaylistId(item.PlaylistRelativePath);

            missingItemsBuilder.Add(new MissingPlaylistItem
            {
                RelativePath = item.RelativePath,
                FullPath = fullPath,
                PlaylistName = item.PlaylistName,
                PlaylistId = playlistId,
                AddedDate = item.AddedDate,
            });
        }

        MissingPlaylistItems = missingItemsBuilder.ToImmutable();
    }

    private void BuildInvalidPlaylists(List<SerializableInvalidPlaylist> serializableInvalidPlaylists)
    {
        var invalidPlaylistsBuilder = ImmutableList.CreateBuilder<InvalidPlaylist>();

        foreach (var item in serializableInvalidPlaylists)
        {
            var fullPath = Path.Combine(RootPath, item.RelativePath);

            invalidPlaylistsBuilder.Add(new InvalidPlaylist
            {
                Path = fullPath,
                ErrorMessage = item.ErrorMessage,
            });
        }

        InvalidPlaylists = invalidPlaylistsBuilder.ToImmutable();
    }

    private void BuildGenreIndex()
    {
        foreach (var song in Songs)
        {
            if (!string.IsNullOrEmpty(song.Genre))
            {
                if (!_genreIndex.TryGetValue(song.Genre, out var value))
                {
                    value = [];
                    _genreIndex[song.Genre] = value;
                }

                value.Add(song.Id);
            }
        }
    }

    private void BuildDirectoryStructure()
    {
        using var activity = MusicLibraryActivitySource.Instance.StartActivity("BuildDirectoryStructure");

        var directoriesBuilder = ImmutableList.CreateBuilder<MusicDirectory>();
        var directoryDict = new Dictionary<string, MusicDirectory>(StringComparer.Ordinal);

        // Create root directory
        var rootDir = new MusicDirectory
        {
            Id = ItemId.CreateDirectoryId(RootPath),
            Name = Path.GetFileName(RootPath) ?? "Music",
            Path = RootPath,
        };
        directoryDict[RootPath] = rootDir;

        // Group songs by directory
        foreach (var song in Songs)
        {
            var dirPath = Path.GetDirectoryName(song.Path);
            if (string.IsNullOrEmpty(dirPath))
                continue;

            if (!directoryDict.TryGetValue(dirPath, out var dir))
            {
                var parentPath = Path.GetDirectoryName(dirPath);
                dir = new MusicDirectory
                {
                    Id = ItemId.CreateDirectoryId(dirPath),
                    Name = Path.GetFileName(dirPath) ?? dirPath,
                    Path = dirPath,
                };

                if (!string.IsNullOrEmpty(parentPath))
                {
                    dir.ParentId = ItemId.CreateDirectoryId(parentPath);
                }

                directoryDict[dirPath] = dir;
            }

            dir.Files.Add(song);
        }

        // Build subdirectory relationships
        foreach (var dir in directoryDict.Values.Where(d => d.Path != RootPath))
        {
            var parentPath = Path.GetDirectoryName(dir.Path);
            if (!string.IsNullOrEmpty(parentPath) && directoryDict.TryGetValue(parentPath, out var parentDir))
            {
                parentDir.SubDirectories.Add(dir);
            }
        }

        directoriesBuilder.AddRange(directoryDict.Values);
        Directories = directoriesBuilder.ToImmutable();

        foreach (var dir in Directories)
        {
            _directoriesById[dir.Id] = dir;
        }

        activity?.SetTag("music.catalog.directories_built", Directories.Count);
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".opus" => "audio/opus",
            ".wav" => "audio/wav",
            ".aac" => "audio/aac",
            ".wma" => "audio/x-ms-wma",
            _ => "application/octet-stream",
        };
    }

    // Query methods
    public Song? GetSong(string id) => _songsById.TryGetValue(id, out var song) ? song : null;
    public Album? GetAlbum(string id) => _albumsById.TryGetValue(id, out var album) ? album : null;
    public Artist? GetArtist(string id) => _artistsById.TryGetValue(id, out var artist) ? artist : null;
    public Playlist? GetPlaylist(string id) => Playlists.TryGetValue(id, out var value) ? value : null;
    public MusicDirectory? GetDirectory(string id) => _directoriesById.TryGetValue(id, out var dir) ? dir : null;

    // Playlist mutation methods
    public void AddOrUpdatePlaylist(Playlist playlist)
    {
        Playlists = Playlists.SetItem(playlist.Id, playlist);
    }

    /// <summary>Creates a Playlist object from a SerializablePlaylist and adds/updates it in the catalog.</summary>
    internal Playlist AddOrUpdatePlaylist(SerializablePlaylist serializablePlaylist)
    {
        var fullPath = Path.Combine(RootPath, serializablePlaylist.RelativePath);
        var fileInfo = new FileInfo(fullPath);

        var playlist = new Playlist
        {
            Id = ItemId.CreatePlaylistId(serializablePlaylist.RelativePath),
            Name = serializablePlaylist.Name,
            Path = fullPath,
            Created = fileInfo.Exists ? fileInfo.CreationTimeUtc : DateTime.UtcNow,
            Changed = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.UtcNow,
            Comment = serializablePlaylist.Comment,
        };

        var items = new List<PlaylistItem>();
        foreach (var item in serializablePlaylist.Items)
        {
            var songId = ItemId.CreateSongId(item.RelativePath, item.FileLastWriteTime);
            if (_songsById.TryGetValue(songId, out var song))
            {
                items.Add(new PlaylistItem
                {
                    Song = song,
                    AddedDate = item.AddedDate ?? DateTime.UtcNow,
                });
            }
        }

        playlist.Items = items;
        playlist.SongCount = items.Count;
        playlist.Duration = items.Sum(i => i.Song.Duration);

        // Set cover art from first song if available
        if (items.Count > 0)
        {
            playlist.CoverArt = items[0].Song.CoverArt;
        }

        AddOrUpdatePlaylist(playlist);
        return playlist;
    }

    public void RemovePlaylist(string playlistId) => Playlists = Playlists.Remove(playlistId);

    public IEnumerable<string> GetGenres() => _genreIndex.Keys.Order(StringComparer.Ordinal);
    public IEnumerable<Song> GetSongsByGenre(string genre) =>
        _genreIndex.TryGetValue(genre, out var ids) ? ids.Select(id => _songsById[id]) : [];

    public IEnumerable<Album> GetRandomAlbums(int count) => Albums.OrderBy(_ => Random.Shared.Next()).Take(count);
    public IEnumerable<Album> GetNewestAlbums(int count) => Albums.OrderByDescending(a => a.Created).Take(count);
    public IEnumerable<Song> GetRandomSongs(int count) => Songs.OrderBy(_ => Random.Shared.Next()).Take(count);

    public (IEnumerable<Artist> artists, IEnumerable<Album> albums, IEnumerable<Song> songs) SearchAll(string query)
    {
        var artists = Artists.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        var albums = Albums.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                       a.Artist.Contains(query, StringComparison.OrdinalIgnoreCase));
        var songs = Songs.Where(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                      s.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                      s.Album.Contains(query, StringComparison.OrdinalIgnoreCase));

        return (artists, albums, songs);
    }

    public CoverArtData? GetCoverArt(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        using var activity = MusicLibraryActivitySource.Instance.StartActivity("GetCoverArt");
        activity?.SetTag("music.coverart.id", id);

        // First, try to find cover art by ID directly using the cache
        if (!_coverArtsById.TryGetValue(id, out var coverArt))
        {
            // If not found by cover art ID, try to find by song or album ID
            var song = GetSong(id);
            if (song is not null)
            {
                coverArt = song.CoverArt;
            }
            else
            {
                var album = GetAlbum(id);
                coverArt = album?.CoverArt;
            }
        }

        if (coverArt is null)
        {
            activity?.SetTag("music.coverart.result", "not_found");
            return null;
        }

        activity?.SetTag("music.coverart.is_metadata", coverArt.IsMetadata);
        activity?.SetTag("music.coverart.has_cache", !string.IsNullOrEmpty(coverArt.CachedFilePath));

        // Try to read from cached file first
        if (!string.IsNullOrEmpty(coverArt.CachedFilePath) && File.Exists(coverArt.CachedFilePath))
        {
            try
            {
                var cachedLastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(coverArt.CachedFilePath), TimeSpan.Zero);
                var data = File.ReadAllBytes(coverArt.CachedFilePath);
                activity?.SetTag("music.coverart.result", "from_cache");
                activity?.SetTag("music.coverart.size_bytes", data.Length);
                return new CoverArtData
                {
                    Data = data,
                    LastModified = cachedLastModified,
                };
            }
            catch
            {
                activity?.SetTag("music.coverart.cache_read_failed", true);
                // Fall through to read from source
            }
        }

        // Read the cover art from source file
        if (!File.Exists(coverArt.FilePath))
        {
            activity?.SetTag("music.coverart.result", "source_not_found");
            return null;
        }

        try
        {
            var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(coverArt.FilePath), TimeSpan.Zero);

            if (coverArt.IsMetadata)
            {
                // Read from embedded metadata
                using var tagFile = TagLib.File.Create(coverArt.FilePath);
                var pictures = tagFile.Tag.Pictures;
                if (pictures.Length > 0)
                {
                    activity?.SetTag("music.coverart.result", "from_metadata");
                    activity?.SetTag("music.coverart.size_bytes", pictures[0].Data.Data.Length);
                    return new CoverArtData
                    {
                        Data = pictures[0].Data.Data,
                        LastModified = lastModified,
                    };
                }
            }
            else
            {
                // Read from external file
                var data = File.ReadAllBytes(coverArt.FilePath);
                activity?.SetTag("music.coverart.result", "from_external_file");
                activity?.SetTag("music.coverart.size_bytes", data.Length);
                return new CoverArtData
                {
                    Data = data,
                    LastModified = lastModified,
                };
            }
        }
        catch
        {
            activity?.SetTag("music.coverart.result", "read_error");
            // Ignore errors reading cover art
        }

        activity?.SetTag("music.coverart.result", "failed");
        return null;
    }

    public string? GetLyrics(string songId)
    {
        var song = GetSong(songId);
        if (song?.Lyrics is null)
            return null;

        using var activity = MusicLibraryActivitySource.Instance.StartActivity("GetLyrics");
        activity?.SetTag("music.lyrics.song_id", songId);
        activity?.SetTag("music.lyrics.is_metadata", song.Lyrics.IsMetadata);
        try
        {
            if (song.Lyrics.IsMetadata)
            {
                // Read from embedded metadata
                using var tagFile = TagLib.File.Create(song.Lyrics.FilePath);
                var lyrics = tagFile.Tag.Lyrics;
                activity?.SetTag("music.lyrics.result", "from_metadata");
                if (lyrics is not null)
                {
                    activity?.SetTag("music.lyrics.length", lyrics.Length);
                }

                return lyrics;
            }
            else
            {
                // Read from external file
                var lyricsText = File.ReadAllText(song.Lyrics.FilePath);

                // If it's an LRC file, parse it
                if (song.Lyrics.FilePath.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = ParseLrcFile(lyricsText);
                    activity?.SetTag("music.lyrics.result", "from_lrc_file");
                    activity?.SetTag("music.lyrics.length", parsed.Length);
                    return parsed;
                }

                activity?.SetTag("music.lyrics.result", "from_text_file");
                activity?.SetTag("music.lyrics.length", lyricsText.Length);
                return lyricsText;
            }
        }
        catch
        {
            activity?.SetTag("music.lyrics.result", "read_error");
            // Ignore errors reading lyrics
        }

        activity?.SetTag("music.lyrics.result", "failed");
        return null;
    }

    private static string ParseLrcFile(string lrcContent)
    {
        var lines = lrcContent.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var lyricsLines = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith('[') && line.Contains(':', StringComparison.Ordinal))
            {
                var closingBracket = line.IndexOf(']', StringComparison.Ordinal);
                if (closingBracket > 0)
                {
                    var tag = line[1..closingBracket];
                    if (!tag.Contains('.', StringComparison.Ordinal) && tag.Split(':').Length == 2)
                    {
                        var parts = tag.Split(':');
                        if (parts.Length == 2 && !int.TryParse(parts[0], CultureInfo.InvariantCulture, out _))
                        {
                            continue;
                        }
                    }

                    var lyricsText = line[(closingBracket + 1)..].Trim();
                    if (!string.IsNullOrEmpty(lyricsText))
                    {
                        lyricsLines.Add(lyricsText);
                    }
                }
            }
            else
            {
                lyricsLines.Add(line.Trim());
            }
        }

        return string.Join(Environment.NewLine, lyricsLines);
    }

    private static FullPath GetCachedCoverArtPath(FullPath coverArtCachePath, string coverId)
    {
        if (coverArtCachePath.IsEmpty)
            return FullPath.Empty;

        return coverArtCachePath / coverId;
    }

    private static async Task EnsureCoverArtCached(CoverArt coverArt)
    {
        if (coverArt.CachedFilePath.IsEmpty)
            return;

        using var activity = MusicLibraryActivitySource.Instance.StartActivity("EnsureCoverArtCached");
        activity?.SetTag("music.coverart.id", coverArt.Id);
        // Check if cached file exists and is up to date
        if (File.Exists(coverArt.CachedFilePath))
        {
            var cachedLastWriteTime = File.GetLastWriteTimeUtc(coverArt.CachedFilePath);
            if (cachedLastWriteTime >= coverArt.SourceLastWriteTimeUtc)
            {
                activity?.SetTag("music.coverart.cache_result", "up_to_date");
                // Cache is up to date
                return;
            }
        }

        // Need to refresh the cache
        try
        {
            byte[]? imageData = null;

            if (coverArt.IsMetadata)
            {
                // Extract from embedded metadata
                using var tagFile = TagLib.File.Create(coverArt.FilePath);
                var pictures = tagFile.Tag.Pictures;
                if (pictures.Length > 0)
                {
                    imageData = pictures[0].Data.Data;
                }
            }
            else
            {
                // Read from external file
                if (File.Exists(coverArt.FilePath))
                {
                    imageData = await File.ReadAllBytesAsync(coverArt.FilePath);
                }
            }

            if (imageData is not null)
            {
                await File.WriteAllBytesAsync(coverArt.CachedFilePath, imageData);
                // Update the cached file's last write time to match the source
                File.SetLastWriteTimeUtc(coverArt.CachedFilePath, coverArt.SourceLastWriteTimeUtc);
                activity?.SetTag("music.coverart.cache_result", "cached");
                activity?.SetTag("music.coverart.size_bytes", imageData.Length);
            }
        }
        catch
        {
            activity?.SetTag("music.coverart.cache_result", "error");
            // Ignore errors caching cover art
        }
    }
}
