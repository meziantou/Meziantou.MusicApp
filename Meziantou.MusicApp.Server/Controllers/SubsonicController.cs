using System.Xml.Linq;
using Meziantou.MusicApp.Server.Models;
using Meziantou.MusicApp.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Meziantou.MusicApp.Server.Controllers;

[ApiController]
[Route("rest")]
[ApiExplorerSettings(IgnoreApi = true)]
public class SubsonicController : ControllerBase
{
    private const string SubsonicServerVersion = "1.16.1";
    private readonly MusicLibraryService _libraryService;
    private readonly MusicServerSettings _commonSettings;
    private readonly TranscodingService _transcodingService;
    private readonly ImageResizingService _imageResizingService;
    private readonly LastFmService _lastFmService;
    private readonly ILogger<SubsonicController> _logger;
    private static readonly XNamespace SubsonicNamespace = "http://subsonic.org/restapi";

    public SubsonicController(
        MusicLibraryService libraryService,
        IOptions<MusicServerSettings> commonSettings,
        TranscodingService transcodingService,
        ImageResizingService imageResizingService,
        LastFmService lastFmService,
        ILogger<SubsonicController> logger)
    {
        _libraryService = libraryService;
        _commonSettings = commonSettings.Value;
        _transcodingService = transcodingService;
        _imageResizingService = imageResizingService;
        _lastFmService = lastFmService;
        _logger = logger;
    }

    [HttpGet("ping")]
    [HttpGet("ping.view")]
    public IActionResult Ping()
    {
        return Ok(CreateResponse());
    }

