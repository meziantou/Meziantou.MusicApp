namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class ComputeReplayGainResponse
{
    /// <summary>Whether the computation was successful</summary>
    public required bool Success { get; set; }

    /// <summary>The ID of the song</summary>
    public required string Id { get; set; }

    /// <summary>The title of the song</summary>
    public required string Title { get; set; }

    /// <summary>The computed track gain in dB (if successful)</summary>
    public double? TrackGain { get; set; }

    /// <summary>The computed track peak (if successful)</summary>
    public double? TrackPeak { get; set; }

    /// <summary>Message describing the result</summary>
    public string? Message { get; set; }
}
