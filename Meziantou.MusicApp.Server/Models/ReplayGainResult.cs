namespace Meziantou.MusicApp.Server.Models;

public sealed class ReplayGainResult
{
    public double TrackGain { get; init; }
    public double? TrackPeak { get; init; }
}
