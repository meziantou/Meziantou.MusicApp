namespace Meziantou.MusicApp.Server.Models;

internal sealed class SerializableMusicCatalog
{
    /// <summary>
    /// Current version of the cache format. Increment this when making incompatible
    /// changes or adding properties that require a rescan of the library.
    /// </summary>
    public const int CacheVersion = 5;

    public int Version { get; set; } = CacheVersion;
    public DateTime? LastScanDate { get; set; }
    public List<SerializableSong> Songs { get; set; } = [];
    public List<SerializablePlaylist> Playlist { get; set; } = [];
    public List<SerializableMissingPlaylistItem> MissingPlaylistItems { get; set; } = [];
    public List<SerializableInvalidPlaylist> InvalidPlaylists { get; set; } = [];
    public List<SerializableUnnormalizedPlaylistItem> UnnormalizedPlaylistItems { get; set; } = [];
}
