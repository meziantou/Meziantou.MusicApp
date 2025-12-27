using Meziantou.MusicApp.Server.Models;
using Meziantou.MusicApp.Server.Models.RestApi;
using Meziantou.MusicApp.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Meziantou.MusicApp.Server.Controllers;

[ApiController]
[Route("api")]
[ExcludeFromDescription]
public class RestApiController(MusicLibraryService library, TranscodingService transcoding, ImageResizingService imageResizing, LastFmService lastFm, ILogger<RestApiController> logger) : ControllerBase
{
    /// <summary>Get all playlists with name and track count</summary>
    [HttpGet("playlists.json")]
    [ProducesResponseType<PlaylistsResponse>(StatusCodes.Status200OK)]
    public ActionResult<PlaylistsResponse> GetPlaylists()
    {
        var playlists = library.GetPlaylists()
            .OrderByDescending(p => Playlist.IsVirtualPlaylist(p.Id))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select((p, index) => new PlaylistSummary
            {
                Id = p.Id,
                Name = p.Name,
                TrackCount = p.SongCount,
                Duration = p.Duration,
                Created = p.Created,
                Changed = p.Changed,
                SortOrder = index,
            }).ToList();

        return Ok(new PlaylistsResponse { Playlists = playlists });
    }

    /// <summary>Get tracks for a specific playlist</summary>
    [HttpGet("playlists/{id}.json")]
    [ProducesResponseType<PlaylistTracksResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public ActionResult<PlaylistTracksResponse> GetPlaylistTracks(string id)
    {
        var playlist = library.GetPlaylist(id);
        if (playlist == null)
        {
            return NotFound(new ErrorResponse { Error = "Playlist not found" });
        }

        return Ok(CreatePlaylistTracksResponse(playlist));
    }

    /// <summary>Create a new playlist</summary>
    [HttpPost("playlists.json")]
    [ProducesResponseType<PlaylistTracksResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlaylistTracksResponse>> CreatePlaylist([FromBody] CreatePlaylistRequest request)
    {
        try
        {
            var playlist = await library.CreatePlaylist(request.Name, request.Comment, request.SongIds);
            var response = CreatePlaylistTracksResponse(playlist);
            return CreatedAtAction(nameof(GetPlaylistTracks), new { id = playlist.Id }, response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create playlist");
            return BadRequest(new ErrorResponse { Error = "Failed to create playlist" });
        }
    }

    /// <summary>Update an existing playlist</summary>
    [HttpPut("playlists/{id}.json")]
    [ProducesResponseType<PlaylistTracksResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlaylistTracksResponse>> UpdatePlaylist(string id, [FromBody] UpdatePlaylistRequest request)
    {
        try
        {
            var playlist = await library.UpdatePlaylist(id, request.Name, request.Comment, request.SongIds);
            return Ok(CreatePlaylistTracksResponse(playlist));
        }
        catch (FileNotFoundException)
        {
            return NotFound(new ErrorResponse { Error = "Playlist not found" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update playlist");
            return BadRequest(new ErrorResponse { Error = "Failed to update playlist" });
        }
    }

    /// <summary>Delete a playlist</summary>
    [HttpDelete("playlists/{id}.json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePlaylist(string id)
    {
        try
        {
            await library.DeletePlaylist(id);
            return NoContent();
        }
        catch (FileNotFoundException)
        {
            return NotFound(new ErrorResponse { Error = "Playlist not found" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete playlist");
            return BadRequest(new ErrorResponse { Error = "Failed to delete playlist" });
        }
    }

    /// <summary>Rename a playlist (renames the file on disk)</summary>
    [HttpPost("playlists/{id}/rename.json")]
    [ProducesResponseType<PlaylistTracksResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlaylistTracksResponse>> RenamePlaylist(string id, [FromBody] RenamePlaylistRequest request)
    {
        try
        {
            var playlist = await library.RenamePlaylist(id, request.Name);
            return Ok(CreatePlaylistTracksResponse(playlist));
        }
        catch (FileNotFoundException)
        {
            return NotFound(new ErrorResponse { Error = "Playlist not found" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.Ordinal))
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rename playlist");
            return BadRequest(new ErrorResponse { Error = "Failed to rename playlist" });
        }
    }

    private static PlaylistTracksResponse CreatePlaylistTracksResponse(Playlist playlist)
    {
        var tracks = playlist.Items
            .Select(item => CreateTrackInfo(item.Song, item.AddedDate))
            .ToList();

        return new PlaylistTracksResponse
        {
            Id = playlist.Id,
            Name = playlist.Name,
            TrackCount = playlist.SongCount,
            Duration = playlist.Duration,
            Created = playlist.Created,
            Changed = playlist.Changed,
            Tracks = tracks,
        };
    }

    private static TrackInfo CreateTrackInfo(Song song, DateTime? addedDate = null)
    {
        return new TrackInfo
        {
            Id = song.Id,
            Title = song.Title,
            Path = song.Path,
            Artists = song.Artist,
            ArtistId = song.ArtistId,
            Album = song.Album,
            AlbumId = song.AlbumId,
            Duration = song.Duration,
            Track = song.Track,
            Year = song.Year,
            Genre = song.Genre,
            BitRate = song.BitRate,
            Size = song.Size,
            ContentType = song.ContentType,
            AddedDate = addedDate,
            Isrc = song.Isrc,
            ReplayGainTrackGain = song.ReplayGainTrackGain,
            ReplayGainTrackPeak = song.ReplayGainTrackPeak,
            ReplayGainAlbumGain = song.ReplayGainAlbumGain,
            ReplayGainAlbumPeak = song.ReplayGainAlbumPeak,
        };
    }

    /// <summary>Get all available tracks (not only those in playlists)</summary>
    [HttpGet("tracks.json")]
    [ProducesResponseType<TracksResponse>(StatusCodes.Status200OK)]
    public ActionResult<TracksResponse> GetAllTracks([FromQuery] int? limit = null, [FromQuery] int? offset = null)
    {
        var allSongs = library.GetAllSongs();

        if (offset.HasValue)
        {
            allSongs = allSongs.Skip(offset.Value);
        }

        if (limit.HasValue)
        {
            allSongs = allSongs.Take(limit.Value);
        }

        var tracks = allSongs.Select(song => CreateTrackInfo(song)).ToList();

        return Ok(new TracksResponse { Tracks = tracks });
    }

    /// <summary>
    /// Get song data (stream audio file). 
    /// If format is specified, the audio will be transcoded; otherwise, the raw file is returned.
    /// </summary>
    [HttpGet("songs/{id}/data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public async Task<IActionResult> GetSongData(
        string id,
        [FromQuery] string? format = null,
        [FromQuery] int? maxBitRate = null,
        [FromQuery] int? timeOffset = null,
        CancellationToken cancellationToken = default)
    {
        var song = library.GetSong(id);
        if (song == null)
        {
            return NotFound(new ErrorResponse { Error = "Song not found" });
        }

        if (!System.IO.File.Exists(song.Path))
        {
            logger.LogError("Song file not found: {Path}", song.Path);
            return NotFound(new ErrorResponse { Error = "Song file not found" });
        }

        // If no format is specified, return raw file
        if (string.IsNullOrEmpty(format))
        {
            var stream = System.IO.File.OpenRead(song.Path);
            return File(stream, song.ContentType, enableRangeProcessing: true);
        }

        // Transcode the file
        try
        {
            var stream = await transcoding.TranscodeToStreamAsync(
                song.Path,
                format,
                maxBitRate,
                timeOffset,
                cancellationToken);

            var contentType = format switch
            {
                "mp3" => "audio/mpeg",
                "opus" => "audio/opus",
                "ogg" => "audio/ogg",
                "m4a" => "audio/mp4",
                "flac" => "audio/flac",
                _ => "audio/mpeg",
            };

            return File(stream, contentType, enableRangeProcessing: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error transcoding song: {SongId}", id);
            return StatusCode(500, new ErrorResponse { Error = "Transcoding failed" });
        }
    }

    /// <summary>Get cover image for a song</summary>
    [HttpGet("songs/{id}/cover")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSongCover(string id, [FromQuery] int? size = null)
    {
        var song = library.GetSong(id);
        if (song == null)
        {
            return NotFound(new ErrorResponse { Error = "Song not found" });
        }

        var coverArtId = song.CoverArt?.Id;
        var coverArtData = library.GetCoverArt(coverArtId);

        if (coverArtData == null)
        {
            return NotFound(new ErrorResponse { Error = "Cover art not found" });
        }

        // Check if client's cached version is still valid
        if (ImageCacheHelper.IsNotModified(Request, coverArtData.LastModified))
        {
            return StatusCode(304);
        }

        var coverData = coverArtData.Data;

        // Resize if size parameter is provided
        if (size.HasValue && size.Value > 0)
        {
            coverData = await imageResizing.ResizeImageAsync(coverData, size, HttpContext.RequestAborted);
        }

        ImageCacheHelper.SetImageCacheHeaders(Response, coverArtData.LastModified);

        return File(coverData, "image/jpeg");
    }

    /// <summary>Get cover image for an album (uses first track)</summary>
    [HttpGet("albums/{id}/cover")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAlbumCover(string id, [FromQuery] int? size = null)
    {
        var album = library.GetAlbum(id);
        if (album == null)
        {
            return NotFound(new ErrorResponse { Error = "Album not found" });
        }

        var coverArtId = album.CoverArt?.Id;
        var coverArtData = library.GetCoverArt(coverArtId);

        if (coverArtData == null)
        {
            return NotFound(new ErrorResponse { Error = "Cover art not found" });
        }

        // Check if client's cached version is still valid
        if (ImageCacheHelper.IsNotModified(Request, coverArtData.LastModified))
        {
            return StatusCode(304);
        }

        var coverData = coverArtData.Data;

        // Resize if size parameter is provided
        if (size.HasValue && size.Value > 0)
        {
            coverData = await imageResizing.ResizeImageAsync(coverData, size, HttpContext.RequestAborted);
        }

        ImageCacheHelper.SetImageCacheHeaders(Response, coverArtData.LastModified);

        return File(coverData, "image/jpeg");
    }

    /// <summary>Get cover image for an artist (uses first track)</summary>
    [HttpGet("artists/{id}/cover")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetArtistCover(string id, [FromQuery] int? size = null)
    {
        var artist = library.GetArtist(id);
        if (artist == null)
        {
            return NotFound(new ErrorResponse { Error = "Artist has no cover art" });
        }

        var coverArtId = artist.CoverArt?.Id;
        if (coverArtId == null && artist.Albums.Count > 0)
        {
            // Get cover from first album
            var firstAlbum = artist.Albums.FirstOrDefault();
            coverArtId = firstAlbum?.CoverArt?.Id;
        }

        if (coverArtId == null)
        {
            return NotFound(new ErrorResponse { Error = "Artist has no cover art" });
        }

        var coverArtData = library.GetCoverArt(coverArtId);

        if (coverArtData == null)
        {
            return NotFound(new ErrorResponse { Error = "Cover art not found" });
        }

        // Check if client's cached version is still valid
        if (ImageCacheHelper.IsNotModified(Request, coverArtData.LastModified))
        {
            return StatusCode(304);
        }

        var coverData = coverArtData.Data;

        // Resize if size parameter is provided
        if (size.HasValue && size.Value > 0)
        {
            coverData = await imageResizing.ResizeImageAsync(coverData, size, HttpContext.RequestAborted);
        }

        ImageCacheHelper.SetImageCacheHeaders(Response, coverArtData.LastModified);

        return File(coverData, "image/jpeg");
    }

    /// <summary>Get all albums</summary>
    [HttpGet("albums.json")]
    [ProducesResponseType<AlbumsResponse>(StatusCodes.Status200OK)]
    public ActionResult<AlbumsResponse> GetAllAlbums()
    {
        var albums = library.GetAllAlbums().Select(a => new AlbumInfo
        {
            Id = a.Id,
            Name = a.Name,
            Artist = a.Artist,
            ArtistId = a.ArtistId,
            Year = a.Year,
            Genre = a.Genre,
            Duration = a.Duration,
            SongCount = a.SongCount,
            Created = a.Created,
        }).ToList();

        return Ok(new AlbumsResponse { Albums = albums });
    }

    /// <summary>Get all artists</summary>
    [HttpGet("artists.json")]
    [ProducesResponseType<ArtistsResponse>(StatusCodes.Status200OK)]
    public ActionResult<ArtistsResponse> GetAllArtists()
    {
        var artists = library.GetAllArtists().Select(a => new ArtistInfo
        {
            Id = a.Id,
            Name = a.Name,
            AlbumCount = a.AlbumCount,
        }).ToList();

        return Ok(new ArtistsResponse { Artists = artists });
    }

    /// <summary>Trigger a library scan</summary>
    [HttpPost("scan.json")]
    [ProducesResponseType<ScanStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<ScanStatusResponse> TriggerScan()
    {
        // Trigger scan in background
        _ = Task.Run(() => library.ScanMusicLibrary());

        return Ok(new ScanStatusResponse
        {
            IsScanning = library.IsScanning,
            IsInitialScanCompleted = library.IsInitialScanCompleted,
            ScanCount = library.ScanCount,
            LastScanDate = library.LastScanDate,
            Percentage = library.ScanProgress,
            EstimatedCompletionTime = library.ScanEta,
        });
    }

    /// <summary>Get scan status</summary>
    [HttpGet("scan/status.json")]
    [ProducesResponseType<ScanStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<ScanStatusResponse> GetScanStatus()
    {
        return Ok(new ScanStatusResponse
        {
            IsScanning = library.IsScanning,
            IsInitialScanCompleted = library.IsInitialScanCompleted,
            ScanCount = library.ScanCount,
            LastScanDate = library.LastScanDate,
            Percentage = library.ScanProgress,
            EstimatedCompletionTime = library.ScanEta,
        });
    }

    /// <summary>Scrobble a track to Last.fm</summary>
    /// <remarks>
    /// Sends track play information to Last.fm. 
    /// When submission is true, it records the track as played.
    /// When submission is false, it updates the "Now Playing" status.
    /// </remarks>
    [HttpPost("scrobble.json")]
    [ProducesResponseType<ScrobbleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScrobbleResponse>> Scrobble([FromBody] ScrobbleRequest request)
    {
        if (string.IsNullOrEmpty(request.Id))
        {
            return BadRequest(new ErrorResponse { Error = "Song ID is required" });
        }

        var song = library.GetSong(request.Id);
        if (song == null)
        {
            return NotFound(new ErrorResponse { Error = "Song not found" });
        }

        if (!lastFm.IsConfigured)
        {
            return Ok(new ScrobbleResponse
            {
                Success = false,
                Title = song.Title,
                Artist = song.Artist,
                Message = "Last.fm is not configured",
            });
        }

        var success = await lastFm.ScrobbleAsync(song, request.Submission, HttpContext.RequestAborted);

        return Ok(new ScrobbleResponse
        {
            Success = success,
            Title = song.Title,
            Artist = song.Artist,
            Message = success
                ? (request.Submission ? "Track scrobbled successfully" : "Now Playing updated successfully")
                : "Failed to scrobble to Last.fm",
        });
    }
}
