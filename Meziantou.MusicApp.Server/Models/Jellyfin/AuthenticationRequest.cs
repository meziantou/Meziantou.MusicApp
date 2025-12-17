namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class AuthenticationRequest
{
    public string Username { get; set; } = string.Empty;
    public string Pw { get; set; } = string.Empty;
}
