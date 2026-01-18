namespace Meziantou.MusicApp.Server.Models;

internal sealed class SerializablePlaylistItem
{
    public required string RelativePath { get; set; }
    public DateTime? AddedDate { get; set; }
    public DateTime FileLastWriteTime { get; set; }
}
