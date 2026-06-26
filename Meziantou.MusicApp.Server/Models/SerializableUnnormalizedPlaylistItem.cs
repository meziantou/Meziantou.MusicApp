namespace Meziantou.MusicApp.Server.Models;

internal sealed class SerializableUnnormalizedPlaylistItem
{
    /// <summary>The path as stored in the playlist file (may be in a different Unicode normalization form).</summary>
    public required string OriginalRelativePath { get; set; }

    /// <summary>The actual file path on disk after Unicode normalization matching.</summary>
    public required string ResolvedRelativePath { get; set; }

    /// <summary>The last-write time of the resolved file, used to look up the song in the catalog.</summary>
    public DateTime FileLastWriteTime { get; set; }

    public required string PlaylistRelativePath { get; set; }
    public required string PlaylistName { get; set; }
    public DateTime? AddedDate { get; set; }
}
