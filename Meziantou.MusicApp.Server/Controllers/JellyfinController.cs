using Meziantou.MusicApp.Server.Models;
using Meziantou.MusicApp.Server.Models.Jellyfin;
using Meziantou.MusicApp.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Meziantou.MusicApp.Server.Controllers;

[ApiController]
[Route("jellyfin")]
[ApiExplorerSettings(IgnoreApi = true)]
public class JellyfinController : ControllerBase
{
    private readonly MusicLibraryService _libraryService;
    private readonly MusicServerSettings _commonSettings;
    private readonly JellyfinSettings _jellyfinSettings;
    private readonly TranscodingService _transcodingService;
    private readonly ImageResizingService _imageResizingService;
    private readonly ILogger<JellyfinController> _logger;

    public JellyfinController(
        MusicLibraryService libraryService,
        IOptions<MusicServerSettings> commonSettings,
        IOptions<JellyfinSettings> jellyfinSettings,
        TranscodingService transcodingService,
        ImageResizingService imageResizingService,
        ILogger<JellyfinController> logger)
    {
        _libraryService = libraryService;
        _commonSettings = commonSettings.Value;
        _jellyfinSettings = jellyfinSettings.Value;
        _transcodingService = transcodingService;
        _imageResizingService = imageResizingService;
        _logger = logger;
    }

    #region System

    [HttpGet("System/Info/Public")]
    public IActionResult GetPublicSystemInfo()
    {
        return Ok(new PublicSystemInfo
        {
            ServerName = _jellyfinSettings.ServerName,
            Version = _jellyfinSettings.Version,
            Id = _jellyfinSettings.ServerId,
            OperatingSystem = Environment.OSVersion.Platform.ToString(),
        });
    }

    [HttpGet("System/Info")]
    public IActionResult GetSystemInfo()
    {
        return Ok(new SystemInfo
        {
            ServerName = _jellyfinSettings.ServerName,
            Version = _jellyfinSettings.Version,
            Id = _jellyfinSettings.ServerId,
            OperatingSystem = Environment.OSVersion.Platform.ToString(),
            HasPendingRestart = false,
            SupportsLibraryMonitor = true,
        });
    }

    #endregion

    #region Authentication

    [HttpPost("Users/AuthenticateByName")]
    public IActionResult AuthenticateByName([FromBody] AuthenticationRequest request)
    {
        _logger.LogInformation("Authentication attempt for user: {Username}", request.Username);

        if (!string.IsNullOrEmpty(_commonSettings.AuthToken) && request.Pw != _commonSettings.AuthToken)
        {
            return Unauthorized(new { error = "Invalid username or password" });
        }

        var user = new UserDto
        {
            Name = string.IsNullOrEmpty(request.Username) ? _jellyfinSettings.DefaultUserName : request.Username,
            ServerId = _jellyfinSettings.ServerId,
            Id = _jellyfinSettings.DefaultUserId,
            HasPassword = !string.IsNullOrEmpty(_commonSettings.AuthToken),
            HasConfiguredPassword = !string.IsNullOrEmpty(_commonSettings.AuthToken),
            Policy = new UserPolicy
            {
                IsAdministrator = true,
                IsHidden = false,
                IsDisabled = false,
                EnableAllFolders = true,
            },
        };

        return Ok(new AuthenticationResult
        {
            User = user,
            AccessToken = _commonSettings.AuthToken,
            ServerId = _jellyfinSettings.ServerId,
        });
    }

    #endregion

    #region Users

    [HttpGet("Users/{userId}")]
    public IActionResult GetUser(string userId)
    {
        return Ok(new UserDto
        {
            Name = _jellyfinSettings.DefaultUserName,
            ServerId = _jellyfinSettings.ServerId,
            Id = userId,
            HasPassword = !string.IsNullOrEmpty(_commonSettings.AuthToken),
            HasConfiguredPassword = !string.IsNullOrEmpty(_commonSettings.AuthToken),
            Policy = new UserPolicy
            {
                IsAdministrator = true,
                IsHidden = false,
                IsDisabled = false,
                EnableAllFolders = true,
            },
        });
    }

    #endregion

    #region Library Browsing

