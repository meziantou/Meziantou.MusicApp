namespace Meziantou.MusicApp.Server.Models.RestApi;

public sealed class CacheCleanupResponse
{
    public int DeletedFileCount { get; set; }
    public int FailedFileCount { get; set; }
}
