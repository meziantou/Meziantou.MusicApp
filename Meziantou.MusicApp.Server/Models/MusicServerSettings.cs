namespace Meziantou.MusicApp.Server.Models;

public sealed class MusicServerSettings
{
    public string AuthToken { get; set; } = "";
    public string MusicFolderPath { get; set; } = "";
    public string CachePath { get; set; } = "";
    public bool EnableTranscodingCache { get; set; }
    public int CacheRefreshIntervalHours { get; set; } = 24;

    /// <summary>
    /// When true, analyzes tracks that are missing ReplayGain data during library scan
    /// and computes ReplayGain values using FFmpeg.
    /// </summary>
    public bool ComputeMissingReplayGain { get; set; }
}
