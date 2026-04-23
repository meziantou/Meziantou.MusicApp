using Meziantou.Framework;
using Meziantou.Framework.MediaTags;

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

    public void CreateTestMp3File(
        string relativePath,
        string? title = null,
        string? artist = null,
        string? albumArtist = null,
        string? album = null,
        string? genre = null,
        int? year = null,
        uint? track = null,
        string? lyrics = null,
        string? isrc = null,
        double? replayGainTrackGain = null,
        double? replayGainTrackPeak = null)
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

        var tags = new MediaTagInfo
        {
            Title = title,
            Artist = artist,
            AlbumArtist = albumArtist,
            Album = album,
            Genre = genre,
            Year = year,
            TrackNumber = track.HasValue ? (int)track.Value : null,
        };

        if (!string.IsNullOrWhiteSpace(lyrics))
        {
            tags.Lyrics = lyrics;
        }

        if (!string.IsNullOrWhiteSpace(isrc))
        {
            tags.Isrc = isrc;
        }

        if (replayGainTrackGain.HasValue || replayGainTrackPeak.HasValue)
        {
            tags.ReplayGain = new ReplayGainInfo
            {
                TrackGain = replayGainTrackGain,
                TrackPeak = replayGainTrackPeak,
            };
        }

        var writeResult = MediaFile.WriteTags(fullPath, tags);
        if (!writeResult.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to write media tags: {writeResult.ErrorMessage}");
        }
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
