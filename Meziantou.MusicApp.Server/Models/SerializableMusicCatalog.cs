namespace Meziantou.MusicApp.Server.Models;

internal sealed class SerializableMusicCatalog
{
    /// <summary>
    /// Current version of the cache format. Increment this when adding new properties
    /// or changing the structure to force a rescan of the library.
    /// </summary>
    public const int CacheVersion = 2;

    public int Version { get; set; } = CacheVersion;
    public List<SerializableSong> Songs { get; set; } = [];
    public List<SerializablePlaylist> Playlist { get; set; } = [];
    public List<SerializableMissingPlaylistItem> MissingPlaylistItems { get; set; } = [];
}

