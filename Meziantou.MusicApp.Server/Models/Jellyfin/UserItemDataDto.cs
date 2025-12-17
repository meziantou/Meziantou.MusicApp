namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class UserItemDataDto
{
    public double PlaybackPositionTicks { get; set; }
    public int PlayCount { get; set; }
    public bool IsFavorite { get; set; }
    public bool Played { get; set; }
}
