namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class CreatePlaylistRequest
{
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public List<string> SongIds { get; set; } = [];
}
