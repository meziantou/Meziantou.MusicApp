namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class BaseItemDto
{
    public string Name { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? AlbumArtist { get; set; }
    public string? Album { get; set; }
    public List<string> Artists { get; set; } = [];
    public string? AlbumId { get; set; }
    public string? ParentId { get; set; }
    public DateTime? PremiereDate { get; set; }
    public int? ProductionYear { get; set; }
    public int? IndexNumber { get; set; }
    public long? RunTimeTicks { get; set; }
    public List<string> Genres { get; set; } = [];
    public UserItemDataDto? UserData { get; set; }
    public string? ImageTags { get; set; }
    public Dictionary<string, string> ImageBlurHashes { get; set; } = [];
    public string? MediaType { get; set; }
    public List<MediaSourceInfo> MediaSources { get; set; } = [];
    public int? ChildCount { get; set; }
    public string? SortName { get; set; }
}
