namespace Meziantou.MusicApp.Server.Models;

internal sealed class SerializableMissingPlaylistItem
{
    public required string RelativePath { get; set; }
    public required string PlaylistRelativePath { get; set; }
    public required string PlaylistName { get; set; }
    public DateTime? AddedDate { get; set; }
}
