namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class AddTrackToPlaylistRequest
{
    public required string SongId { get; set; }
}
