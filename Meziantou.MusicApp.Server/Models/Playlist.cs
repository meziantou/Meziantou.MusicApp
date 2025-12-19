namespace Meziantou.MusicApp.Server.Models;

public sealed class Playlist
{
    private const string VirtualPlaylistPrefix = "virtual:";
    public const string AllSongsPlaylistId = "virtual:all-songs";
    public const string MissingTracksPlaylistId = "virtual:missing-tracks";
    public const string NoReplayGainPlaylistId = "virtual:no-replay-gain";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public int SongCount { get; set; }
    public int Duration { get; set; }
    public DateTime Created { get; init; }
    public DateTime Changed { get; init; }
    public CoverArt? CoverArt { get; set; }
    public string? Comment { get; set; }
    public List<PlaylistItem> Items { get; set; } = [];

    public static bool IsVirtualPlaylist(string playlistId) => playlistId.StartsWith(VirtualPlaylistPrefix, StringComparison.Ordinal);
}
