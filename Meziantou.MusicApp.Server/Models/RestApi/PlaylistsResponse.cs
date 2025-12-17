namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class PlaylistsResponse
{
    public List<PlaylistSummary> Playlists { get; set; } = [];
}
