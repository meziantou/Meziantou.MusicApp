namespace Meziantou.MusicApp.Server.Models;

internal sealed class SerializableSong
{
    public required string RelativePath { get; set; }
    public long FileSize { get; set; }
    public DateTime FileCreatedAt { get; set; }
    public DateTime FileLastWriteTime { get; set; }
    public string? Title { get; set; }
    public string? AlbumName { get; set; }
    public string? Artist { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Genre { get; set; }
    public int Year { get; set; }
    public int Track { get; set; }
    public TimeSpan Duration { get; set; }
    public int BitRate { get; set; }
    public string? Lyrics { get; set; }
    public string? ExternalLyricsPath { get; set; }
    public bool HasEmbeddedCover { get; set; }
    public string? ExternalCoverArtPath { get; set; }
    public string? Isrc { get; set; }

    // ReplayGain values
    public double? ReplayGainTrackGain { get; set; }
    public double? ReplayGainTrackPeak { get; set; }
    public double? ReplayGainAlbumGain { get; set; }
    public double? ReplayGainAlbumPeak { get; set; }
}
