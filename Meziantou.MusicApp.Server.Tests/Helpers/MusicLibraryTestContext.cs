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

        // Use TagLibSharp to add ID3 tags
        using var tagFile = TagLib.File.Create(fullPath);
        
        if (title != null)
        {
            tagFile.Tag.Title = title;
        }

        if (album != null)
        {
            tagFile.Tag.Album = album;
        }

        if (genre != null)
        {
            tagFile.Tag.Genres = [genre];
        }

        if (year.HasValue)
        {
            tagFile.Tag.Year = (uint)year.Value;
        }

        if (track.HasValue)
        {
            tagFile.Tag.Track = track.Value;
        }

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

        // Add ISRC if provided
        if (isrc != null)
        {
            if (tagFile.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3v2Tag)
            {
                var tsrcFrame = TagLib.Id3v2.TextInformationFrame.Get(id3v2Tag, "TSRC", true);
                tsrcFrame.Text = [isrc];
            }
        }

        // Add ReplayGain if provided
        if (replayGainTrackGain.HasValue && replayGainTrackPeak.HasValue)
        {
            var trackGainStr = $"{replayGainTrackGain.Value:F2} dB";
            var trackPeakStr = $"{replayGainTrackPeak.Value:F6}";

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
