namespace Meziantou.MusicApp.Server.Models;

public sealed class MusicDirectory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? ParentId { get; set; }
    public List<Song> Files { get; init; } = [];
    public List<MusicDirectory> SubDirectories { get; init; } = [];
}
