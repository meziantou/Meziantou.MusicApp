namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class TrackInfo
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Artists { get; set; }
    public string? ArtistId { get; set; }
    public string? Album { get; set; }
    public string? AlbumId { get; set; }
    public int Duration { get; set; }
    public int? Track { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public int? BitRate { get; set; }
    public long Size { get; set; }
    public string? ContentType { get; set; }
    public DateTime? AddedDate { get; set; }

    // ReplayGain values (in dB for gain, linear for peak)
    public double? ReplayGainTrackGain { get; set; }
    public double? ReplayGainTrackPeak { get; set; }
    public double? ReplayGainAlbumGain { get; set; }
    public double? ReplayGainAlbumPeak { get; set; }
}
