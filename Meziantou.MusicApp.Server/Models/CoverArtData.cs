using System.Diagnostics.CodeAnalysis;

namespace Meziantou.MusicApp.Server.Models;

/// <summary>Represents the data and metadata for a cover art image.</summary>
public sealed class CoverArtData
{
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "This is a data transfer object representing binary image data")]
    public required byte[] Data { get; init; }
    public required DateTimeOffset LastModified { get; init; }
}
