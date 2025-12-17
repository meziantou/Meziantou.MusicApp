namespace Meziantou.MusicApp.Server.Models;

public sealed class Album
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Artist { get; init; }
    public required string ArtistId { get; init; }
    public int? Year { get; init; }
    public required string Genre { get; init; }
    public CoverArt? CoverArt { get; set; }
    public int Duration { get; init; }
    public int SongCount { get; init; }
    public DateTime Created { get; init; }
    public List<Song> Songs { get; init; } = [];
}
