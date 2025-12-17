namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class AuthenticationResult
{
    public UserDto User { get; set; } = new();
    public string AccessToken { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
}
