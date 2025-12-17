namespace Meziantou.MusicApp.Server.Models;

/// <summary>
/// Represents a playlist item that references a file that no longer exists locally.
/// </summary>
public sealed class MissingPlaylistItem
{
    /// <summary>
    /// The expected relative path of the missing file.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// The full path where the file was expected to be.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// The name of the playlist that contains this missing item.
    /// </summary>
    public required string PlaylistName { get; init; }

    /// <summary>
    /// The ID of the playlist that contains this missing item.
    /// </summary>
    public required string PlaylistId { get; init; }

    /// <summary>
    /// The date when the item was added to the playlist, if known.
    /// </summary>
    public DateTime? AddedDate { get; init; }
}
