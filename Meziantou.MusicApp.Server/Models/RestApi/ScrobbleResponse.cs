namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class ScrobbleResponse
{
    /// <summary>
    /// Indicates whether the scrobble was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The title of the scrobbled track
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The artist of the scrobbled track
    /// </summary>
    public string? Artist { get; set; }

    /// <summary>
    /// Message providing additional details
    /// </summary>
    public string? Message { get; set; }
}
