namespace Meziantou.MusicApp.Server.Models.RestApi;

public class ScanStatusResponse
{
    public bool IsScanning { get; set; }
    public bool IsInitialScanCompleted { get; set; }
    public int ScanCount { get; set; }
    public DateTime? LastScanDate { get; set; }
    public double? Percentage { get; set; }
    public TimeSpan? EstimatedCompletionTime { get; set; }
    public int? ProcessedFiles { get; set; }
    public int? TotalFiles { get; set; }
    public int? ProcessedPlaylists { get; set; }
    public int? TotalPlaylists { get; set; }
    public long ActiveScanGeneration { get; set; }
    public long LastCompletedScanGeneration { get; set; }
    public List<InvalidPlaylistInfo> InvalidPlaylists { get; set; } = [];
}
