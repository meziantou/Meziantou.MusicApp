# Meziantou.MusicApp.WebPlayer

This project is a web-based music player that connects to a compatible server and streams music directly to your browser. It offers a rich set of features for an optimal listening experience.

The features

- Playlist Management: Browse and play playlists with thousands of tracks
- PWA Support: Install as a native app on mobile and desktop
- Offline Mode: Full offline support with automatic caching
- Quality Control: Different streaming quality for Normal/Low Data connections
- Auto-sync: Background playlist refresh and automatic track downloading
- Search: Quick search across tracks, artists, and albums
- Playback Controls: Play, pause, seek, shuffle, repeat modes
- 10-band Equalizer: Real-time EQ controls (31Hz to 16kHz) in the player bar
- AirPlay: Stream audio to AirPlay devices on Safari/macOS
- Media Session API: System-level media controls and notifications
- Dark Theme: Easy on the eyes
- Read-only server interaction (except manual/force library rescans and transcoding cache cleanup)
- State persistence: Playback state saved in IndexedDB
- Auto-resume: Continues playback on app reopen
- Background sync: Periodic playlist refresh (5 min intervals)
- Playing queue management

Configuration
- Server URL: The URL of your Meziantou Music Server
- Authentication token is not required
- Normal Data Quality: Streaming format and bitrate when on WiFi/Ethernet
- Low Data Quality: Lower bitrate for cellular/slow networks
- Download Quality: Quality for offline cached tracks
- Auto-download: Enable background downloading of new tracks
- Interface toggles: Hide cover art, track index, duration, and cache status in the track list

## Playback Algorithm

The player determines the source of the music based on the network status and settings:

1. **Offline**:
   - Always play from the local cache.
   - If the track is not cached, playback fails.

2. **Low Data Mode** (Cellular/Slow connection):
   - **If cached**: Play from local cache.
   - **If not cached**:
     - If "Prevent download on Low Data Mode" is **enabled**: Skip track / do not play.
     - If "Prevent download on Low Data Mode" is **disabled**: Stream using the configured **Low Data Quality**.
   - **Cover Art**: Do not download cover art in Low Data Mode unless cached.

3. **Normal Data Mode** (WiFi/Ethernet):
   - **If cached**: Play from local cache.
   - **If not cached**: Stream using the configured **Normal Data Quality**.

In the future, I want to support more backend servers like Subsonic, Airsonic, static files, and others. So, the model should be generic enough to support multiple backends.

# Components and features

## Playlist list
- Sidebar with all playlists
- Playlist metadata: name, track count, duration
- When a playlist is selected, its tracks are displayed in the main area
- When a music is playint an indicator

# Track list
- List of tracks with configurable columns (cover, track index, title, artist, album, cache status, duration)
- Double-click on a track to play it
- Support playlists with thousands of tracks (performance optimized)
- Search to filter tracks by title, artist, album (accent insensitive, case insensitive, partial matches, etc.)
- Context menu on tracks for actions (download, queue, details, etc.)
- Show indicator when a track is currently playing (the same track can be in multiple playlists, show the indicator only when the track is playing from that playlist)

# Player bar
- Play/pause button
- Seek slider with current time and total duration
- Volume control slider
- 10-band equalizer popover with per-band sliders and reset
- Shuffle and repeat buttons
- Clicking on the cover shows the current playlist and scrolls to the current track

# Player
- Gapless playback
- Automatic quality selection based on network type
- Background downloading of tracks for offline mode

# Playing queue
- View and manage the current playing queue
- Remove tracks from the queue
- Add tracks to the queue from the track list or playlists
- Queue must be persistent across sessions
- When a track ends, automatically play the next track in the queue
- If the mode is repeat mode, be sure there are always at least 200 items in the queue by fetching more from the current playlist. If the playlist has less than 200 items, loop over it multiple times.
- Manually added tracks and playlist tracks are displayed in different sections in the queue

### Search
- Use the search box to filter tracks by title, artist, or album

### Offline Mode
- Downloaded tracks show a icon
- When offline, only cached tracks are playable (greyed out otherwise)
- Playback continues automatically when switching between online/offline
- 

## Technical Details

### Stack
- **TypeScript**: Type-safe development
- **Vite**: Fast build tool and dev server
- **Tauri**: macOS desktop wrapper for the WebPlayer frontend
- **IndexedDB**: Client-side storage for offline support
- **Service Worker**: PWA and caching functionality
- **Media Session API**: System integration
- Minimal dependencies (only necessary ones)

### Performance
- Virtual scrolling for large playlists
- Efficient IndexedDB caching
- Optimized asset loading

## Desktop wrapper (macOS)

The WebPlayer can also run as a macOS desktop application using Tauri.

### Prerequisites

- Node.js (same version used for WebPlayer development)
- Rust toolchain (`rustup`, `cargo`)
- Xcode Command Line Tools

### Commands

```bash
cd Meziantou.MusicApp.WebPlayer
npm ci

# Run desktop app in development mode
npm run tauri:dev

# Build macOS desktop bundle
npm run tauri:build
```

Build artifacts are generated under:

`Meziantou.MusicApp.WebPlayer/src-tauri/target/release/bundle/`
