namespace Meziantou.MusicApp.Server.Models.RestApi;

public class ScanStatusResponse
{
    public bool IsScanning { get; set; }
    public bool IsInitialScanCompleted { get; set; }
    public int ScanCount { get; set; }
    public DateTime? LastScanDate { get; set; }
    public double? Percentage { get; set; }
    public TimeSpan? EstimatedCompletionTime { get; set; }
}
