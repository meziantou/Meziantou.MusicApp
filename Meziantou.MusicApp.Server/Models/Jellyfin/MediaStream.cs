namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class MediaStream
{
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public int? BitRate { get; set; }
    public int? Channels { get; set; }
    public int? SampleRate { get; set; }
    public string Type { get; set; } = string.Empty;
}
