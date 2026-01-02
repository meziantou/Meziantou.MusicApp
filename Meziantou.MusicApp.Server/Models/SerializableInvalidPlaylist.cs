namespace Meziantou.MusicApp.Server.Models;

internal sealed class SerializableInvalidPlaylist
{
    public string RelativePath { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}
