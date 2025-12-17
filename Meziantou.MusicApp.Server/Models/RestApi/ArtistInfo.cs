namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class ArtistInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int AlbumCount { get; set; }
}
