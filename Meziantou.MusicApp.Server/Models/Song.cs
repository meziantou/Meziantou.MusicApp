namespace Meziantou.MusicApp.Server.Models;

public sealed class Song
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Path { get; init; }
    public required string Album { get; init; }
    public string? AlbumId { get; set; }
    public required string Artist { get; init; }
    public string? ArtistId { get; set; }
    public required string AlbumArtist { get; init; }
    public int? Track { get; init; }
    public int? Year { get; init; }
    public required string Genre { get; init; }
    public long Size { get; init; }
    public required string ContentType { get; init; }
    public required string Suffix { get; init; }
    public int Duration { get; init; }
    public int? BitRate { get; init; }
    public DateTime Created { get; init; }
    public string? ParentId { get; set; }
    public CoverArt? CoverArt { get; set; }
    public Lyrics? Lyrics { get; init; }
    public string? Isrc { get; init; }

    // ReplayGain values (in dB for gain, linear for peak)
    public double? ReplayGainTrackGain { get; init; }
    public double? ReplayGainTrackPeak { get; init; }
    public double? ReplayGainAlbumGain { get; init; }
    public double? ReplayGainAlbumPeak { get; init; }
}