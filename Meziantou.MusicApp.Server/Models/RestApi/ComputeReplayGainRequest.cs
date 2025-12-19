namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class ComputeReplayGainRequest
{
    /// <summary>The ID of the song to compute replay gain for</summary>
    public required string Id { get; set; }
}
