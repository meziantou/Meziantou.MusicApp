namespace Meziantou.MusicApp.Server.Models;

/// <summary>
/// Represents a playlist item whose file path was resolved by normalizing Unicode (NFC/NFD),
/// meaning the path stored in the playlist did not match the actual file name on disk.
/// </summary>
public sealed class UnnormalizedPlaylistItem
{
    /// <summary>The path as stored in the playlist file.</summary>
    public required string OriginalRelativePath { get; init; }

    /// <summary>The actual file path on disk after normalization matching.</summary>
    public required string ResolvedRelativePath { get; init; }

    /// <summary>The song resolved from the normalized path.</summary>
    public required Song Song { get; init; }

    /// <summary>The name of the playlist that contains this item.</summary>
    public required string PlaylistName { get; init; }

    /// <summary>The ID of the playlist that contains this item.</summary>
    public required string PlaylistId { get; init; }

    /// <summary>The date when the item was added to the playlist, if known.</summary>
    public DateTime? AddedDate { get; init; }
}
