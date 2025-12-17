namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class UserPolicy
{
    public bool IsAdministrator { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDisabled { get; set; }
    public bool EnableAllFolders { get; set; } = true;
}
