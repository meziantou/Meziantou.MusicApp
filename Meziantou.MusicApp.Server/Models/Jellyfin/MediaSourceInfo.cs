namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class MediaSourceInfo
{
    public string Id { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Container { get; set; }
    public long? Size { get; set; }
    public long? RunTimeTicks { get; set; }
    public List<MediaStream> MediaStreams { get; set; } = [];
}