    [HttpGet("getLicense.view")]
    public IActionResult GetLicense()
    {
        var response = CreateResponse();
        response.Root!.Add(new XElement(SubsonicNamespace + "license",
            new XAttribute("valid", "true"),
            new XAttribute("email", "api@subsonic.local"),
            new XAttribute("licenseExpires", DateTime.UtcNow.AddYears(100).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))
        ));
        return Ok(response);
    }

    [HttpGet("getMusicFolders.view")]
    public IActionResult GetMusicFolders()
    {
        var response = CreateResponse();
        var musicFolders = new XElement(SubsonicNamespace + "musicFolders",
            new XElement(SubsonicNamespace + "musicFolder",
                new XAttribute("id", "1"),
                new XAttribute("name", Path.GetFileName(_commonSettings.MusicFolderPath) ?? "Music")
            )
        );
        response.Root!.Add(musicFolders);
        return Ok(response);
    }

    [HttpGet("getArtists.view")]
    public IActionResult GetArtists()
    {
        var response = CreateResponse();
        var artists = _libraryService.GetAllArtists();

        var artistsElement = new XElement(SubsonicNamespace + "artists",
            new XAttribute("ignoredArticles", "The El La Los Las Le Les")
        );

        var grouped = artists.GroupBy(a => char.IsLetter(a.Name.FirstOrDefault()) ? char.ToUpperInvariant(a.Name[0]).ToString() : "#", StringComparer.Ordinal);

        foreach (var group in grouped.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var indexElement = new XElement(SubsonicNamespace + "index", new XAttribute("name", group.Key));

            foreach (var artist in group.OrderBy(a => a.Name, StringComparer.Ordinal))
            {
                indexElement.Add(new XElement(SubsonicNamespace + "artist",
                    new XAttribute("id", artist.Id),
                    new XAttribute("name", artist.Name),
                    new XAttribute("albumCount", artist.AlbumCount)
                ));
            }

            artistsElement.Add(indexElement);
        }

        response.Root!.Add(artistsElement);
        return Ok(response);
    }

    [HttpGet("getArtist.view")]
    public IActionResult GetArtist([FromQuery] string id)
    {
        var artist = _libraryService.GetArtist(id);
        if (artist is null)
            return Ok(CreateError(70, "Artist not found"));

        var response = CreateResponse();
        var artistElement = new XElement(SubsonicNamespace + "artist",
            new XAttribute("id", artist.Id),
            new XAttribute("name", artist.Name),
            new XAttribute("albumCount", artist.AlbumCount)
        );

        foreach (var album in artist.Albums.OrderBy(a => a.Year ?? 0).ThenBy(a => a.Name, StringComparer.Ordinal))
        {
            artistElement.Add(CreateAlbumElement(album));
        }

        response.Root!.Add(artistElement);
        return Ok(response);
    }

    [HttpGet("getAlbum.view")]
    public IActionResult GetAlbum([FromQuery] string id)
    {
        var album = _libraryService.GetAlbum(id);
        if (album is null)
            return Ok(CreateError(70, "Album not found"));

        var response = CreateResponse();
        var albumElement = new XElement(SubsonicNamespace + "album",
            new XAttribute("id", album.Id),
            new XAttribute("name", album.Name),
            new XAttribute("artist", album.Artist),
            new XAttribute("artistId", album.ArtistId),
            new XAttribute("songCount", album.SongCount),
            new XAttribute("duration", album.Duration),
            new XAttribute("created", album.Created.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))
        );

        if (album.Year.HasValue)
            albumElement.Add(new XAttribute("year", album.Year.Value));
        if (!string.IsNullOrEmpty(album.Genre))
            albumElement.Add(new XAttribute("genre", album.Genre));
        if (album.CoverArt is not null)
            albumElement.Add(new XAttribute("coverArt", album.CoverArt.Id));

        foreach (var song in album.Songs)
        {
            albumElement.Add(CreateSongElement(song));
        }

        response.Root!.Add(albumElement);
        return Ok(response);
    }

    [HttpGet("getSong.view")]
    public IActionResult GetSong([FromQuery] string id)
    {
        var song = _libraryService.GetSong(id);
        if (song is null)
            return Ok(CreateError(70, "Song not found"));

        var response = CreateResponse();
        response.Root!.Add(CreateSongElement(song));
        return Ok(response);
    }

    [HttpGet("getAlbumList2.view")]
    public IActionResult GetAlbumList2(
        [FromQuery] string type = "random",
        [FromQuery] int size = 10,
        [FromQuery] int offset = 0)
    {
        var response = CreateResponse();
        var albums = type.ToLowerInvariant() switch
        {
            "random" => _libraryService.GetRandomAlbums(size),
            "newest" => _libraryService.GetNewestAlbums(size),
            _ => _libraryService.GetRandomAlbums(size),
        };

        var albumListElement = new XElement(SubsonicNamespace + "albumList2");
        foreach (var album in albums.Skip(offset).Take(size))
        {
            albumListElement.Add(CreateAlbumElement(album));
        }

        response.Root!.Add(albumListElement);
        return Ok(response);
    }

    [HttpGet("getRandomSongs.view")]
    public IActionResult GetRandomSongs([FromQuery] int size = 10)
    {
        var response = CreateResponse();
        var songs = _libraryService.GetRandomSongs(size);

        var randomSongsElement = new XElement(SubsonicNamespace + "randomSongs");
        foreach (var song in songs)
        {
            randomSongsElement.Add(CreateSongElement(song));
        }

        response.Root!.Add(randomSongsElement);
        return Ok(response);
    }

    [HttpGet("getGenres.view")]
    public IActionResult GetGenres()
    {
        var response = CreateResponse();
        var genres = _libraryService.GetGenres();

        var genresElement = new XElement(SubsonicNamespace + "genres");
        foreach (var genre in genres)
        {
            genresElement.Add(new XElement(SubsonicNamespace + "genre",
                new XAttribute("value", genre),
                new XAttribute("songCount", _libraryService.GetSongsByGenre(genre).Count()),
                new XAttribute("albumCount", 0) // Simplified
            ));
        }

        response.Root!.Add(genresElement);
        return Ok(response);
    }

    [HttpGet("getSongsByGenre.view")]
    public IActionResult GetSongsByGenre(
        [FromQuery] string genre,
        [FromQuery] int count = 10,
        [FromQuery] int offset = 0)
    {
        var response = CreateResponse();
        var songs = _libraryService.GetSongsByGenre(genre).Skip(offset).Take(count);

        var songsByGenreElement = new XElement(SubsonicNamespace + "songsByGenre");
        foreach (var song in songs)
        {
            songsByGenreElement.Add(CreateSongElement(song));
        }

        response.Root!.Add(songsByGenreElement);
        return Ok(response);
    }

    [HttpGet("search3.view")]
    public IActionResult Search3(
        [FromQuery] string query,
        [FromQuery] int artistCount = 20,
        [FromQuery] int artistOffset = 0,
        [FromQuery] int albumCount = 20,
        [FromQuery] int albumOffset = 0,
        [FromQuery] int songCount = 20,
        [FromQuery] int songOffset = 0)
    {
        var response = CreateResponse();
        var (artists, albums, songs) = _libraryService.SearchAll(query);

        var searchResultElement = new XElement(SubsonicNamespace + "searchResult3");

        foreach (var artist in artists.Skip(artistOffset).Take(artistCount))
        {
            searchResultElement.Add(new XElement(SubsonicNamespace + "artist",
                new XAttribute("id", artist.Id),
                new XAttribute("name", artist.Name),
                new XAttribute("albumCount", artist.AlbumCount)
            ));
        }

        foreach (var album in albums.Skip(albumOffset).Take(albumCount))
        {
            searchResultElement.Add(CreateAlbumElement(album));
        }

        foreach (var song in songs.Skip(songOffset).Take(songCount))
        {
            searchResultElement.Add(CreateSongElement(song));
        }

        response.Root!.Add(searchResultElement);
        return Ok(response);
    }

    [HttpGet("getPlaylists.view")]
    public IActionResult GetPlaylists()
    {
        var response = CreateResponse();
        var playlists = _libraryService.GetPlaylists();

        var playlistsElement = new XElement(SubsonicNamespace + "playlists");
        foreach (var playlist in playlists)
        {
            var playlistElement = new XElement(SubsonicNamespace + "playlist",
                new XAttribute("id", playlist.Id),
                new XAttribute("name", playlist.Name),
                new XAttribute("songCount", playlist.SongCount),
                new XAttribute("duration", playlist.Duration),
                new XAttribute("created", playlist.Created.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XAttribute("changed", playlist.Changed.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XAttribute("owner", "admin"),
                new XAttribute("public", "false")
            );

            if (playlist.CoverArt is not null)
                playlistElement.Add(new XAttribute("coverArt", playlist.CoverArt.Id));
            if (!string.IsNullOrEmpty(playlist.Comment))
                playlistElement.Add(new XAttribute("comment", playlist.Comment));

            playlistsElement.Add(playlistElement);
        }

        response.Root!.Add(playlistsElement);
        return Ok(response);
    }

    [HttpGet("getPlaylist.view")]
    public IActionResult GetPlaylist([FromQuery] string id)
    {
        var playlist = _libraryService.GetPlaylist(id);
        if (playlist is null)
            return Ok(CreateError(70, "Playlist not found"));

        var response = CreateResponse();
        var playlistElement = new XElement(SubsonicNamespace + "playlist",
            new XAttribute("id", playlist.Id),
            new XAttribute("name", playlist.Name),
            new XAttribute("songCount", playlist.SongCount),
            new XAttribute("duration", playlist.Duration),
            new XAttribute("created", playlist.Created.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            new XAttribute("changed", playlist.Changed.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            new XAttribute("owner", "admin"),
            new XAttribute("public", "false")
        );

        if (playlist.CoverArt is not null)
            playlistElement.Add(new XAttribute("coverArt", playlist.CoverArt.Id));
        if (!string.IsNullOrEmpty(playlist.Comment))
            playlistElement.Add(new XAttribute("comment", playlist.Comment));

        foreach (var item in playlist.Items)
        {
            playlistElement.Add(CreateSongElement(item.Song, "entry"));
        }

        response.Root!.Add(playlistElement);
        return Ok(response);
    }

    [HttpGet("stream.view")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public async Task<IActionResult> Stream(
        [FromQuery] string id,
        [FromQuery] string? format = null,
        [FromQuery] int? maxBitRate = null,
        [FromQuery] int? timeOffset = null,
        [FromQuery] bool? estimateContentLength = null)
    {
        var song = _libraryService.GetSong(id);
        if (song is null)
            return Ok(CreateError(70, "Song not found"));

        if (!System.IO.File.Exists(song.Path))
            return Ok(CreateError(70, "File not found on disk"));

        var needsTranscoding = !string.IsNullOrEmpty(format) ||
                              maxBitRate.HasValue ||
                              timeOffset.HasValue;

        if (!needsTranscoding)
        {
            var stream = new FileStream(song.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, song.ContentType, enableRangeProcessing: true);
        }

        try
        {
            var transcodedStream = await _transcodingService.TranscodeToStreamAsync(
                song.Path,
                format,
                maxBitRate,
                timeOffset,
                HttpContext.RequestAborted);

            var contentType = TranscodingService.GetContentType(format);

            if (estimateContentLength == true && maxBitRate.HasValue)
            {
                var estimatedSize = TranscodingService.EstimateSize(song.Duration, maxBitRate);
                if (estimatedSize.HasValue)
                {
                    Response.Headers.ContentLength = estimatedSize.Value;
                }
            }

            return File(transcodedStream, contentType, enableRangeProcessing: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcode song: {SongId}", id);
            return Ok(CreateError(0, "Transcoding failed"));
        }
    }

    [HttpGet("download.view")]
    public IActionResult Download([FromQuery] string id)
    {
        return Stream(id).Result;
    }

    [HttpGet("hls.m3u8")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities")]
    public IActionResult Hls(
        [FromQuery] string id,
        [FromQuery] int? bitRate = null,
        [FromQuery] string? audioCodec = null)
    {
        var song = _libraryService.GetSong(id);
        if (song is null)
            return Ok(CreateError(70, "Song not found"));

        if (!System.IO.File.Exists(song.Path))
            return Ok(CreateError(70, "File not found on disk"));

        var segmentDuration = 10;
        var codec = audioCodec ?? "mp3";
        var playlist = _transcodingService.GenerateHlsPlaylist(
            id,
            song.Duration,
            bitRate,
            codec,
            segmentDuration);

        return Content(playlist, "application/vnd.apple.mpegurl");
    }

    [HttpGet("hls/{id}/{segment}.{format}")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public async Task<IActionResult> HlsSegment(
        [FromRoute] string id,
        [FromRoute] int segment,
        [FromRoute] string format,
        [FromQuery] int? bitRate = null)
    {
        var song = _libraryService.GetSong(id);
        if (song is null)
            return Ok(CreateError(70, "Song not found"));

        if (!System.IO.File.Exists(song.Path))
            return Ok(CreateError(70, "File not found on disk"));

        try
        {
            var segmentDuration = 10;
            var segmentStream = await _transcodingService.TranscodeHlsSegmentAsync(
                song.Path,
                segment,
                segmentDuration,
                format,
                bitRate,
                HttpContext.RequestAborted);

            var contentType = TranscodingService.GetContentType(format);
            return File(segmentStream, contentType, enableRangeProcessing: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcode HLS segment {Segment} for song: {SongId}", segment, id);
            return Ok(CreateError(0, "HLS segment transcoding failed"));
        }
    }

    [HttpGet("getCoverArt.view")]
    public async Task<IActionResult> GetCoverArt([FromQuery] string id, [FromQuery] int? size)
    {
        var coverArtData = _libraryService.GetCoverArt(id);

        if (coverArtData is null)
            return Ok(CreateError(70, "Cover art not found"));

        // Check if client's cached version is still valid
        if (ImageCacheHelper.IsNotModified(Request, coverArtData.LastModified))
        {
            return StatusCode(304);
        }

        var coverArt = coverArtData.Data;
        if (size.HasValue && size.Value > 0)
        {
            coverArt = await _imageResizingService.ResizeImageAsync(coverArt, size, HttpContext.RequestAborted);
        }

        var contentType = "image/jpeg";
        if (coverArt.Length >= 8)
        {
            if (coverArt[0] == 0x89 && coverArt[1] == 0x50 && coverArt[2] == 0x4E && coverArt[3] == 0x47)
            {
                contentType = "image/png";
            }
            else if (coverArt[0] == 0xFF && coverArt[1] == 0xD8 && coverArt[2] == 0xFF)
            {
                contentType = "image/jpeg";
            }
        }

        ImageCacheHelper.SetImageCacheHeaders(Response, coverArtData.LastModified);

        return File(coverArt, contentType);
    }

    [HttpGet("getLyrics.view")]
    public IActionResult GetLyrics([FromQuery] string? artist, [FromQuery] string? title)
    {
        var response = CreateResponse();

        if (string.IsNullOrEmpty(artist) && string.IsNullOrEmpty(title))
        {
            response.Root!.Add(new XElement(SubsonicNamespace + "lyrics"));
            return Ok(response);
        }

        var songs = _libraryService.SearchAll(title ?? string.Empty).songs;

        Song? matchedSong = null;
        if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
        {
            matchedSong = songs.FirstOrDefault(s =>
                s.Artist.Equals(artist, StringComparison.OrdinalIgnoreCase) &&
                s.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

            if (matchedSong is null)
            {
                matchedSong = songs.FirstOrDefault(s =>
                    s.Artist.Contains(artist, StringComparison.OrdinalIgnoreCase) &&
                    s.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (!string.IsNullOrEmpty(title))
        {
            matchedSong = songs.FirstOrDefault(s =>
                s.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

            if (matchedSong is null)
            {
                matchedSong = songs.FirstOrDefault(s =>
                    s.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            }
        }

        var lyricsElement = new XElement(SubsonicNamespace + "lyrics");

        if (matchedSong is not null && matchedSong.Lyrics is not null)
        {
            lyricsElement.Add(new XAttribute("artist", matchedSong.Artist));
            lyricsElement.Add(new XAttribute("title", matchedSong.Title));
            var lyricsText = _libraryService.Catalog.GetLyrics(matchedSong.Id);
            if (lyricsText is not null)
            {
                lyricsElement.Value = lyricsText;
            }
        }

        response.Root!.Add(lyricsElement);
        return Ok(response);
    }

    [HttpGet("getScanStatus.view")]
    public IActionResult GetScanStatus()
    {
        var response = CreateResponse();
        response.Root!.Add(new XElement(SubsonicNamespace + "scanStatus",
            new XAttribute("scanning", _libraryService.IsScanning.ToString().ToLowerInvariant()),
            new XAttribute("count", _libraryService.ScanCount)
        ));
        return Ok(response);
    }

    [HttpGet("startScan.view")]
    public IActionResult StartScan()
    {
        _ = Task.Run(async () => await _libraryService.ScanMusicLibrary());

        var response = CreateResponse();
        response.Root!.Add(new XElement(SubsonicNamespace + "scanStatus",
            new XAttribute("scanning", "true"),
            new XAttribute("count", 0)
        ));
        return Ok(response);
    }

    [HttpGet("getIndexes.view")]
    public IActionResult GetIndexes([FromQuery] string? musicFolderId)
    {
        var response = CreateResponse();
        var directories = _libraryService.GetAllDirectories();

        var rootDirs = directories.Where(d =>
            string.IsNullOrEmpty(d.ParentId) ||
            (musicFolderId != null && d.ParentId == musicFolderId)
        ).ToList();

        var indexesElement = new XElement(SubsonicNamespace + "indexes",
            new XAttribute("ignoredArticles", "The El La Los Las Le Les"),
            new XAttribute("lastModified", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        );

        var grouped = rootDirs.GroupBy(d =>
            char.IsLetter(d.Name.FirstOrDefault()) ? char.ToUpperInvariant(d.Name[0]).ToString() : "#",
            StringComparer.Ordinal
        );

        foreach (var group in grouped.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var indexElement = new XElement(SubsonicNamespace + "index", new XAttribute("name", group.Key));

            foreach (var dir in group.OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                indexElement.Add(new XElement(SubsonicNamespace + "artist",
                    new XAttribute("id", dir.Id),
                    new XAttribute("name", dir.Name)
                ));
            }

            indexesElement.Add(indexElement);
        }

        response.Root!.Add(indexesElement);
        return Ok(response);
    }

    [HttpGet("getMusicDirectory.view")]
    public IActionResult GetMusicDirectory([FromQuery] string id)
    {
        var directory = _libraryService.GetDirectory(id);
        if (directory is null)
            return Ok(CreateError(70, "Directory not found"));

        var response = CreateResponse();
        var directoryElement = new XElement(SubsonicNamespace + "directory",
            new XAttribute("id", directory.Id),
            new XAttribute("name", directory.Name)
        );

        if (!string.IsNullOrEmpty(directory.ParentId))
            directoryElement.Add(new XAttribute("parent", directory.ParentId));

        foreach (var subDir in directory.SubDirectories.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            directoryElement.Add(new XElement(SubsonicNamespace + "child",
                new XAttribute("id", subDir.Id),
                new XAttribute("parent", directory.Id),
                new XAttribute("title", subDir.Name),
                new XAttribute("isDir", "true")
            ));
        }

        foreach (var file in directory.Files.OrderBy(f => f.Title, StringComparer.Ordinal))
        {
            var childElement = new XElement(SubsonicNamespace + "child",
                new XAttribute("id", file.Id),
                new XAttribute("parent", directory.Id),
                new XAttribute("title", file.Title),
                new XAttribute("isDir", "false"),
                new XAttribute("created", file.Created.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XAttribute("duration", file.Duration),
                new XAttribute("bitRate", file.BitRate ?? 0),
                new XAttribute("size", file.Size),
                new XAttribute("suffix", file.Suffix),
                new XAttribute("contentType", file.ContentType),
                new XAttribute("path", Path.GetFileName(file.Path))
            );

            if (!string.IsNullOrEmpty(file.Album))
                childElement.Add(new XAttribute("album", file.Album));
            if (!string.IsNullOrEmpty(file.Artist))
                childElement.Add(new XAttribute("artist", file.Artist));
            if (file.Track.HasValue)
                childElement.Add(new XAttribute("track", file.Track.Value));
            if (file.Year.HasValue)
                childElement.Add(new XAttribute("year", file.Year.Value));
            if (!string.IsNullOrEmpty(file.Genre))
                childElement.Add(new XAttribute("genre", file.Genre));
            if (file.CoverArt is not null)
                childElement.Add(new XAttribute("coverArt", file.CoverArt.Id));

            directoryElement.Add(childElement);
        }

        response.Root!.Add(directoryElement);
        return Ok(response);
    }

    [HttpGet("scrobble.view")]
    public async Task<IActionResult> Scrobble([FromQuery] string id, [FromQuery] bool? submission)
    {
        var song = _libraryService.GetSong(id);
        if (song is not null)
        {
            await _lastFmService.ScrobbleAsync(song, submission ?? true, HttpContext.RequestAborted);
        }

        return Ok(CreateResponse());
    }

    [HttpGet("getStarred2.view")]
    public IActionResult GetStarred2()
    {
        var response = CreateResponse();
        response.Root!.Add(new XElement(SubsonicNamespace + "starred2"));
        return Ok(response);
    }

    [HttpGet("getPodcasts.view")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter")]
    public IActionResult GetPodcasts([FromQuery] bool? includeEpisodes, [FromQuery] string? id)
    {
        var response = CreateResponse();
        response.Root!.Add(new XElement(SubsonicNamespace + "podcasts"));
        return Ok(response);
    }

    [HttpGet("getNewestPodcasts.view")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter")]
    public IActionResult GetNewestPodcasts([FromQuery] int? count)
    {
        var response = CreateResponse();
        response.Root!.Add(new XElement(SubsonicNamespace + "newestPodcasts"));
        return Ok(response);
    }

    [HttpGet("getInternetRadioStations.view")]
    public IActionResult GetInternetRadioStations()
    {
        var response = CreateResponse();
        response.Root!.Add(new XElement(SubsonicNamespace + "internetRadioStations"));
        return Ok(response);
    }

    [HttpGet("createPlaylist.view")]
    public async Task<IActionResult> CreatePlaylist(
        [FromQuery] string? name,
        [FromQuery] string? playlistId,
        [FromQuery] string? comment,
        [FromQuery] string[]? songId)
    {
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(playlistId))
        {
            return Ok(CreateError(10, "Required parameter is missing: name or playlistId"));
        }

        try
        {
            var playlistName = name ?? "New Playlist";
            var songIds = songId?.ToList() ?? new List<string>();

            var playlist = await _libraryService.CreatePlaylist(playlistName, comment, songIds);

            var response = CreateResponse();
            var playlistElement = new XElement(SubsonicNamespace + "playlist",
                new XAttribute("id", playlist.Id),
                new XAttribute("name", playlist.Name),
                new XAttribute("songCount", playlist.SongCount),
                new XAttribute("duration", playlist.Duration),
                new XAttribute("created", playlist.Created.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XAttribute("changed", playlist.Changed.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XAttribute("owner", "admin"),
                new XAttribute("public", "false")
            );

            if (playlist.CoverArt is not null)
                playlistElement.Add(new XAttribute("coverArt", playlist.CoverArt.Id));
            if (!string.IsNullOrEmpty(playlist.Comment))
                playlistElement.Add(new XAttribute("comment", playlist.Comment));

            response.Root!.Add(playlistElement);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create playlist");
            return Ok(CreateError(0, "Failed to create playlist"));
        }
    }

    [HttpGet("updatePlaylist.view")]
    public async Task<IActionResult> UpdatePlaylist(
        [FromQuery] string? playlistId,
        [FromQuery] string? name,
        [FromQuery] string? comment,
        [FromQuery] string[]? songIdToAdd,
        [FromQuery] int[]? songIndexToRemove)
    {
        if (string.IsNullOrEmpty(playlistId))
        {
            return Ok(CreateError(10, "Required parameter is missing: playlistId"));
        }

        try
        {
            var playlist = _libraryService.GetPlaylist(playlistId);
            if (playlist is null)
            {
                return Ok(CreateError(70, "Playlist not found"));
            }

            // Build the updated song list
            List<string>? updatedSongIds = null;

            // If we need to modify the song list
            if (songIdToAdd?.Length > 0 || songIndexToRemove?.Length > 0)
            {
                updatedSongIds = playlist.Items.Select(i => i.Song.Id).ToList();

                // Remove songs by index (in reverse order to maintain indices)
                if (songIndexToRemove?.Length > 0)
                {
                    foreach (var index in songIndexToRemove.OrderByDescending(i => i))
                    {
                        if (index >= 0 && index < updatedSongIds.Count)
                        {
                            updatedSongIds.RemoveAt(index);
                        }
                    }
                }

                // Add new songs
                if (songIdToAdd?.Length > 0)
                {
                    updatedSongIds.AddRange(songIdToAdd);
                }
            }

            var updatedPlaylist = await _libraryService.UpdatePlaylist(
                playlistId,
                name,
                comment,
                updatedSongIds);

            return Ok(CreateResponse());
        }
        catch (FileNotFoundException)
        {
            return Ok(CreateError(70, "Playlist not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update playlist");
            return Ok(CreateError(0, "Failed to update playlist"));
        }
    }

    [HttpGet("deletePlaylist.view")]
    public async Task<IActionResult> DeletePlaylist([FromQuery] string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return Ok(CreateError(10, "Required parameter is missing: id"));
        }

        try
        {
            await _libraryService.DeletePlaylist(id);
            return Ok(CreateResponse());
        }
        catch (FileNotFoundException)
        {
            return Ok(CreateError(70, "Playlist not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete playlist");
            return Ok(CreateError(0, "Failed to delete playlist"));
        }
    }

    [HttpGet("star.view")]
    [HttpGet("unstar.view")]
    [HttpGet("setRating.view")]
    public IActionResult WriteOperationNotSupported()
    {
        return Ok(CreateError(50, "User is not authorized for the given operation. This server is read-only."));
    }

    private static XDocument CreateResponse()
    {
        return new XDocument(
            new XElement(SubsonicNamespace + "subsonic-response",
                new XAttribute("status", "ok"),
                new XAttribute("version", SubsonicServerVersion)
            )
        );
    }

    private static XDocument CreateError(int code, string message)
    {
        var doc = new XDocument(
            new XElement(SubsonicNamespace + "subsonic-response",
                new XAttribute("status", "failed"),
                new XAttribute("version", SubsonicServerVersion),
                new XElement(SubsonicNamespace + "error",
                    new XAttribute("code", code),
                    new XAttribute("message", message)
                )
            )
        );
        return doc;
    }

    private static XElement CreateAlbumElement(Album album)
    {
        var element = new XElement(SubsonicNamespace + "album",
            new XAttribute("id", album.Id),
            new XAttribute("name", album.Name),
            new XAttribute("artist", album.Artist),
            new XAttribute("artistId", album.ArtistId),
            new XAttribute("songCount", album.SongCount),
            new XAttribute("duration", album.Duration),
            new XAttribute("created", album.Created.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))
        );

        if (album.Year.HasValue)
            element.Add(new XAttribute("year", album.Year.Value));
        if (!string.IsNullOrEmpty(album.Genre))
            element.Add(new XAttribute("genre", album.Genre));
        if (album.CoverArt is not null)
            element.Add(new XAttribute("coverArt", album.CoverArt.Id));

        return element;
    }

    private static XElement CreateSongElement(Song song)
    {
        return CreateSongElement(song, "song");
    }

    private static XElement CreateSongElement(Song song, string elementName)
    {
        var element = new XElement(SubsonicNamespace + elementName,
            new XAttribute("id", song.Id),
            new XAttribute("title", song.Title),
            new XAttribute("isDir", "false"),
            new XAttribute("created", song.Created.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            new XAttribute("duration", song.Duration),
            new XAttribute("bitRate", song.BitRate ?? 0),
            new XAttribute("size", song.Size),
            new XAttribute("suffix", song.Suffix),
            new XAttribute("contentType", song.ContentType),
            new XAttribute("isVideo", "false"),
            new XAttribute("path", Path.GetFileName(song.Path)),
            new XAttribute("type", "music")
        );

        if (!string.IsNullOrEmpty(song.Album))
            element.Add(new XAttribute("album", song.Album));
        if (!string.IsNullOrEmpty(song.AlbumId))
            element.Add(new XAttribute("albumId", song.AlbumId));
        if (!string.IsNullOrEmpty(song.Artist))
            element.Add(new XAttribute("artist", song.Artist));
        if (!string.IsNullOrEmpty(song.ArtistId))
            element.Add(new XAttribute("artistId", song.ArtistId));
        if (song.Track.HasValue)
            element.Add(new XAttribute("track", song.Track.Value));
        if (song.Year.HasValue)
            element.Add(new XAttribute("year", song.Year.Value));
        if (!string.IsNullOrEmpty(song.Genre))
            element.Add(new XAttribute("genre", song.Genre));
        if (song.CoverArt is not null)
            element.Add(new XAttribute("coverArt", song.CoverArt.Id));
        if (!string.IsNullOrEmpty(song.ParentId))
            element.Add(new XAttribute("parent", song.ParentId));

        // Add ReplayGain information as a nested element (Subsonic API extension)
        if (song.ReplayGainTrackGain.HasValue || song.ReplayGainAlbumGain.HasValue)
        {
            var replayGainElement = new XElement(SubsonicNamespace + "replayGain");
            if (song.ReplayGainTrackGain.HasValue)
                replayGainElement.Add(new XAttribute("trackGain", song.ReplayGainTrackGain.Value.ToString("F2", CultureInfo.InvariantCulture)));
            if (song.ReplayGainTrackPeak.HasValue)
                replayGainElement.Add(new XAttribute("trackPeak", song.ReplayGainTrackPeak.Value.ToString("F6", CultureInfo.InvariantCulture)));
            if (song.ReplayGainAlbumGain.HasValue)
                replayGainElement.Add(new XAttribute("albumGain", song.ReplayGainAlbumGain.Value.ToString("F2", CultureInfo.InvariantCulture)));
            if (song.ReplayGainAlbumPeak.HasValue)
                replayGainElement.Add(new XAttribute("albumPeak", song.ReplayGainAlbumPeak.Value.ToString("F6", CultureInfo.InvariantCulture)));
            element.Add(replayGainElement);
        }

        return element;
    }

    private static XmlResponse Ok(XDocument value)
    {
        return new XmlResponse(value);
    }

    private sealed class XmlResponse : IActionResult
    {
        private readonly XDocument _document;
        public XmlResponse(XDocument document)
        {
            _document = document;
        }
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.ContentType = "application/xml; charset=utf-8";
            var declaration = new XDeclaration("1.0", "UTF-8", standalone: null);
            var output = declaration.ToString() + Environment.NewLine + _document.ToString(SaveOptions.DisableFormatting);
            await response.WriteAsync(output);
        }
    }
}
