namespace Meziantou.MusicApp.Server.Models;

public sealed class PlaylistItem
{
    public required Song Song { get; set; }
    public DateTime AddedDate { get; set; }
}
