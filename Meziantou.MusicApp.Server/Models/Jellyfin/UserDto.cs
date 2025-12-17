namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class UserDto
{
    public string Name { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public bool HasPassword { get; set; }
    public bool HasConfiguredPassword { get; set; }
    public UserPolicy Policy { get; set; } = new();
}
