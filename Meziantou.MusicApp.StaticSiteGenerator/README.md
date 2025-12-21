# Meziantou.MusicApp.StaticSiteGenerator

A console application that generates static files from XSPF playlists to create a read-only music streaming API.

## Features

- Parses XSPF playlist files and extracts metadata
- Converts all music files to Opus format (default 160kbps) using ffmpeg
- Generates static JSON files matching the REST API schema
- Deduplicates songs across multiple playlists
- Preserves playlist metadata including creation dates and track order

## Usage

```bash
dotnet run --project Meziantou.MusicApp.StaticSiteGenerator -- --playlists playlist1.xspf playlist2.xspf --output ./output --bitrate 160
```

Or after building:

```bash
Meziantou.MusicApp.StaticSiteGenerator.exe --playlists playlist1.xspf playlist2.xspf --output ./output --bitrate 160
```

### Options

- `-p, --playlists` (required): One or more XSPF playlist files to process
- `-o, --output` (required): Output folder for the static site
- `-b, --bitrate` (optional): Opus bitrate in kbps (default: 160)

### Examples

```bash
# Single playlist
dotnet run -- -p music.xspf -o ./static-site

# Multiple playlists
dotnet run -- -p rock.xspf jazz.xspf classical.xspf -o ./static-site

# Custom bitrate
dotnet run -- -p music.xspf -o ./static-site -b 128
```

## Output Structure

The application creates the following folder structure:

```
output/
├── api/
│   ├── playlists.json          # List of all playlists
│   └── playlists/
│       ├── {id}.json           # Individual playlist with tracks
│       └── ...
├── stream/
│   ├── {songId}.opus           # Converted audio files
│   └── ...
└── covers/                     # (Reserved for future use)
```

## Requirements

- .NET 9.0 or later
- ffmpeg (must be in PATH)
- ffprobe (usually included with ffmpeg)

## Serving the Static Site

The generated static files can be served by any web server:

### Using nginx

```nginx
server {
    listen 80;
    server_name music.example.com;
    root /path/to/output;

    location /api/ {
        add_header Content-Type application/json;
    }

    location /stream/ {
        add_header Content-Type audio/ogg;
    }
}
```
