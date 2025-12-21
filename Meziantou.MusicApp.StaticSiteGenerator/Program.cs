using System.CommandLine;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using CliWrap;
using CliWrap.Buffered;

namespace Meziantou.MusicApp.StaticSiteGenerator;

internal sealed class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly XNamespace XspfNamespace = "http://xspf.org/ns/0/";
    private static readonly XNamespace MeziantouExtensionNamespace = "http://meziantou.net/xspf-extension/1/";

    static async Task<int> Main(string[] args)
    {
        var playlistsOption = new Option<FileInfo[]>("--playlists")
        {
            Description = "XSPF playlist files to process",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };

        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Output folder for static site",
            Required = true,
        };

        var bitrateOption = new Option<int>("--bitrate")
        {
            Description = "Opus bitrate in kbps",
            DefaultValueFactory = _ => 160,
        };

        var rootCommand = new RootCommand("Generate static music site from XSPF playlists")
        {
            playlistsOption,
            outputOption,
            bitrateOption,
        };

        rootCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var playlists = parseResult.GetValue(playlistsOption)!;
            var output = parseResult.GetValue(outputOption)!;
            var bitrate = parseResult.GetValue(bitrateOption);

            await GenerateStaticSite(playlists, output, bitrate);
            return 0;
        });

        ParseResult result = rootCommand.Parse(args);
        return await result.InvokeAsync();
    }

    private static async Task GenerateStaticSite(FileInfo[] playlistFiles, DirectoryInfo outputFolder, int opusBitrate)
    {
        // Create output structure
        var apiFolder = Directory.CreateDirectory(Path.Combine(outputFolder.FullName, "api"));
        var playlistsApiFolder = Directory.CreateDirectory(Path.Combine(apiFolder.FullName, "playlists"));
        var streamFolder = Directory.CreateDirectory(Path.Combine(outputFolder.FullName, "stream"));
        var coversFolder = Directory.CreateDirectory(Path.Combine(outputFolder.FullName, "covers"));

        Console.WriteLine($"Output folder structure created at: {outputFolder.FullName}");

        // Process all playlists
        var processedSongs = new Dictionary<string, SongInfo>(StringComparer.OrdinalIgnoreCase);
        var playlistSummaries = new List<PlaylistSummary>();
        var sortOrder = 0;

        foreach (var playlistFile in playlistFiles)
        {
            if (!playlistFile.Exists)
            {
                Console.WriteLine($"Warning: Playlist file not found: {playlistFile.FullName}");
                continue;
            }

            var summary = await ParseXspfPlaylist(playlistFile, streamFolder, opusBitrate, processedSongs);
            summary.SortOrder = sortOrder;
            playlistSummaries.Add(summary);

            // Save individual playlist JSON
            var playlistTracks = new PlaylistTracksResponse
            {
                Id = summary.Id,
                Name = summary.Name,
                TrackCount = summary.TrackCount,
                Duration = summary.Duration,
                Created = summary.Created,
                Changed = summary.Changed,
                Tracks = summary.Tracks,
            };

            var playlistJsonPath = Path.Combine(playlistsApiFolder.FullName, $"{summary.Id}.json");
            await File.WriteAllTextAsync(playlistJsonPath, JsonSerializer.Serialize(playlistTracks, JsonOptions));

            sortOrder++;
        }

        // Create playlists index
        var playlistsResponse = new PlaylistsResponse
        {
            Playlists = playlistSummaries,
        };

        var playlistsIndexPath = Path.Combine(apiFolder.FullName, "playlists.json");
        await File.WriteAllTextAsync(playlistsIndexPath, JsonSerializer.Serialize(playlistsResponse, JsonOptions));

        Console.WriteLine();
        Console.WriteLine("Static site generation complete!");
        Console.WriteLine($"  Processed playlists: {playlistSummaries.Count}");
        Console.WriteLine($"  Total songs: {processedSongs.Count}");
        Console.WriteLine($"  Output folder: {outputFolder.FullName}");
        Console.WriteLine();
        Console.WriteLine("JSON API endpoints:");
        Console.WriteLine("  GET /api/playlists.json - List all playlists");
        Console.WriteLine("  GET /api/playlists/{id}.json - Get playlist tracks");
        Console.WriteLine();
        Console.WriteLine("Audio streams:");
        Console.WriteLine("  GET /stream/{songId}.opus - Stream audio file");
    }

    private static async Task<PlaylistSummary> ParseXspfPlaylist(
        FileInfo playlistFile,
        DirectoryInfo streamFolder,
        int opusBitrate,
        Dictionary<string, SongInfo> processedSongs)
    {
        Console.WriteLine($"Processing playlist: {playlistFile.Name}");

        var xspf = await XDocument.LoadAsync(
            File.OpenRead(playlistFile.FullName),
            LoadOptions.None,
            CancellationToken.None);

        var playlistElement = xspf.Root;
        if (playlistElement == null || playlistElement.Name.LocalName != "playlist")
        {
            throw new InvalidOperationException($"Invalid XSPF file: {playlistFile.FullName}");
        }

        var titleElement = playlistElement.Element(XspfNamespace + "title");
        var annotationElement = playlistElement.Element(XspfNamespace + "annotation");

        var playlistName = titleElement?.Value ?? Path.GetFileNameWithoutExtension(playlistFile.Name);
        var comment = annotationElement?.Value;

        // Generate playlist ID
        var playlistId = Guid.NewGuid().ToString("N")[..16];

        var tracks = new List<TrackInfo>();
        var totalDuration = 0;

        var trackListElement = playlistElement.Element(XspfNamespace + "trackList");
        if (trackListElement != null)
        {
            foreach (var trackElement in trackListElement.Elements(XspfNamespace + "track"))
            {
                var locationElement = trackElement.Element(XspfNamespace + "location");
                if (locationElement == null)
                    continue;

                var location = locationElement.Value;

                // Resolve path (can be relative or absolute)
                string songPath;
                if (Path.IsPathRooted(location))
                {
                    songPath = location;
                }
                else
                {
                    var playlistDir = playlistFile.Directory!.FullName;
                    songPath = Path.GetFullPath(Path.Combine(playlistDir, location));
                }

                if (!File.Exists(songPath))
                {
                    Console.WriteLine($"  Warning: Song not found: {songPath}");
                    continue;
                }

                // Check if we've already processed this song
                SongInfo songInfo;
                if (processedSongs.TryGetValue(songPath, out var cachedSongInfo))
                {
                    songInfo = cachedSongInfo;
                    Console.WriteLine($"  Using cached song: {Path.GetFileName(songPath)}");
                }
                else
                {
                    // Process new song
                    Console.WriteLine($"  Processing song: {Path.GetFileName(songPath)}");

                    songInfo = await ProcessSong(songPath, streamFolder, opusBitrate);
                    processedSongs[songPath] = songInfo;
                }

                // Get addedAt date if available
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

                // Create track info
                var trackInfo = new TrackInfo
                {
                    Id = songInfo.Id,
                    Title = songInfo.Title,
                    Artists = songInfo.Artists,
                    Album = songInfo.Album,
                    Duration = songInfo.Duration,
                    Track = songInfo.Track,
                    Year = songInfo.Year,
                    Genre = songInfo.Genre,
                    BitRate = songInfo.BitRate,
                    Size = songInfo.Size,
                    ContentType = "audio/ogg",
                    AddedDate = addedDate,
                };

                tracks.Add(trackInfo);
                totalDuration += songInfo.Duration;
            }
        }

        // Create playlist summary
        var created = playlistFile.CreationTimeUtc;
        var changed = playlistFile.LastWriteTimeUtc;

        return new PlaylistSummary
        {
            Id = playlistId,
            Name = playlistName,
            TrackCount = tracks.Count,
            Duration = totalDuration,
            Created = created,
            Changed = changed,
            SortOrder = 0,
            Tracks = tracks,
        };
    }

    private static async Task<SongInfo> ProcessSong(string songPath, DirectoryInfo streamFolder, int opusBitrate)
    {
        // Generate song ID using SHA256 hash
        var songId = ComputeFileHash(songPath);

        // Output path for converted file
        var outputPath = Path.Combine(streamFolder.FullName, $"{songId}.opus");

        // Convert to opus if not already done
        if (!File.Exists(outputPath))
        {
            Console.WriteLine($"    Converting to Opus ({opusBitrate} kbps)...");
            await ConvertToOpus(songPath, outputPath, opusBitrate);
        }

        // Extract metadata using TagLib
        string title;
        string? artists = null;
        string? album = null;
        int? track = null;
        int? year = null;
        string? genre = null;

        try
        {
            using var file = TagLib.File.Create(songPath);
            var tag = file.Tag;

            title = !string.IsNullOrWhiteSpace(tag.Title)
                ? tag.Title
                : Path.GetFileNameWithoutExtension(songPath);

            artists = tag.JoinedPerformers;
            album = tag.Album;
            track = tag.Track > 0 ? (int)tag.Track : null;
            year = tag.Year > 0 ? (int)tag.Year : null;
            genre = tag.JoinedGenres;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Warning: Failed to extract metadata: {ex.Message}");
            title = Path.GetFileNameWithoutExtension(songPath);
        }

        // Get audio properties from converted file
        var duration = await GetAudioDuration(outputPath);
        var bitrate = await GetAudioBitrate(outputPath);
        var size = new FileInfo(outputPath).Length;

        return new SongInfo
        {
            Id = songId,
            Title = title,
            Artists = artists,
            Album = album,
            Duration = duration,
            Track = track,
            Year = year,
            Genre = genre,
            BitRate = bitrate,
            Size = size,
        };
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task ConvertToOpus(string inputPath, string outputPath, int bitrate)
    {
        var result = await Cli.Wrap("ffmpeg")
            .WithArguments(args => args
                .Add("-i").Add(inputPath)
                .Add("-c:a").Add("libopus")
                .Add("-b:a").Add($"{bitrate}k")
                .Add("-vn")
                .Add("-y")
                .Add(outputPath))
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed: {result.StandardError}");
        }
    }

    private static async Task<int> GetAudioDuration(string filePath)
    {
        try
        {
            var result = await Cli.Wrap("ffprobe")
                .WithArguments(args => args
                    .Add("-v").Add("error")
                    .Add("-show_entries").Add("format=duration")
                    .Add("-of").Add("default=noprint_wrappers=1:nokey=1")
                    .Add(filePath))
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            if (double.TryParse(result.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
            {
                return (int)Math.Round(duration);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not get duration for {filePath}: {ex.Message}");
        }

        return 0;
    }

    private static async Task<int?> GetAudioBitrate(string filePath)
    {
        try
        {
            var result = await Cli.Wrap("ffprobe")
                .WithArguments(args => args
                    .Add("-v").Add("error")
                    .Add("-show_entries").Add("format=bit_rate")
                    .Add("-of").Add("default=noprint_wrappers=1:nokey=1")
                    .Add(filePath))
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            if (double.TryParse(result.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var bitrate))
            {
                return (int)Math.Round(bitrate / 1000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not get bitrate for {filePath}: {ex.Message}");
        }

        return null;
    }
}

internal sealed class SongInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Artists { get; init; }
    public string? Album { get; init; }
    public int Duration { get; init; }
    public int? Track { get; init; }
    public int? Year { get; init; }
    public string? Genre { get; init; }
    public int? BitRate { get; init; }
    public long Size { get; init; }
}

internal sealed class PlaylistsResponse
{
    public List<PlaylistSummary> Playlists { get; set; } = [];
}

internal sealed class PlaylistSummary
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int TrackCount { get; set; }
    public int Duration { get; set; }
    public DateTime Created { get; set; }
    public DateTime Changed { get; set; }
    public int SortOrder { get; set; }

    [JsonIgnore]
    public List<TrackInfo> Tracks { get; set; } = [];
}

internal sealed class PlaylistTracksResponse
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int TrackCount { get; set; }
    public int Duration { get; set; }
    public DateTime Created { get; set; }
    public DateTime Changed { get; set; }
    public List<TrackInfo> Tracks { get; set; } = [];
}

internal sealed class TrackInfo
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Artists { get; set; }
    public string? ArtistId { get; set; }
    public string? Album { get; set; }
    public string? AlbumId { get; set; }
    public int Duration { get; set; }
    public int? Track { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public int? BitRate { get; set; }
    public long Size { get; set; }
    public string ContentType { get; set; } = "audio/ogg";
    public DateTime? AddedDate { get; set; }
}
