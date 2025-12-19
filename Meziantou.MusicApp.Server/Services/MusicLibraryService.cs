using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Meziantou.MusicApp.Server.Models;
using Meziantou.Framework;
using Microsoft.Extensions.Options;

namespace Meziantou.MusicApp.Server.Services;

public sealed class MusicLibraryService(ILogger<MusicLibraryService> logger, IOptions<MusicServerSettings> options, ReplayGainService replayGainService) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SemaphoreSlim _scanSemaphore = new(1, 1);
    private readonly TaskCompletionSource _initialScanCompleted = new();
    private MusicCatalog _catalog = new MusicCatalog(FullPath.FromPath(options.Value.MusicFolderPath ?? ""));
    private SerializableMusicCatalog? _cachedSerializableCatalog;
    private int _scanCount;
    private int _processedFilesCount;
    private int _totalFilesToScan;
    private DateTime _scanStartTime;

    private static readonly XNamespace XspfNamespace = "http://xspf.org/ns/0/";
    private static readonly XNamespace MeziantouExtensionNamespace = "http://meziantou.net/xspf-extension/1/";

    private static readonly string[] AudioExtensions = [".mp3", ".flac", ".m4a", ".ogg", ".opus", ".wav", ".aac", ".wma"];
    private static readonly string[] M3uPlaylistExtensions = [".m3u", ".m3u8"];
    private static readonly string[] XspfPlaylistExtensions = [".xspf"];
    private static readonly string[] CoverFiles =
    [
        "cover.jpg", "cover.jpeg", "cover.png",
        "folder.jpg", "folder.jpeg", "folder.png",
        "front.jpg", "front.jpeg", "front.png",
        "album.jpg", "album.jpeg", "album.png",
        "Cover.jpg", "Cover.jpeg", "Cover.png",
        "Folder.jpg", "Folder.jpeg", "Folder.png",
        "Front.jpg", "Front.jpeg", "Front.png",
        "Album.jpg", "Album.jpeg", "Album.png",
    ];

    public MusicCatalog Catalog => _catalog;
    public bool IsInitialScanCompleted => _initialScanCompleted.Task.IsCompleted;
    public Task InitialScanCompleted => _initialScanCompleted.Task;
    public bool IsScanning => _scanSemaphore.CurrentCount is 0;
    public int ScanCount => IsScanning ? _processedFilesCount : _scanCount;
    public double? ScanProgress => IsScanning && _totalFilesToScan > 0 ? (double)_processedFilesCount / _totalFilesToScan * 100 : null;
    public TimeSpan? ScanEta
    {
        get
        {
            if (!IsScanning || _processedFilesCount == 0 || _totalFilesToScan == 0) return null;
            var elapsed = DateTime.UtcNow - _scanStartTime;
            var rate = elapsed.TotalSeconds / _processedFilesCount;
            var remainingItems = _totalFilesToScan - _processedFilesCount;
            return TimeSpan.FromSeconds(rate * remainingItems);
        }
    }
    public DateTime? LastScanDate => _catalog.LastScanDate;

    public int MusicFileCount => _catalog.Songs.Count;
    public int PlaylistCount => _catalog.Playlists.Count;

    private FullPath RootFolder => _catalog.RootPath;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Music Library Service is starting");

        await LoadCachedLibrary();

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Starting scheduled music library scan");
            await Task.Run(() => ScanMusicLibrary(), stoppingToken);
            await Task.Delay(options.Value.CacheRefreshInterval, stoppingToken);
        }
    }

    private async Task LoadCachedLibrary()
    {
        try
        {
            var cachePath = GetCacheJsonPath();
            if (cachePath.IsEmpty || !File.Exists(cachePath))
            {
                logger.LogInformation("No cached library found at {Path}", cachePath);
                return;
            }

            logger.LogInformation("Loading cached music library from {Path}", cachePath);
            var json = await File.ReadAllTextAsync(cachePath);
            var content = JsonSerializer.Deserialize<SerializableMusicCatalog>(json, JsonOptions);
            if (content is not null)
            {
                _cachedSerializableCatalog = content;
                _catalog = await CreateCatalog(content);
                logger.LogInformation("Loaded cached library");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading cached library from {Path}", GetCacheJsonPath());
        }
    }

    private FullPath GetCacheJsonPath()
    {
        if (string.IsNullOrEmpty(options.Value.CachePath))
            return FullPath.Empty;

        return FullPath.FromPath(options.Value.CachePath) / "cache.json";
    }

    private FullPath GetCoverArtCachePath()
    {
        if (string.IsNullOrEmpty(options.Value.CachePath))
            return FullPath.Empty;

        return FullPath.FromPath(options.Value.CachePath) / "cover";
    }

    private async Task<MusicCatalog> CreateCatalog(SerializableMusicCatalog content)
    {
        var coverArtCachePath = GetCoverArtCachePath();
        return await MusicCatalog.Create(content, RootFolder, coverArtCachePath);
    }

    public async Task ScanMusicLibrary()
    {
        var rootFolder = RootFolder;
        var lockAcquired = false;
        try
        {
            if (!Directory.Exists(rootFolder))
            {
                logger.LogWarning("Music folder path does not exist: {Path}", rootFolder);
                return;
            }

            if (!_scanSemaphore.Wait(0))
            {
                logger.LogInformation("Library scan already in progress, skipping concurrent scan request");
                return;
            }

            lockAcquired = true;
            logger.LogInformation("Scanning music library");
            var library = new SerializableMusicCatalog();
            var files = Directory.EnumerateFiles(rootFolder, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.None, IgnoreInaccessible = true, RecurseSubdirectories = true })
                .Select(FullPath.FromPath)
                .ToArray();

            _totalFilesToScan = files.Count(f => AudioExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase));
            _processedFilesCount = 0;
            _scanStartTime = DateTime.UtcNow;

            // Build a lookup dictionary from cached songs for incremental scanning
            var cachedSongsByPath = _cachedSerializableCatalog?.Songs
                .ToDictionary(s => s.RelativePath, s => s, StringComparer.Ordinal)
                ?? new Dictionary<string, SerializableSong>(StringComparer.Ordinal);

            foreach (var file in files)
            {
                if (AudioExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    await ScanMusicFile(new IndexerContext(rootFolder, file, library, cachedSongsByPath));
                    _processedFilesCount++;
                    logger.LogInformation("Processed file {Count}/{Total}: {Path}", _processedFilesCount, _totalFilesToScan, file);
                }
            }

            // Scan existing XSPF playlists
            foreach (var file in files)
            {
                if (XspfPlaylistExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Scanning XSPF playlist: {Path}", file);
                    await ScanXspfPlaylist(new IndexerContext(rootFolder, file, library));
                }
            }

            // Convert M3U/M3U8 files to XSPF format and scan them
            foreach (var file in files)
            {
                if (M3uPlaylistExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Converting and scanning M3U playlist: {Path}", file);
                    var xspfPath = await ConvertM3uToXspf(new IndexerContext(rootFolder, file, library));
                    if (xspfPath != null)
                    {
                        await ScanXspfPlaylist(new IndexerContext(rootFolder, xspfPath.Value, library));
                    }
                }
            }

            var cachePath = GetCacheJsonPath();
            if (!cachePath.IsEmpty)
            {
                try
                {
                    logger.LogInformation("Caching music library to {Path}", cachePath);
                    var json = JsonSerializer.Serialize(library, JsonOptions);
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    await File.WriteAllTextAsync(cachePath, json);
                    logger.LogInformation("Cached music library to {Path}", cachePath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error caching music library to {Path}", cachePath);
                }
            }

            logger.LogInformation("Music library scan finished, updating catalog");
            _cachedSerializableCatalog = library;
            _catalog = await CreateCatalog(library);
            _scanCount = _catalog.Songs.Count;

            logger.LogInformation("Music library scan complete. Found {FileCount} files, {ArtistCount} artists, {AlbumCount} albums, {PlaylistCount} playlists",
                _catalog.Songs.Count, _catalog.Artists.Count, _catalog.Albums.Count, _catalog.Playlists.Count);
        }
        catch (Exception ex)
        {
            _initialScanCompleted.TrySetException(ex);
            logger.LogError(ex, "Error scanning music library at {Path}", rootFolder);
            throw;
        }
        finally
        {
            if (lockAcquired)
            {
                _scanSemaphore.Release();
            }

            _initialScanCompleted.TrySetResult();
        }
    }

    private async Task ScanMusicFile(IndexerContext context)
    {
        try
        {
            var fileInfo = new FileInfo(context.Path);
            var relativePath = context.RelativePath;
            var fileLastWriteTime = fileInfo.LastWriteTimeUtc;

            // Check if we have a cached version of this song and if the file hasn't changed
            if (context.CachedSongsByPath.TryGetValue(relativePath, out var cachedSong) &&
                TruncateMilliseconds(cachedSong.FileLastWriteTime) >= TruncateMilliseconds(fileLastWriteTime) &&
                cachedSong.FileSize == fileInfo.Length)
            {
                // File hasn't changed, reuse cached metadata
                // Check if external files (LRC, cover art) still exist
                var song = new SerializableSong
                {
                    RelativePath = cachedSong.RelativePath,
                    FileSize = cachedSong.FileSize,
                    FileCreatedAt = cachedSong.FileCreatedAt,
                    FileLastWriteTime = cachedSong.FileLastWriteTime,
                    Title = cachedSong.Title,
                    AlbumName = cachedSong.AlbumName,
                    Artist = cachedSong.Artist,
                    AlbumArtist = cachedSong.AlbumArtist,
                    Genre = cachedSong.Genre,
                    Year = cachedSong.Year,
                    Track = cachedSong.Track,
                    Duration = cachedSong.Duration,
                    BitRate = cachedSong.BitRate,
                    Lyrics = cachedSong.Lyrics,
                    HasEmbeddedCover = cachedSong.HasEmbeddedCover,
                    ReplayGainTrackGain = cachedSong.ReplayGainTrackGain,
                    ReplayGainTrackPeak = cachedSong.ReplayGainTrackPeak,
                    ReplayGainAlbumGain = cachedSong.ReplayGainAlbumGain,
                    ReplayGainAlbumPeak = cachedSong.ReplayGainAlbumPeak,
                };

                // Re-check external cover art path (file might have been added/removed)
                if (!song.HasEmbeddedCover)
                {
                    foreach (var coverFileName in CoverFiles)
                    {
                        var coverFilePath = context.Path.Parent / coverFileName;
                        if (File.Exists(coverFilePath))
                        {
                            song.ExternalCoverArtPath = context.CreateRelativePath(coverFilePath);
                            break;
                        }
                    }
                }

                // Re-check external lyrics path
                var lrcFilePath = context.Path.ChangeExtension(".lrc");
                if (File.Exists(lrcFilePath))
                {
                    song.ExternalLyricsPath = context.CreateRelativePath(lrcFilePath);
                }

                // Check if we need to compute ReplayGain for cached song
                if (options.Value.ComputeMissingReplayGain && song.ReplayGainTrackGain is null)
                {
                    await ComputeReplayGainAsync(context.Path, song);
                }

                context.Catalog.Songs.Add(song);
                return;
            }

            // File is new or has changed, read metadata from file
            var newSong = new SerializableSong
            {
                RelativePath = relativePath,
                FileSize = fileInfo.Length,
                FileCreatedAt = fileInfo.CreationTimeUtc,
                FileLastWriteTime = fileLastWriteTime,
                Title = context.Path.NameWithoutExtension,
            };

            try
            {
                using var file = TagLib.File.Create(context.Path);
                newSong.Title = file.Tag.Title ?? context.Path.NameWithoutExtension;
                newSong.AlbumName = file.Tag.Album;
                newSong.Artist = file.Tag.FirstPerformer;
                newSong.AlbumArtist = file.Tag.FirstAlbumArtist ?? file.Tag.FirstPerformer ?? string.Empty;
                newSong.Genre = file.Tag.FirstGenre ?? string.Empty;
                newSong.Year = (int)file.Tag.Year;
                newSong.Track = (int)file.Tag.Track;
                newSong.Duration = file.Properties.Duration;
                newSong.BitRate = file.Properties.AudioBitrate;
                newSong.Lyrics = file.Tag.Lyrics;

                if (file.Tag.Pictures.Length > 0)
                {
                    newSong.HasEmbeddedCover = true;
                }

                if (!newSong.HasEmbeddedCover)
                {
                    foreach (var coverFileName in CoverFiles)
                    {
                        var coverFilePath = context.Path.Parent / coverFileName;
                        if (File.Exists(coverFilePath))
                        {
                            newSong.ExternalCoverArtPath = context.CreateRelativePath(coverFilePath);
                            break;
                        }
                    }
                }

                // Read ReplayGain tags from file
                ReadReplayGainTags(file, newSong);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error reading tags for file: {Path}", context.Path);
            }

            var lrcPath = context.Path.ChangeExtension(".lrc");
            if (File.Exists(lrcPath))
            {
                newSong.ExternalLyricsPath = context.CreateRelativePath(lrcPath);
            }

            // Compute ReplayGain if missing and enabled
            if (options.Value.ComputeMissingReplayGain && newSong.ReplayGainTrackGain is null)
            {
                await ComputeReplayGainAsync(context.Path, newSong);
            }

            context.Catalog.Songs.Add(newSong);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scanning music file: {Path}", context.Path);
        }
    }

    private static void ReadReplayGainTags(TagLib.File file, SerializableSong song)
    {
        // Try to read ReplayGain from different tag formats
        // ID3v2 tags (MP3)
        if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2Tag)
        {
            foreach (var frame in id3v2Tag.GetFrames<TagLib.Id3v2.TextInformationFrame>())
            {
                var value = frame.Text.FirstOrDefault();
                if (string.IsNullOrEmpty(value))
                    continue;

                // TXXX frames for ReplayGain
                if (frame is TagLib.Id3v2.UserTextInformationFrame userFrame)
                {
                    ParseReplayGainTag(userFrame.Description, userFrame.Text.FirstOrDefault(), song);
                }
            }

            // Also check TXXX frames directly
            foreach (var frame in id3v2Tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
            {
                ParseReplayGainTag(frame.Description, frame.Text.FirstOrDefault(), song);
            }
        }

        // Xiph Comment (Vorbis/FLAC/Opus)
        if (file.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiphTag)
        {
            var trackGain = xiphTag.GetFirstField("REPLAYGAIN_TRACK_GAIN");
            var trackPeak = xiphTag.GetFirstField("REPLAYGAIN_TRACK_PEAK");
            var albumGain = xiphTag.GetFirstField("REPLAYGAIN_ALBUM_GAIN");
            var albumPeak = xiphTag.GetFirstField("REPLAYGAIN_ALBUM_PEAK");

            ParseReplayGainTag("REPLAYGAIN_TRACK_GAIN", trackGain, song);
            ParseReplayGainTag("REPLAYGAIN_TRACK_PEAK", trackPeak, song);
            ParseReplayGainTag("REPLAYGAIN_ALBUM_GAIN", albumGain, song);
            ParseReplayGainTag("REPLAYGAIN_ALBUM_PEAK", albumPeak, song);
        }

        // Apple tags (M4A/AAC)
        if (file.GetTag(TagLib.TagTypes.Apple) is TagLib.Mpeg4.AppleTag appleTag)
        {
            // Apple stores ReplayGain in custom atoms
            // ----:com.apple.iTunes:replaygain_track_gain
            var trackGainItem = appleTag.GetDashBox("com.apple.iTunes", "replaygain_track_gain");
            var trackPeakItem = appleTag.GetDashBox("com.apple.iTunes", "replaygain_track_peak");
            var albumGainItem = appleTag.GetDashBox("com.apple.iTunes", "replaygain_album_gain");
            var albumPeakItem = appleTag.GetDashBox("com.apple.iTunes", "replaygain_album_peak");

            ParseReplayGainTag("REPLAYGAIN_TRACK_GAIN", trackGainItem, song);
            ParseReplayGainTag("REPLAYGAIN_TRACK_PEAK", trackPeakItem, song);
            ParseReplayGainTag("REPLAYGAIN_ALBUM_GAIN", albumGainItem, song);
            ParseReplayGainTag("REPLAYGAIN_ALBUM_PEAK", albumPeakItem, song);
        }
    }

    private static void ParseReplayGainTag(string? tagName, string? value, SerializableSong song)
    {
        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(value))
            return;

        // Normalize tag name for comparison
        var normalizedName = tagName.ToUpperInvariant().Replace(" ", "_", StringComparison.Ordinal);

        // Remove " dB" suffix if present and parse the value
        var cleanValue = value.Replace(" dB", "", StringComparison.OrdinalIgnoreCase)
                              .Replace("dB", "", StringComparison.OrdinalIgnoreCase)
                              .Trim();

        if (!double.TryParse(cleanValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var numValue))
            return;

        if (normalizedName.Contains("REPLAYGAIN_TRACK_GAIN", StringComparison.Ordinal))
        {
            song.ReplayGainTrackGain = numValue;
        }
        else if (normalizedName.Contains("REPLAYGAIN_TRACK_PEAK", StringComparison.Ordinal))
        {
            song.ReplayGainTrackPeak = numValue;
        }
        else if (normalizedName.Contains("REPLAYGAIN_ALBUM_GAIN", StringComparison.Ordinal))
        {
            song.ReplayGainAlbumGain = numValue;
        }
        else if (normalizedName.Contains("REPLAYGAIN_ALBUM_PEAK", StringComparison.Ordinal))
        {
            song.ReplayGainAlbumPeak = numValue;
        }
    }

    private async Task ComputeReplayGainAsync(FullPath filePath, SerializableSong song)
    {
        try
        {
            var result = await replayGainService.AnalyzeTrackAsync(filePath);
            if (result is not null)
            {
                song.ReplayGainTrackGain = result.TrackGain;
                song.ReplayGainTrackPeak = result.TrackPeak;
                logger.LogInformation("Computed ReplayGain for {Path}: Gain={Gain:F2}dB, Peak={Peak:F6}", filePath, result.TrackGain, result.TrackPeak);

                // Write ReplayGain tags back to the file
                WriteReplayGainTags(filePath, result.TrackGain, result.TrackPeak);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to compute ReplayGain for {Path}", filePath);
        }
    }

    private void WriteReplayGainTags(FullPath filePath, double trackGain, double? trackPeak)
    {
        try
        {
            using (var file = TagLib.File.Create(filePath))
            {
                // Format values according to ReplayGain spec
                var trackGainStr = $"{trackGain:F2} dB";
                var trackPeakStr = trackPeak.HasValue ? $"{trackPeak.Value:F6}" : null;

                // Write to ID3v2 tags (MP3)
                if (file.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3v2Tag)
                {
                    // Remove existing ReplayGain frames first
                    RemoveId3v2ReplayGainFrames(id3v2Tag);

                    // Add track gain
                    var trackGainFrame = new TagLib.Id3v2.UserTextInformationFrame("REPLAYGAIN_TRACK_GAIN")
                    {
                        Text = [trackGainStr]
                    };
                    id3v2Tag.AddFrame(trackGainFrame);

                    // Add track peak
                    if (trackPeakStr is not null)
                    {
                        var trackPeakFrame = new TagLib.Id3v2.UserTextInformationFrame("REPLAYGAIN_TRACK_PEAK")
                        {
                            Text = [trackPeakStr]
                        };
                        id3v2Tag.AddFrame(trackPeakFrame);
                    }
                }

                // Write to Xiph Comment (Vorbis/FLAC/Opus)
                if (file.GetTag(TagLib.TagTypes.Xiph, create: true) is TagLib.Ogg.XiphComment xiphTag)
                {
                    xiphTag.SetField("REPLAYGAIN_TRACK_GAIN", trackGainStr);
                    if (trackPeakStr is not null)
                    {
                        xiphTag.SetField("REPLAYGAIN_TRACK_PEAK", trackPeakStr);
                    }
                }

                // Write to Apple tags (M4A/AAC)
                if (file.GetTag(TagLib.TagTypes.Apple, create: true) is TagLib.Mpeg4.AppleTag appleTag)
                {
                    appleTag.SetDashBox("com.apple.iTunes", "replaygain_track_gain", trackGainStr);
                    if (trackPeakStr is not null)
                    {
                        appleTag.SetDashBox("com.apple.iTunes", "replaygain_track_peak", trackPeakStr);
                    }
                }

                file.Save();
            }

            logger.LogDebug("Wrote ReplayGain tags to {Path}", filePath);

            // Verify tags were written correctly
            using (var file = TagLib.File.Create(filePath))
            {
                var song = new SerializableSong { RelativePath = "" };
                ReadReplayGainTags(file, song);

                if (!song.ReplayGainTrackGain.HasValue || Math.Abs(song.ReplayGainTrackGain.Value - trackGain) > 0.01)
                {
                    logger.LogError("Failed to verify ReplayGain Track Gain for {Path}. Expected: {Expected}, Actual: {Actual}", filePath, trackGain, song.ReplayGainTrackGain);
                }

                if (trackPeak.HasValue)
                {
                    if (!song.ReplayGainTrackPeak.HasValue || Math.Abs(song.ReplayGainTrackPeak.Value - trackPeak.Value) > 0.000001)
                    {
                        logger.LogError("Failed to verify ReplayGain Track Peak for {Path}. Expected: {Expected}, Actual: {Actual}", filePath, trackPeak, song.ReplayGainTrackPeak);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write ReplayGain tags to {Path}", filePath);
        }
    }

    private static void RemoveId3v2ReplayGainFrames(TagLib.Id3v2.Tag id3v2Tag)
    {
        var framesToRemove = id3v2Tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>()
            .Where(f => f.Description?.Contains("REPLAYGAIN", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        foreach (var frame in framesToRemove)
        {
            id3v2Tag.RemoveFrame(frame);
        }
    }

    private async Task<FullPath?> ConvertM3uToXspf(IndexerContext context)
    {
        try
        {
            // Check if XSPF version already exists
            var xspfPath = context.Path.ChangeExtension(".xspf");
            if (File.Exists(xspfPath))
            {
                logger.LogDebug("XSPF version already exists for {Path}, skipping conversion", context.Path);
                return null;
            }

            var lines = await File.ReadAllLinesAsync(context.Path);
            var conversionDate = DateTime.UtcNow;

            var trackList = new XElement(XspfNamespace + "trackList");

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                    continue;

                FullPath songPath;
                if (Path.IsPathRooted(trimmedLine))
                {
                    songPath = FullPath.FromPath(trimmedLine);
                }
                else
                {
                    songPath = context.Path.Parent / trimmedLine;
                }

                if (!songPath.IsChildOf(context.Root) && !songPath.Equals(context.Root))
                {
                    logger.LogWarning("Playlist item is outside of music folder: {Path} in playlist {Playlist}", songPath, context.Path);
                    continue;
                }

                // Include all tracks in the XSPF (both existing and missing) so missing tracks can be tracked
                var relativePath = context.CreateRelativePath(songPath);
                var trackElement = new XElement(XspfNamespace + "track",
                    new XElement(XspfNamespace + "location", relativePath),
                    new XElement(XspfNamespace + "extension",
                        new XAttribute("application", MeziantouExtensionNamespace.NamespaceName),
                        new XElement(MeziantouExtensionNamespace + "addedAt", conversionDate.ToString("o", CultureInfo.InvariantCulture))));
                trackList.Add(trackElement);
            }

            var xspfDoc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(XspfNamespace + "playlist",
                    new XAttribute("version", "1"),
                    new XAttribute(XNamespace.Xmlns + "meziantou", MeziantouExtensionNamespace.NamespaceName),
                    new XElement(XspfNamespace + "title", context.Path.NameWithoutExtension),
                    new XElement(XspfNamespace + "date", conversionDate.ToString("o", CultureInfo.InvariantCulture)),
                    trackList));

            await using var stream = new FileStream(xspfPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await xspfDoc.SaveAsync(stream, SaveOptions.None, CancellationToken.None);

            // Backup the original M3U file
            var bakPath = context.Path + ".bak";
            if (File.Exists(bakPath))
            {
                logger.LogDebug("Overwriting existing backup file: {Path}", bakPath);
            }
            File.Move(context.Path, bakPath, overwrite: true);

            logger.LogInformation("Converted M3U playlist to XSPF: {Source} -> {Target}", context.Path, xspfPath);
            return xspfPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error converting M3U playlist to XSPF: {Path}", context.Path);
            return null;
        }
    }

    private async Task ScanXspfPlaylist(IndexerContext context)
    {
        try
        {
            var (playlist, missingItems) = await ParseXspfPlaylist(context);

            context.Catalog.Playlist.Add(playlist);
            context.Catalog.MissingPlaylistItems.AddRange(missingItems);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scanning XSPF playlist file: {Path}", context.Path);
        }
    }

    /// <summary>Compute replay gain for a specific song by ID</summary>
    public async Task<ReplayGainResult?> ComputeSongReplayGainAsync(string songId)
    {
        var song = _catalog.GetSong(songId);
        if (song is null)
            return null;

        var filePath = FullPath.FromPath(song.Path);
        if (!File.Exists(filePath))
            return null;

        var result = await replayGainService.AnalyzeTrackAsync(filePath);
        if (result is not null)
        {
            // Find the serializable song in the cached catalog and update it
            if (_cachedSerializableCatalog is not null)
            {
                // Calculate relative path from the root to find the matching SerializableSong
                var relativePath = Path.GetRelativePath(RootFolder, song.Path);
                var serializableSong = _cachedSerializableCatalog.Songs.FirstOrDefault(s => s.RelativePath.Equals(relativePath, StringComparison.Ordinal));
                if (serializableSong is not null)
                {
                    serializableSong.ReplayGainTrackGain = result.TrackGain;
                    serializableSong.ReplayGainTrackPeak = result.TrackPeak;
                    logger.LogInformation("Computed ReplayGain for {Path}: Gain={Gain:F2}dB, Peak={Peak:F6}", filePath, result.TrackGain, result.TrackPeak);

                    // Update the in-memory catalog as well
                    _catalog = await CreateCatalog(_cachedSerializableCatalog);

                    // Save updated cache to disk
                    var cachePath = GetCacheJsonPath();
                    if (!cachePath.IsEmpty)
                    {
                        try
                        {
                            var json = JsonSerializer.Serialize(_cachedSerializableCatalog, JsonOptions);
                            await File.WriteAllTextAsync(cachePath, json);
                            logger.LogInformation("Updated cached music library at {Path}", cachePath);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error updating cached music library at {Path}", cachePath);
                        }
                    }
                }
            }

            // Write ReplayGain tags back to the file
            WriteReplayGainTags(filePath, result.TrackGain, result.TrackPeak);
        }

        return result;
    }

    // Query methods for compatibility
    public IEnumerable<Artist> GetAllArtists() => _catalog.Artists.OrderBy(a => a.Name, StringComparer.Ordinal);
    public Artist? GetArtist(string id) => _catalog.GetArtist(id);
    public IEnumerable<Album> GetAllAlbums() => _catalog.Albums.OrderBy(a => a.Name, StringComparer.Ordinal);
    public Album? GetAlbum(string id) => _catalog.GetAlbum(id);
    public IEnumerable<Song> GetAllSongs() => _catalog.Songs.OrderBy(s => s.Title, StringComparer.Ordinal);
    public Song? GetSong(string id) => _catalog.GetSong(id);
    public MusicDirectory? GetDirectory(string id) => _catalog.GetDirectory(id);
    public IEnumerable<MusicDirectory> GetAllDirectories() => _catalog.Directories;
    public IEnumerable<MissingPlaylistItem> GetMissingPlaylistItems() => _catalog.MissingPlaylistItems;

    public IEnumerable<Playlist> GetPlaylists()
    {
        // Return regular playlists followed by virtual playlists
        var playlists = _catalog.Playlists.Select(p => p.Value).ToList();

        // Add "All songs" virtual playlist
        var allSongsPlaylist = CreateAllSongsVirtualPlaylist();
        playlists.Insert(0, allSongsPlaylist); // Put it at the beginning

        // Add "Missing tracks" virtual playlist if there are missing items
        var missingTracksPlaylist = CreateMissingTracksVirtualPlaylist();
        if (missingTracksPlaylist.SongCount > 0)
        {
            playlists.Insert(1, missingTracksPlaylist);
        }

        // Add "No Replay Gain" virtual playlist if there are songs without replay gain
        var noReplayGainPlaylist = CreateNoReplayGainVirtualPlaylist();
        if (noReplayGainPlaylist.SongCount > 0)
        {
            playlists.Insert(playlists.Count, noReplayGainPlaylist);
        }

        return playlists;
    }

    public Playlist? GetPlaylist(string id)
    {
        // Check if it's a virtual playlist
        if (Playlist.IsVirtualPlaylist(id))
        {
            if (id == Playlist.AllSongsPlaylistId)
            {
                return CreateAllSongsVirtualPlaylist();
            }

            if (id == Playlist.MissingTracksPlaylistId)
            {
                return CreateMissingTracksVirtualPlaylist();
            }

            if (id == Playlist.NoReplayGainPlaylistId)
            {
                return CreateNoReplayGainVirtualPlaylist();
            }

            // Future virtual playlists can be added here
            return null;
        }

        return _catalog.GetPlaylist(id);
    }

    private Playlist CreateAllSongsVirtualPlaylist()
    {
        var allSongs = _catalog.Songs.OrderBy(s => s.Title, StringComparer.Ordinal).ToList();
        var items = allSongs.Select(song => new PlaylistItem
        {
            Song = song,
            AddedDate = song.Created,
        }).ToList();

        return new Playlist
        {
            Id = Playlist.AllSongsPlaylistId,
            Name = "All Songs",
            Path = string.Empty, // Virtual playlists don't have a file path
            SongCount = items.Count,
            Duration = items.Sum(i => i.Song.Duration),
            Created = DateTime.UtcNow,
            Changed = DateTime.UtcNow,
            CoverArt = items.FirstOrDefault()?.Song.CoverArt,
            Comment = "Virtual playlist containing all songs in the library",
            Items = items,
        };
    }

    private Playlist CreateMissingTracksVirtualPlaylist()
    {
        var missingItems = _catalog.MissingPlaylistItems
            .OrderBy(m => m.PlaylistName, StringComparer.Ordinal)
            .ThenBy(m => m.RelativePath, StringComparer.Ordinal)
            .ToList();

        // Create placeholder songs for missing items
        var items = missingItems.Select(missing =>
        {
            var fileName = Path.GetFileNameWithoutExtension(missing.RelativePath);
            var extension = Path.GetExtension(missing.RelativePath).TrimStart('.').ToLowerInvariant();

            var placeholderSong = new Song
            {
                Id = $"missing:{missing.RelativePath}",
                Title = $"[Missing] {fileName}",
                Path = missing.FullPath,
                Album = $"From playlist: {missing.PlaylistName}",
                Artist = "Missing File",
                AlbumArtist = "Missing File",
                Genre = string.Empty,
                Size = 0,
                ContentType = GetContentTypeForExtension(extension),
                Suffix = extension,
                Duration = 0,
                Created = missing.AddedDate ?? DateTime.MinValue,
            };

            return new PlaylistItem
            {
                Song = placeholderSong,
                AddedDate = missing.AddedDate ?? DateTime.MinValue,
            };
        }).ToList();

        return new Playlist
        {
            Id = Playlist.MissingTracksPlaylistId,
            Name = "⚠️ Missing Tracks",
            Path = string.Empty, // Virtual playlists don't have a file path
            SongCount = items.Count,
            Duration = 0,
            Created = DateTime.UtcNow,
            Changed = DateTime.UtcNow,
            CoverArt = null,
            Comment = "Virtual playlist containing tracks that are referenced in playlists but don't exist locally",
            Items = items,
        };
    }

    private Playlist CreateNoReplayGainVirtualPlaylist()
    {
        var songsWithoutReplayGain = _catalog.Songs
            .Where(s => s.ReplayGainTrackGain is null && s.ReplayGainAlbumGain is null)
            .OrderBy(s => s.Artist, StringComparer.Ordinal)
            .ThenBy(s => s.Album, StringComparer.Ordinal)
            .ThenBy(s => s.Track ?? 0)
            .ThenBy(s => s.Title, StringComparer.Ordinal)
            .ToList();

        var items = songsWithoutReplayGain.Select(song => new PlaylistItem
        {
            Song = song,
            AddedDate = song.Created,
        }).ToList();

        return new Playlist
        {
            Id = Playlist.NoReplayGainPlaylistId,
            Name = "⚠️ No Replay Gain",
            Path = string.Empty,
            SongCount = items.Count,
            Duration = items.Sum(i => i.Song.Duration),
            Created = DateTime.UtcNow,
            Changed = DateTime.UtcNow,
            CoverArt = items.FirstOrDefault()?.Song.CoverArt,
            Comment = "Virtual playlist containing tracks without replay gain information",
            Items = items,
        };
    }

    private static string GetContentTypeForExtension(string extension)
    {
        return extension switch
        {
            "mp3" => "audio/mpeg",
            "flac" => "audio/flac",
            "m4a" => "audio/mp4",
            "wav" => "audio/wav",
            "ogg" => "audio/ogg",
            "opus" => "audio/opus",
            "wma" => "audio/x-ms-wma",
            _ => "audio/mpeg",
        };
    }

    /// <summary>Refreshes a single playlist in the catalog without performing a full library scan.</summary>
    private async Task<Playlist> RefreshPlaylist(FullPath playlistPath)
    {
        // Parse the XSPF file to get playlist data
        var (serializablePlaylist, _) = await ParseXspfPlaylist(new IndexerContext(RootFolder, playlistPath, null!));

        // Update the catalog
        var playlist = _catalog.AddOrUpdatePlaylist(serializablePlaylist);

        // Update the serializable catalog cache
        if (_cachedSerializableCatalog is not null)
        {
            var playlists = _cachedSerializableCatalog.Playlist.ToList();
            playlists.RemoveAll(p => p.RelativePath == serializablePlaylist.RelativePath);
            playlists.Add(serializablePlaylist);
            _cachedSerializableCatalog.Playlist = playlists;
        }

        return playlist;
    }

    /// <summary>Parses an XSPF file and returns a SerializablePlaylist along with any missing playlist items.</summary>
    private static async Task<(SerializablePlaylist Playlist, List<SerializableMissingPlaylistItem> MissingItems)> ParseXspfPlaylist(IndexerContext context)
    {
        await using var stream = new FileStream(context.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var xspfDoc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);

        var playlistElement = xspfDoc.Root;
        if (playlistElement == null || playlistElement.Name.LocalName != "playlist")
        {
            throw new InvalidOperationException("Invalid XSPF file");
        }

        var titleElement = playlistElement.Element(XspfNamespace + "title");
        var annotationElement = playlistElement.Element(XspfNamespace + "annotation");

        var playlist = new SerializablePlaylist
        {
            RelativePath = context.RelativePath,
            Name = titleElement?.Value ?? context.Path.NameWithoutExtension,
            Comment = annotationElement?.Value,
        };

        var missingItems = new List<SerializableMissingPlaylistItem>();

        var trackListElement = playlistElement.Element(XspfNamespace + "trackList");
        if (trackListElement != null)
        {
            foreach (var trackElement in trackListElement.Elements(XspfNamespace + "track"))
            {
                var locationElement = trackElement.Element(XspfNamespace + "location");
                if (locationElement == null)
                    continue;

                var location = locationElement.Value;
                FullPath songPath;
                if (Path.IsPathRooted(location))
                {
                    songPath = FullPath.FromPath(location);
                }
                else
                {
                    songPath = context.Path.Parent / location;
                }

                if (!songPath.IsChildOf(context.Root) && !songPath.Equals(context.Root))
                {
                    continue;
                }

                // Try to get the addedAt from extension
                DateTime? addedDate = null;
                var extensionElement = trackElement.Element(XspfNamespace + "extension");
                if (extensionElement != null)
                {
                    var addedAtElement = extensionElement.Element(MeziantouExtensionNamespace + "addedAt");
                    if (addedAtElement != null &&
                        DateTime.TryParse(addedAtElement.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
                    {
                        addedDate = parsedDate;
                    }
                }

                if (File.Exists(songPath))
                {
                    var playlistItem = new SerializablePlaylistItem
                    {
                        RelativePath = context.CreateRelativePath(songPath),
                        AddedDate = addedDate,
                    };
                    playlist.Items.Add(playlistItem);
                }
                else
                {
                    var missingItem = new SerializableMissingPlaylistItem
                    {
                        RelativePath = context.CreateRelativePath(songPath),
                        PlaylistRelativePath = context.RelativePath,
                        PlaylistName = playlist.Name,
                        AddedDate = addedDate,
                    };
                    missingItems.Add(missingItem);
                }
            }
        }

        return (playlist, missingItems);
    }

    /// <summary>Removes a playlist from the catalog without performing a full library scan.</summary>
    private void RemovePlaylistFromCatalog(string playlistId, string relativePath)
    {
        _catalog.RemovePlaylist(playlistId);

        // Update the serializable catalog cache
        if (_cachedSerializableCatalog is not null)
        {
            _cachedSerializableCatalog.Playlist.RemoveAll(p => p.RelativePath == relativePath);
        }
    }

    public async Task<Playlist> CreatePlaylist(string name, string? comment, List<string> songIds)
    {
        var rootFolder = RootFolder;

        // Generate a unique filename
        var sanitizedName = SanitizeFilename(name);
        var playlistPath = rootFolder / $"{sanitizedName}.xspf";

        // Ensure unique filename
        var counter = 1;
        while (File.Exists(playlistPath))
        {
            playlistPath = rootFolder / $"{sanitizedName}_{counter}.xspf";
            counter++;
        }

        var createdDate = DateTime.UtcNow;
        var trackList = new XElement(XspfNamespace + "trackList");

        foreach (var songId in songIds)
        {
            var song = _catalog.GetSong(songId);
            if (song is not null)
            {
                var relativePath = CreateRelativePathFromRoot(FullPath.FromPath(song.Path));
                var trackElement = new XElement(XspfNamespace + "track",
                    new XElement(XspfNamespace + "location", relativePath),
                    new XElement(XspfNamespace + "extension",
                        new XAttribute("application", MeziantouExtensionNamespace.NamespaceName),
                        new XElement(MeziantouExtensionNamespace + "addedAt", createdDate.ToString("o", CultureInfo.InvariantCulture))));
                trackList.Add(trackElement);
            }
        }

        var xspfDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(XspfNamespace + "playlist",
                new XAttribute("version", "1"),
                new XAttribute(XNamespace.Xmlns + "meziantou", MeziantouExtensionNamespace.NamespaceName),
                new XElement(XspfNamespace + "title", name),
                new XElement(XspfNamespace + "date", createdDate.ToString("o", CultureInfo.InvariantCulture)),
                trackList));

        if (!string.IsNullOrEmpty(comment))
        {
            xspfDoc.Root!.Add(new XElement(XspfNamespace + "annotation", comment));
        }

        playlistPath.CreateParentDirectory();
        await using (var stream = new FileStream(playlistPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await xspfDoc.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
        } // Stream is disposed here before rescan

        logger.LogInformation("Created playlist: {Name} at {Path}", name, playlistPath);

        // Refresh only the new playlist instead of a full library scan
        return await RefreshPlaylist(playlistPath);
    }

    public async Task<Playlist> UpdatePlaylist(string playlistId, string? name, string? comment, List<string>? songIds)
    {
        // Prevent modification of virtual playlists
        if (Playlist.IsVirtualPlaylist(playlistId))
        {
            throw new InvalidOperationException("Cannot modify virtual playlists");
        }

        var playlist = _catalog.GetPlaylist(playlistId);
        if (playlist is null)
        {
            throw new FileNotFoundException("Playlist not found");
        }

        if (!File.Exists(playlist.Path))
        {
            throw new FileNotFoundException("Playlist file not found");
        }

        if (!string.IsNullOrEmpty(name))
        {
            playlist = await RenamePlaylist(playlist.Id, name);
        }

        // Load existing XSPF file
        await using var readStream = new FileStream(playlist.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var xspfDoc = await XDocument.LoadAsync(readStream, LoadOptions.None, CancellationToken.None);
        readStream.Close();

        var playlistElement = xspfDoc.Root;
        if (playlistElement is null || playlistElement.Name.LocalName != "playlist")
        {
            throw new InvalidOperationException("Invalid XSPF file");
        }

        // Update comment if provided
        if (comment is not null)
        {
            var annotationElement = playlistElement.Element(XspfNamespace + "annotation");
            if (string.IsNullOrEmpty(comment))
            {
                annotationElement?.Remove();
            }
            else if (annotationElement is not null)
            {
                annotationElement.Value = comment;
            }
            else
            {
                playlistElement.Add(new XElement(XspfNamespace + "annotation", comment));
            }
        }

        // Update song list if provided
        if (songIds is not null)
        {
            var trackListElement = playlistElement.Element(XspfNamespace + "trackList");
            if (trackListElement is null)
            {
                trackListElement = new XElement(XspfNamespace + "trackList");
                playlistElement.Add(trackListElement);
            }

            // Build a map of existing tracks with their addedAt dates
            var existingTracksMap = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (var trackElement in trackListElement.Elements(XspfNamespace + "track"))
            {
                var locationElement = trackElement.Element(XspfNamespace + "location");
                if (locationElement is not null)
                {
                    var location = locationElement.Value;

                    // Get the addedAt date if present
                    var extensionElement = trackElement.Element(XspfNamespace + "extension");
                    var addedAtElement = extensionElement?.Element(MeziantouExtensionNamespace + "addedAt");
                    if (addedAtElement is not null &&
                        DateTime.TryParse(addedAtElement.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
                    {
                        existingTracksMap[location] = parsedDate;
                    }
                }
            }

            trackListElement.RemoveAll();

            var now = DateTime.UtcNow;
            foreach (var songId in songIds)
            {
                var song = _catalog.GetSong(songId);
                if (song is not null)
                {
                    var relativePath = CreateRelativePathFromRoot(FullPath.FromPath(song.Path));

                    // Preserve existing addedAt date, or use current time for new songs
                    var addedAt = existingTracksMap.GetValueOrDefault(relativePath, now);

                    var trackElement = new XElement(XspfNamespace + "track",
                        new XElement(XspfNamespace + "location", relativePath),
                        new XElement(XspfNamespace + "extension",
                            new XAttribute("application", MeziantouExtensionNamespace.NamespaceName),
                            new XElement(MeziantouExtensionNamespace + "addedAt", addedAt.ToString("o", CultureInfo.InvariantCulture))));
                    trackListElement.Add(trackElement);
                }
            }
        }

        // Update the date
        var dateElement = playlistElement.Element(XspfNamespace + "date");
        var changedDate = DateTime.UtcNow;
        if (dateElement is not null)
        {
            dateElement.Value = changedDate.ToString("o", CultureInfo.InvariantCulture);
        }
        else
        {
            playlistElement.Add(new XElement(XspfNamespace + "date", changedDate.ToString("o", CultureInfo.InvariantCulture)));
        }

        // Write back to file
        await using (var writeStream = new FileStream(playlist.Path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await xspfDoc.SaveAsync(writeStream, SaveOptions.None, CancellationToken.None);
        }

        logger.LogInformation("Updated playlist: {Name} at {Path}", name ?? playlist.Name, playlist.Path);

        // Refresh only the updated playlist instead of a full library scan
        return await RefreshPlaylist(FullPath.FromPath(playlist.Path));
    }

    public async Task DeletePlaylist(string playlistId)
    {
        // Prevent deletion of virtual playlists
        if (Playlist.IsVirtualPlaylist(playlistId))
        {
            throw new InvalidOperationException("Cannot delete virtual playlists");
        }

        var playlist = _catalog.GetPlaylist(playlistId);
        if (playlist is null)
        {
            throw new FileNotFoundException("Playlist not found");
        }

        if (!File.Exists(playlist.Path))
        {
            throw new FileNotFoundException("Playlist file not found");
        }

        var relativePath = CreateRelativePathFromRoot(FullPath.FromPath(playlist.Path));
        File.Delete(playlist.Path);
        logger.LogInformation("Deleted playlist: {Name} at {Path}", playlist.Name, playlist.Path);

        // Remove the playlist from the catalog without a full library scan
        RemovePlaylistFromCatalog(playlistId, relativePath);
    }

    public async Task<Playlist> RenamePlaylist(string playlistId, string newName)
    {
        // Prevent renaming of virtual playlists
        if (Playlist.IsVirtualPlaylist(playlistId))
        {
            throw new InvalidOperationException("Cannot rename virtual playlists");
        }

        var playlist = _catalog.GetPlaylist(playlistId);
        if (playlist is null)
        {
            throw new FileNotFoundException("Playlist not found");
        }

        if (playlist.Name == newName)
        {
            return playlist; // No change needed
        }

        if (!File.Exists(playlist.Path))
        {
            throw new FileNotFoundException("Playlist file not found");
        }

        // Check if a playlist with the same name already exists
        var existingPlaylist = _catalog.Playlists.FirstOrDefault(p =>
            p.Key != playlistId &&
            string.Equals(p.Value.Name, newName, StringComparison.OrdinalIgnoreCase));
        if (existingPlaylist.Value is not null)
        {
            throw new InvalidOperationException("A playlist with this name already exists");
        }

        // Generate the new file path
        var rootFolder = RootFolder;
        var sanitizedName = SanitizeFilename(newName);
        var newPlaylistPath = rootFolder / $"{sanitizedName}.xspf";

        // Check if a file with the same name already exists on disk
        if (File.Exists(newPlaylistPath) && !string.Equals(playlist.Path, newPlaylistPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A playlist file with this name already exists");
        }

        // Load existing XSPF file and update the title
        await using var readStream = new FileStream(playlist.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var xspfDoc = await XDocument.LoadAsync(readStream, LoadOptions.None, CancellationToken.None);
        readStream.Close();

        var playlistElement = xspfDoc.Root;
        if (playlistElement is null || playlistElement.Name.LocalName != "playlist")
        {
            throw new InvalidOperationException("Invalid XSPF file");
        }

        // Update the title in the XSPF file
        var titleElement = playlistElement.Element(XspfNamespace + "title");
        if (titleElement is not null)
        {
            titleElement.Value = newName;
        }
        else
        {
            playlistElement.Add(new XElement(XspfNamespace + "title", newName));
        }

        // Update the date
        var dateElement = playlistElement.Element(XspfNamespace + "date");
        var changedDate = DateTime.UtcNow;
        if (dateElement is not null)
        {
            dateElement.Value = changedDate.ToString("o", CultureInfo.InvariantCulture);
        }
        else
        {
            playlistElement.Add(new XElement(XspfNamespace + "date", changedDate.ToString("o", CultureInfo.InvariantCulture)));
        }

        // Write to the new file path
        await using (var writeStream = new FileStream(newPlaylistPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await xspfDoc.SaveAsync(writeStream, SaveOptions.None, CancellationToken.None);
        }

        // Delete the old file if the path has changed
        if (!string.Equals(playlist.Path, newPlaylistPath, StringComparison.OrdinalIgnoreCase))
        {
            // Remove old playlist from catalog first
            var oldRelativePath = CreateRelativePathFromRoot(FullPath.FromPath(playlist.Path));
            RemovePlaylistFromCatalog(playlistId, oldRelativePath);
            File.Move(playlist.Path, playlist.Path + ".bak", overwrite: false);
        }

        logger.LogInformation("Renamed playlist: {OldName} to {NewName}, file moved from {OldPath} to {NewPath}", playlist.Name, newName, playlist.Path, newPlaylistPath);

        // Refresh only the renamed playlist instead of a full library scan
        return await RefreshPlaylist(newPlaylistPath);
    }

    private static string SanitizeFilename(string filename)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join('_', filename.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? "playlist" : sanitized;
    }

    private string CreateRelativePathFromRoot(FullPath path)
    {
        return path.MakePathRelativeTo(RootFolder).Replace('\\', '/');
    }

    public override void Dispose()
    {
        _scanSemaphore.Dispose();
        base.Dispose();
    }

    public IEnumerable<string> GetGenres() => _catalog.GetGenres();
    public IEnumerable<Song> GetSongsByGenre(string genre) => _catalog.GetSongsByGenre(genre);
    public IEnumerable<Album> GetRandomAlbums(int count) => _catalog.GetRandomAlbums(count);
    public IEnumerable<Album> GetNewestAlbums(int count) => _catalog.GetNewestAlbums(count);
    public IEnumerable<Song> GetRandomSongs(int count) => _catalog.GetRandomSongs(count);
    public (IEnumerable<Artist> artists, IEnumerable<Album> albums, IEnumerable<Song> songs) SearchAll(string query) => _catalog.SearchAll(query);
    public CoverArtData? GetCoverArt(string? id) => _catalog.GetCoverArt(id);
    private static DateTime TruncateMilliseconds(DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);
    }

    private readonly record struct IndexerContext(
        FullPath Root,
        FullPath Path,
        SerializableMusicCatalog Catalog,
        Dictionary<string, SerializableSong>? CachedSongsByPath = null)
    {
        public Dictionary<string, SerializableSong> CachedSongsByPath { get; init; } = CachedSongsByPath ?? new Dictionary<string, SerializableSong>(StringComparer.Ordinal);
        public string RelativePath => CreateRelativePath(Path);

        public string CreateRelativePath(FullPath path) => path.MakePathRelativeTo(Root).Replace('\\', '/');
    }
}