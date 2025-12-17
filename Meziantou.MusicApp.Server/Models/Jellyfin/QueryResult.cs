namespace Meziantou.MusicApp.Server.Models.Jellyfin;

public class QueryResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalRecordCount { get; set; }
    public int StartIndex { get; set; }
}
