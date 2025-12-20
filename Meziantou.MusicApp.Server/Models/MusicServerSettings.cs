namespace Meziantou.MusicApp.Server.Models;

public sealed class MusicServerSettings
{
    public string AuthToken { get; set; } = "";
    public string MusicFolderPath { get; set; } = "";
    public string CachePath { get; set; } = "";
    public bool EnableTranscodingCache { get; set; }
    public TimeSpan CacheRefreshInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// When true, analyzes tracks that are missing ReplayGain data during library scan
    /// and computes ReplayGain values using FFmpeg.
    /// </summary>
    public bool ComputeMissingReplayGain { get; set; }

    /// <summary>
    /// Maximum number of files to process in parallel during music library scan.
    /// Defaults to the number of processors if not specified or set to 0.
    /// </summary>
    public int MaxDegreeOfParallelismForScan { get; set; }
}
