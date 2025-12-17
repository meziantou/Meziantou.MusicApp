using Meziantou.Framework;

namespace Meziantou.MusicApp.Server.Models;

public sealed class CoverArt
{
    public required string Id { get; init; }

    /// <summary>Path of the cover image file. Could be an image file, or a path to the song file if the image is embedded in metadata.</summary>
    public required string FilePath { get; init; }

    /// <summary>Indicate that the cover image is embedded in the song metadata</summary>
    public bool IsMetadata { get; init; }

    /// <summary>Path to the cached cover image file (extracted from metadata or copied from external file)</summary>
    public FullPath CachedFilePath { get; set; }

    /// <summary>Last write time of the source file used to determine if the cache needs to be refreshed</summary>
    public DateTime SourceLastWriteTimeUtc { get; set; }
}
