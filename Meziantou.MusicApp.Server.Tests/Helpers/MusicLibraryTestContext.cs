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

    public void CreateTestMp3FileWithReplayGain(string relativePath, string title, string? artist, string? albumArtist, string album, string genre, int year, uint track, double trackGain, double trackPeak)
    {
        CreateTestMp3File(relativePath, title, artist, albumArtist, album, genre, year, track);

        var fullPath = root / relativePath;
        using var tagFile = TagLib.File.Create(fullPath);

        var trackGainStr = $"{trackGain:F2} dB";
        var trackPeakStr = $"{trackPeak:F6}";

        if (tagFile.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3v2Tag)
        {
            var trackGainFrame = new TagLib.Id3v2.UserTextInformationFrame("REPLAYGAIN_TRACK_GAIN")
            {
                Text = [trackGainStr]
            };
            id3v2Tag.AddFrame(trackGainFrame);

            var trackPeakFrame = new TagLib.Id3v2.UserTextInformationFrame("REPLAYGAIN_TRACK_PEAK")
            {
                Text = [trackPeakStr]
            };
            id3v2Tag.AddFrame(trackPeakFrame);
        }

        tagFile.Save();
    }

    public void CreateTestMp3FileWithIsrc(string relativePath, string title, string? artist, string? albumArtist, string album, string genre, int year, uint track, string isrc)
    {
        CreateTestMp3File(relativePath, title, artist, albumArtist, album, genre, year, track);

        var fullPath = root / relativePath;
        using var tagFile = TagLib.File.Create(fullPath);

        // Add ISRC to ID3v2 tag using TSRC frame
        if (tagFile.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3v2Tag)
        {
            var tsrcFrame = TagLib.Id3v2.TextInformationFrame.Get(id3v2Tag, "TSRC", true);
            tsrcFrame.Text = [isrc];
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