    [HttpGet("Users/{userId}/Items")]
    public IActionResult GetItems(
        string userId,
        [FromQuery] string? parentId,
        [FromQuery] string? includeItemTypes,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] bool? recursive)
    {
        _ = userId;
        _ = sortBy;
        _ = sortOrder;
        _ = recursive;

        var items = new List<BaseItemDto>();

        if (string.IsNullOrEmpty(parentId))
        {
            items.Add(new BaseItemDto
            {
                Name = "Music",
                Id = "music-root",
                Type = "CollectionFolder",
                ServerId = _jellyfinSettings.ServerId,
                MediaType = "Audio",
            });
        }
        else if (parentId == "music-root")
        {
            if (includeItemTypes?.Contains("MusicArtist", StringComparison.OrdinalIgnoreCase) == true || string.IsNullOrEmpty(includeItemTypes))
            {
                var artists = _libraryService.GetAllArtists();
                items.AddRange(artists.Select(MapArtistToItem));
            }
        }
        else if (parentId.StartsWith("artist-", StringComparison.Ordinal))
        {
            var artistId = parentId;
            var artist = _libraryService.GetArtist(artistId);
            if (artist != null)
            {
                items.AddRange(artist.Albums.Select(album => MapAlbumToItem(album, artistId)));
            }
        }
        else if (parentId.StartsWith("album-", StringComparison.Ordinal))
        {
            var albumId = parentId;
            var album = _libraryService.GetAlbum(albumId);
            if (album != null)
            {
                items.AddRange(album.Songs.Select(song => MapSongToItem(song, albumId)));
            }
        }

        var start = startIndex ?? 0;
        var count = limit ?? items.Count;
        var pagedItems = items.Skip(start).Take(count).ToList();

        return Ok(new QueryResult<BaseItemDto>
        {
            Items = pagedItems,
            TotalRecordCount = items.Count,
            StartIndex = start,
        });
    }

    [HttpGet("Artists")]
    public IActionResult GetArtists(
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? userId)
    {
        _ = userId;
        var artists = _libraryService.GetAllArtists();
        var items = artists.Select(MapArtistToItem).ToList();

        var start = startIndex ?? 0;
        var count = limit ?? items.Count;
        var pagedItems = items.Skip(start).Take(count).ToList();

        return Ok(new QueryResult<BaseItemDto>
        {
            Items = pagedItems,
            TotalRecordCount = items.Count,
            StartIndex = start,
        });
    }

    [HttpGet("Artists/AlbumArtists")]
    public IActionResult GetAlbumArtists(
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? userId)
    {
        return GetArtists(startIndex, limit, userId);
    }

    [HttpGet("Items/{itemId}")]
    public IActionResult GetItem(string itemId, [FromQuery] string? userId)
    {
        _ = userId;
        if (itemId.StartsWith("artist-", StringComparison.Ordinal))
        {
            var artist = _libraryService.GetArtist(itemId);
            if (artist != null)
            {
                return Ok(MapArtistToItem(artist));
            }
        }
        else if (itemId.StartsWith("album-", StringComparison.Ordinal))
        {
            var album = _libraryService.GetAlbum(itemId);
            if (album != null)
            {
                return Ok(MapAlbumToItem(album, album.ArtistId));
            }
        }
        else if (itemId.StartsWith("song-", StringComparison.Ordinal))
        {
            var song = _libraryService.GetSong(itemId);
            if (song != null && !string.IsNullOrEmpty(song.AlbumId))
            {
                return Ok(MapSongToItem(song, song.AlbumId));
            }
        }

        return NotFound();
    }

    #endregion

    #region Streaming

    [HttpGet("Audio/{itemId}/stream")]
    [HttpHead("Audio/{itemId}/stream")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities")]
    public async Task<IActionResult> StreamAudio(
        string itemId,
        [FromQuery] string? audioCodec,
        [FromQuery] int? audioBitRate,
        [FromQuery] int? maxAudioBitRate)
    {
        var song = _libraryService.GetSong(itemId);
        if (song == null)
            return NotFound();

        var filePath = song.Path;
        if (!System.IO.File.Exists(filePath))
            return NotFound();

        var targetBitRate = maxAudioBitRate ?? audioBitRate;
        var targetFormat = audioCodec;

        var needsTranscoding = targetBitRate.HasValue || !string.IsNullOrEmpty(targetFormat);

        if (needsTranscoding && !string.IsNullOrEmpty(targetFormat))
        {
#pragma warning disable CA2000
            var stream = await _transcodingService.TranscodeToStreamAsync(
                filePath,
                targetFormat,
                targetBitRate,
                null,
                HttpContext.RequestAborted);
#pragma warning restore CA2000

            var contentType = TranscodingService.GetContentType(targetFormat);
            return File(stream, contentType, enableRangeProcessing: false);
        }

        return PhysicalFile(filePath, song.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("Audio/{itemId}/stream.{format}")]
    [HttpHead("Audio/{itemId}/stream.{format}")]
    public async Task<IActionResult> StreamAudioWithFormat(
        string itemId,
        string format,
        [FromQuery] int? audioBitRate,
        [FromQuery] int? maxAudioBitRate)
    {
        return await StreamAudio(itemId, format, audioBitRate, maxAudioBitRate);
    }

    #endregion

    #region Images

    [HttpGet("Items/{itemId}/Images/{imageType}")]
    [HttpHead("Items/{itemId}/Images/{imageType}")]
    public async Task<IActionResult> GetItemImage(
        string itemId,
        string imageType,
        [FromQuery] int? maxWidth,
        [FromQuery] int? maxHeight,
        [FromQuery] int? quality)
    {
        _ = imageType;
        _ = quality;
        string? coverArtId = null;

        if (itemId.StartsWith("artist-", StringComparison.Ordinal))
        {
            var artist = _libraryService.GetArtist(itemId);
            coverArtId = artist?.CoverArt?.Id;
        }
        else if (itemId.StartsWith("album-", StringComparison.Ordinal))
        {
            var album = _libraryService.GetAlbum(itemId);
            coverArtId = album?.CoverArt?.Id;
        }
        else if (itemId.StartsWith("song-", StringComparison.Ordinal))
        {
            var song = _libraryService.GetSong(itemId);
            coverArtId = song?.CoverArt?.Id;
        }

        if (string.IsNullOrEmpty(coverArtId))
            return NotFound();

        var coverArtData = _libraryService.GetCoverArt(coverArtId);
        if (coverArtData == null)
            return NotFound();

        // Check if client's cached version is still valid
        if (ImageCacheHelper.IsNotModified(Request, coverArtData.LastModified))
        {
            return StatusCode(304);
        }

        var size = maxWidth.HasValue && maxHeight.HasValue
            ? Math.Min(maxWidth.Value, maxHeight.Value)
            : maxWidth ?? maxHeight;

        var coverArt = coverArtData.Data;
        if (size.HasValue && size.Value > 0)
        {
            coverArt = await _imageResizingService.ResizeImageAsync(coverArt, size, HttpContext.RequestAborted);
        }

        ImageCacheHelper.SetImageCacheHeaders(Response, coverArtData.LastModified);

        return File(coverArt, "image/jpeg");
    }

    #endregion

    #region Search

    [HttpGet("Search/Hints")]
    public IActionResult SearchHints(
        [FromQuery] string? searchTerm,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? userId)
    {
        _ = userId;
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Ok(new QueryResult<BaseItemDto>
            {
                Items = [],
                TotalRecordCount = 0,
                StartIndex = 0,
            });
        }

        var results = _libraryService.SearchAll(searchTerm);
        var items = new List<BaseItemDto>();

        items.AddRange(results.artists.Select(MapArtistToItem));
        items.AddRange(results.albums.Select(album => MapAlbumToItem(album, album.ArtistId)));
        items.AddRange(results.songs.Select(song => MapSongToItem(song, song.AlbumId ?? string.Empty)));

        var start = startIndex ?? 0;
        var count = limit ?? items.Count;
        var pagedItems = items.Skip(start).Take(count).ToList();

        return Ok(new QueryResult<BaseItemDto>
        {
            Items = pagedItems,
            TotalRecordCount = items.Count,
            StartIndex = start,
        });
    }

    #endregion

    #region Mapping Helpers

    private BaseItemDto MapArtistToItem(Artist artist)
    {
        return new BaseItemDto
        {
            Name = artist.Name,
            Id = artist.Id,
            Type = "MusicArtist",
            ServerId = _jellyfinSettings.ServerId,
            ImageTags = artist.CoverArt is not null ? artist.Id : null,
            ChildCount = artist.AlbumCount,
            MediaType = "Audio",
        };
    }

    private BaseItemDto MapAlbumToItem(Album album, string artistId)
    {
        return new BaseItemDto
        {
            Name = album.Name,
            Id = album.Id,
            Type = "MusicAlbum",
            ServerId = _jellyfinSettings.ServerId,
            AlbumArtist = album.Artist,
            Album = album.Name,
            Artists = [album.Artist],
            ParentId = artistId,
            ProductionYear = album.Year,
            PremiereDate = album.Year.HasValue ? new DateTime(album.Year.Value, 1, 1) : null,
            Genres = string.IsNullOrEmpty(album.Genre) ? [] : [album.Genre],
            ImageTags = album.CoverArt is not null ? album.Id : null,
            RunTimeTicks = album.Duration * TimeSpan.TicksPerSecond,
            ChildCount = album.SongCount,
            MediaType = "Audio",
        };
    }

    private BaseItemDto MapSongToItem(Song song, string albumId)
    {
        return new BaseItemDto
        {
            Name = song.Title,
            Id = song.Id,
            Type = "Audio",
            ServerId = _jellyfinSettings.ServerId,
            AlbumArtist = song.AlbumArtist,
            Album = song.Album,
            AlbumId = albumId,
            Artists = string.IsNullOrEmpty(song.Artist) ? [] : [song.Artist],
            ParentId = albumId,
            IndexNumber = song.Track,
            ProductionYear = song.Year,
            PremiereDate = song.Year.HasValue ? new DateTime(song.Year.Value, 1, 1) : null,
            Genres = string.IsNullOrEmpty(song.Genre) ? [] : [song.Genre],
            ImageTags = song.CoverArt is not null ? song.Id : null,
            RunTimeTicks = song.Duration * TimeSpan.TicksPerSecond,
            MediaType = "Audio",
            MediaSources =
            [
                new MediaSourceInfo
                {
                    Id = song.Id,
                    Path = song.Path,
                    Container = song.Suffix,
                    Size = song.Size,
                    RunTimeTicks = song.Duration * TimeSpan.TicksPerSecond,
                    MediaStreams =
                    [
                        new MediaStream
                        {
                            Type = "Audio",
                            Codec = song.Suffix,
                            BitRate = song.BitRate,
                        },
                    ],
                },
            ],
        };
    }

    #endregion
}

