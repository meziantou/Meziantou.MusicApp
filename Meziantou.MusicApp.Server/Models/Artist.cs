namespace Meziantou.MusicApp.Server.Models;

public sealed class Artist
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<Album> Albums { get; init; } = [];
    public CoverArt? CoverArt { get; set; }
    public int AlbumCount { get; set; }
}
