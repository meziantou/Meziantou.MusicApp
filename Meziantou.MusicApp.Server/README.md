# Meziantou MusicServer

This project is a music streaming server that supports both Subsonic and Jellyfin APIs. It allows you to stream your music collection over the network, with features like on-the-fly transcoding, Last.fm scrobbling, and ReplayGain support.

## Features

- Dual API Support: Subsonic REST API (v1.16.1) and Jellyfin API (v10.9)
- Automatic M3U/M3U8 playlist detection
- ID3 tag reading using TagLibSharp
- Lyrics support from file metadata
- Cover art extraction from files or folders
- Simple token-based authentication
- Single-user mode (hardcoded token)
- Audio transcoding with FFmpeg support
- Last.fm Scrobbling: Send play data to Last.fm
- ReplayGain Support: Read and compute ReplayGain for volume normalization

## Running the Application

### Prerequisites

- .NET SDK (check global.json for the minimum version)

### Build and Run

```powershell
dotnet restore
dotnet build
dotnet run
```

The server will be available at:
- **Subsonic API**: `http://localhost:5000/rest/...`
- **Jellyfin API**: `http://localhost:5000/jellyfin/...`
- **Custom API**: `http://localhost:5000/api/...`, `http://localhost:5000/openapi/v1.json`

### Configuration

Configure FFmpeg path in `appsettings.json` (optional, defaults to system PATH):

```json
{
  "FFmpeg": {
    "Path": "ffmpeg",
    "MaxConcurrentTranscodes": 5,
    "MaxConcurrentReplayGainAnalysis": 2
  }
}
```

## Playlist Support

The API automatically detects and parses M3U and M3U8 playlist files in the music directory.

## Lyrics Support

The API supports lyrics from two sources (in order of priority):

1. **External LRC files** - `.lrc` files with the same name as the audio file
2. **Embedded metadata** - Lyrics stored in audio file tags (ID3, Vorbis comments, etc.)

Both sources are automatically detected and extracted during library scans.

## ReplayGain Support

ReplayGain is a standard for measuring and storing audio loudness information. The server supports reading existing ReplayGain tags from audio files and can optionally compute them during library scans using FFmpeg.

### Features

- **Read ReplayGain tags** from multiple formats:
  - ID3v2 tags (MP3): `TXXX:REPLAYGAIN_TRACK_GAIN`, etc.
  - Vorbis Comments (FLAC/OGG/Opus): `REPLAYGAIN_TRACK_GAIN`, etc.
  - Apple tags (M4A/AAC): `----:com.apple.iTunes:replaygain_track_gain`, etc.
- **Compute missing ReplayGain** using FFmpeg's loudnorm filter (EBU R128)

### Configuration

Enable automatic ReplayGain computation in `appsettings.json`:

```json
{
  "MusicServer": {
    "ComputeMissingReplayGain": true
  },
  "FFmpeg": {
    "Path": "ffmpeg",
    "MaxConcurrentReplayGainAnalysis": 2
  }
}
```

- `ComputeMissingReplayGain`: When `true`, tracks without ReplayGain tags will be analyzed during library scans (default: `false`)
- `MaxConcurrentReplayGainAnalysis`: Maximum number of concurrent FFmpeg analysis processes (default: `2`)

## Last.fm Scrobbling

The server supports scrobbling (sending track play data) to Last.fm. This allows you to track your listening history on Last.fm.

### Configuration

Add your Last.fm credentials to `appsettings.json`:

```json
{
  "LastFm": {
    "ApiKey": "your-api-key",
    "ApiSecret": "your-api-secret",
    "SessionKey": "your-session-key"
  }
}
```

## References

- [Subsonic API Documentation](https://subsonic.org/pages/api.jsp)
- [Jellyfin API Documentation](https://api.jellyfin.org/)
- [Last.fm API Documentation](https://www.last.fm/api/)
