namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class JellyfinSettings
{
    public string ServerId { get; set; } = Guid.NewGuid().ToString("N");
    public string ServerName { get; set; } = "Meziantou Music Server";
    public string Version { get; set; } = "10.9.0";
    public string DefaultUserId { get; set; } = "default-user-id";
    public string DefaultUserName { get; set; } = "user";
}
