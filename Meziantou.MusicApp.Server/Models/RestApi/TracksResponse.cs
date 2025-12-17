namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class TracksResponse
{
    public List<TrackInfo> Tracks { get; set; } = [];
}
