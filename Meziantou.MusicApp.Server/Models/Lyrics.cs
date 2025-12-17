namespace Meziantou.MusicApp.Server.Models;

public sealed class Lyrics
{
    public required string Id { get; init; }

    /// <summary>Path of the lyrics file. Could be a .lrc file, or a path to the song file if the lyrics are embedded in metadata.</summary>
    public required string FilePath { get; init; }

    /// <summary>Indicate that the lyrics are embedded in the song metadata</summary>
    public bool IsMetadata { get; init; }
}
