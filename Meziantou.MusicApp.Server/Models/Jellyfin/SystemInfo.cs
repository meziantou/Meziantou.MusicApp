namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class SystemInfo
{
    public string ServerName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public bool HasPendingRestart { get; set; }
    public bool SupportsLibraryMonitor { get; set; }
}
