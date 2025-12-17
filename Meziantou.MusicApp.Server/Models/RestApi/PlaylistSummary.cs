namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class PlaylistSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public int Duration { get; set; }
    public DateTime Created { get; set; }
    public DateTime Changed { get; set; }
    public int SortOrder { get; set; }
}
