namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class AlbumInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string ArtistId { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string Genre { get; set; } = string.Empty;
    public int Duration { get; set; }
    public int SongCount { get; set; }
    public DateTime Created { get; set; }
}
