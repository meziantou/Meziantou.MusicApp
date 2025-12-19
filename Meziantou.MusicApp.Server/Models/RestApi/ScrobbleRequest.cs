namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class ScrobbleRequest
{
    /// <summary>The ID of the song to scrobble</summary>
    public required string Id { get; set; }

    /// <summary>If true, submits as a completed play. If false (default), updates "Now Playing" status.</summary>
    public bool Submission { get; set; }
}
