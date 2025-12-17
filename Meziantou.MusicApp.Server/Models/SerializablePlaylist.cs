namespace Meziantou.MusicApp.Server.Models;

internal sealed class SerializablePlaylist
{
    public required string RelativePath { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public List<SerializablePlaylistItem> Items { get; set; } = [];
}
