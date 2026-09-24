# Meziantou Music App

This repository contains a complete music streaming solution consisting of a backend server and a frontend web player.

## Projects

### [Meziantou.MusicApp.Server](Meziantou.MusicApp.Server/README.md)

A dual-API music server supporting both **Subsonic** and **Jellyfin** protocols. It serves music files from a local directory and provides features like:
- Dual API support (Subsonic & Jellyfin)
- On-the-fly transcoding with FFmpeg
- ReplayGain support
- Read-only API surface with manual library rescan and transcoding cache cleanup support

### [Meziantou.MusicApp.WebPlayer](Meziantou.MusicApp.WebPlayer/README.md)

A modern, web-based music player designed to work with the server. Features include:
- Progressive Web App (PWA) support
- Offline mode with caching
- Dark theme
- Background sync and auto-resume

### [Meziantou.MusicApp.MacPlayer](Meziantou.MusicApp.MacPlayer/README.md)

A native macOS player built with SwiftUI and AVFoundation, with the same features as the web player:
- Offline playlists and gapless playback
- Media keys, Now Playing, and AirPlay output selection
- Persistent playing queue and auto-resume

### [Meziantou.MusicApp.iOSPlayer](Meziantou.MusicApp.iOSPlayer/README.md)

A native iPhone and iPad player built with SwiftUI and AVFoundation:
- Offline playlist downloads using the same local cache format as the macOS player
- Background playback with lock-screen and Control Center controls
- Direct deployment from Xcode to iOS/iPadOS 18 or later

## Getting Started

Please refer to the individual project READMEs for detailed instructions on how to build, configure, and run each component.

- [Server Documentation](Meziantou.MusicApp.Server/README.md)
- [Web Player Documentation](Meziantou.MusicApp.WebPlayer/README.md)
- [macOS Player Documentation](Meziantou.MusicApp.MacPlayer/README.md)
- [iOS and iPadOS Player Documentation](Meziantou.MusicApp.iOSPlayer/README.md)
