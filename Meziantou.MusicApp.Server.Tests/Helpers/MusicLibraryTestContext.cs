using Meziantou.Framework;

namespace Meziantou.MusicApp.Server.Tests.Helpers;

internal sealed class MusicLibraryTestContext(FullPath root)
{
    public FullPath RootPath => root;

    public void AddFolder(string relativePath)
    {
        Directory.CreateDirectory(root / relativePath);
    }

    public void AddFile(string relativePath, byte[] content)
    {
        File.WriteAllBytes(root / relativePath, content);
    }

    public void CreateTestMp3File(string relativePath, string title, string? artist, string? albumArtist, string album, string genre, int year, uint track, string? lyrics = null)
    {
        // Create a minimal valid MP3 file
        ReadOnlySpan<byte> mp3Data =
        [
            // ID3v2.3 header
            0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // MP3 frame header
            0xFF, 0xFB, 0x90, 0x00,
            // Minimal MP3 frame data
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

        var fullPath = root / relativePath;
        fullPath.CreateParentDirectory();
        File.WriteAllBytes(fullPath, mp3Data);

        // Use TagLibSharp to add ID3 tags
        using var tagFile = TagLib.File.Create(fullPath);
        tagFile.Tag.Title = title;
        tagFile.Tag.Album = album;
        tagFile.Tag.Genres = [genre];
        tagFile.Tag.Year = (uint)year;
        tagFile.Tag.Track = track;

        if (artist != null)
        {
            tagFile.Tag.Performers = [artist];
        }

        if (albumArtist != null)
        {
            tagFile.Tag.AlbumArtists = [albumArtist];
        }

        if (lyrics != null)
        {
            tagFile.Tag.Lyrics = lyrics;
        }

        tagFile.Save();
    }

    public async Task CreateLrcFile(string relativePath, string content)
    {
        var fullPath = root / relativePath;
        fullPath.CreateParentDirectory();
        await File.WriteAllTextAsync(fullPath, content);
    }

    public async Task CreatePlaylistFile(string relativePath, string content)
    {
        var fullPath = root / relativePath;
        fullPath.CreateParentDirectory();
        await File.WriteAllTextAsync(fullPath, content);
    }
}
