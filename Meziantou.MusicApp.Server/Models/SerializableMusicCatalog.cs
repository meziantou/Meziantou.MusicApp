namespace Meziantou.MusicApp.Server.Models;

internal sealed class SerializableMusicCatalog
{
    public List<SerializableSong> Songs { get; set; } = [];
    public List<SerializablePlaylist> Playlist { get; set; } = [];
    public List<SerializableMissingPlaylistItem> MissingPlaylistItems { get; set; } = [];
}
