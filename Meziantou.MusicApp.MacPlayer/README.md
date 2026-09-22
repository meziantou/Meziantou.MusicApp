# Meziantou.MusicApp.MacPlayer

A native macOS player (Swift, SwiftUI and AVFoundation) for the Meziantou Music Server. It has the same features as the [web player](../Meziantou.MusicApp.WebPlayer/README.md).

## Features

- Playlists in the sidebar with track count, duration, optional file size, and a playing indicator
- Track list for playlists with thousands of tracks (native table), with sortable columns (title, artist, album, added date)
- Accent-insensitive and case-insensitive search on title, artist, album and ISRC (⌘F)
- Context menu: play, add to queue, download / remove download, download the raw file, copy the file path, view details. Works on multiple selected tracks.
- Player bar: play/pause, previous/next, shuffle, repeat (off/all/one), seek bar, elapsed/remaining time, volume up to 200%, mute. The mouse wheel or trackpad adjusts the volume (5% per notch) and seeks (5 seconds per notch) when over those sliders
- ReplayGain (track/album), and warnings for tracks without ReplayGain data
- Gapless playback: the next track is preloaded near the end of the current one and scheduled right after it
- Playing queue in the inspector: "Now Playing", "Next Up" (manually added) and "Next from: playlist" sections; double-click to jump, drag to reorder, remove items
- The queue, the current track and position, the volume, shuffle and repeat are restored when the app starts; playback resumes if it was playing
- Offline mode: download playlists for offline use (with progress), downloaded tracks are marked, unavailable tracks are dimmed when offline
- Low Data Mode (constrained or expensive networks) uses the low data quality and can prevent streaming
- Audio output selection, including AirPlay speakers
- Now Playing integration: media keys, Control Center, and the lock screen, with artwork
- Dock menu: current track, play/pause, next/previous, volume and mute, shuffle and repeat, and a Playlists submenu to start playing a playlist
- Menu bar mode (Settings > Interface > Show in Menu Bar): the same controls as the Dock menu in the menu bar, plus Show Meziantou Music, Settings and Quit. While no window is open, the Dock icon is hidden: close the window to keep playing with the least resources
- Background synchronization of playlists every 5 minutes and when the app becomes active (at most once a minute)
- Single instance: starting the app again (from another copy, with `open -n`, or by running the executable) brings the running instance to the front and shows its window
- Low resource use in the background: when no window is visible, UI updates and animations stop and cached images are released; closing the window also releases the track list. While playing without a visible window, the app only wakes up to preload the next track and to save the playback position (every 30 seconds; pausing, seeking, changing track or quitting save right away)
- Large audio I/O buffer (up to 4096 frames, as supported by the output device): music does not need a low latency, and the audio thread wakes up far less often
- Update check: at launch, the app checks the GitHub releases (`macos-v*` tags) and suggests updating when a newer version is available; "Update" opens the release page, and "Skip This Version" stops suggesting that version. Use **Meziantou Music > Check for Updates…** to check manually. Debug builds don't check at launch
- Version link: the version at the bottom of the sidebar opens the GitHub page of that release
- Settings: server URL with connection test, streaming/download qualities, interface options, ReplayGain, library rescan (with progress), transcoding cache cleanup, and cache diagnostics

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| Space | Play / pause |
| → / ← | Skip forward / back 20 seconds |
| ⇧→ / ⇧← | Skip forward / back 5 seconds |
| ⌘→ / ⌘← | Next / previous track |
| ⇧⌘→ / ⇧⌘← | Skip forward / back 10 seconds |
| ⌥⌘→ / ⌥⌘← | Skip forward / back 30 seconds |
| ⌘↑ / ⌘↓ | Volume up / down |
| ⌥⌘↓ | Mute |
| ⌘R | Cycle repeat mode |
| ⌘L | Go to the playing track |
| ⌥⌘U | Show / hide the playing queue |
| ⌘F | Search |
| ⌘, | Settings |

Space and the bare or ⇧ arrow keys are ignored while typing in a text field, and the arrow keys only seek while a track is loaded, so they keep their usual meaning in the track list otherwise.

## Playback algorithm

The source of the audio is chosen as described in the web player's README:

1. **Offline**: play the downloaded file; tracks that are not downloaded cannot be played.
2. **Low Data Mode**: play the downloaded file, otherwise stream with the low data quality, or skip the track if "Prevent download on Low Data Mode" is enabled.
3. **Normal**: play the downloaded file when its quality is at least the requested quality, otherwise stream with the normal quality.

If macOS cannot decode a stream (for instance Ogg/Opus on older macOS versions, or unusual raw files), the player asks the server for AAC (for Opus/OGG) or FLAC (for raw files) instead.

## Requirements

- macOS 15 or later
- Xcode 16 or later (Swift 6)

## Build and run

```bash
swift build
swift run MeziantouMusic
```

To create an application bundle (`.build/Meziantou Music.app`):

```bash
Scripts/build-app.sh
```

You can also open `Package.swift` in Xcode.

The application icon (`Sources/MusicPlayerMac/Resources/AppIcon.icns`) is generated from the web player's icon design:

```bash
swift Scripts/generate-icon.swift
```

## Releases

The [Build and release macOS player](../.github/workflows/mac-player-release.yml) workflow publishes the app as a GitHub release. Push a tag such as `macos-v1.2.0`, or run the workflow manually with the tag (and optionally as a prerelease):

```bash
git tag macos-v1.2.0
git push origin macos-v1.2.0
```

The release contains `MeziantouMusic-<tag>.zip` with the app and an `Open-MeziantouMusic.command` helper that removes the quarantine flag.

The app is ad-hoc signed unless these repository secrets are configured:

- Developer ID signing: `MACOS_CERT_P12_BASE64`, `MACOS_CERT_P12_PASSWORD`, `MACOS_SIGNING_IDENTITY`
- Notarization: `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_SPECIFIC_PASSWORD`

## Tests

```bash
swift test
```

## Project structure

- `Sources/MusicPlayerCore`: platform-independent logic (API client, models, play queue, playback source selection, search and sorting, local storage, downloads)
- `Sources/MusicPlayerMac`: the macOS application (SwiftUI views, AVAudioEngine playback, Now Playing, network monitoring, audio outputs)
- `Tests/MusicPlayerCoreTests`: unit tests

Data is stored in `~/Library/Application Support/Meziantou Music`. Set the `MEZIANTOU_MUSIC_DATA_DIR` environment variable to use another directory (useful during development); such an instance can run alongside the regular one.
