namespace Meziantou.MusicApp.Server.Models.RestApi;

public class InvalidPlaylistInfo
{
    public required string Path { get; set; }
    public required string ErrorMessage { get; set; }
}
