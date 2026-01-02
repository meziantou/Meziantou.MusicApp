namespace Meziantou.MusicApp.Server.Models;

public sealed class InvalidPlaylist
{
    public required string Path { get; init; }
    public required string ErrorMessage { get; init; }
}
