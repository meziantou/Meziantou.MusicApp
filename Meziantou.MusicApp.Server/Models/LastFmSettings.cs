namespace Meziantou.MusicApp.Server.Models;

public sealed class LastFmSettings
{
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string SessionKey { get; set; } = "";
}
