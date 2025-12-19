namespace Meziantou.MusicApp.Server.Models;

// Create an application: https://www.last.fm/api/account/create
// List applications: https://www.last.fm/api/accounts
// Generate session key: https://dullmace.github.io/lastfm-sessionkey/
public sealed class LastFmSettings
{
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string SessionKey { get; set; } = ""; 
}
