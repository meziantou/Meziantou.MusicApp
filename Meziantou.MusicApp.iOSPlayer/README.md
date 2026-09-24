# Meziantou.MusicApp.iOSPlayer

A native SwiftUI player for iPhone and iPad running iOS/iPadOS 18 or later. It uses the same REST API, local cache format, streaming qualities, and offline downloads as the macOS player.

## Features

- Playlist browsing and track search on iPhone and iPad
- Shuffle and repeat (off / repeat all / repeat one) playback modes, shared with the macOS player
- Local playlist downloads for offline listening
- Background audio playback with Control Center and lock-screen transport controls
- Restored current track, playback position, volume, and selected playlist
- Streaming and download quality settings

## Deploy to an iPhone or iPad

1. Generate the Xcode project with `xcodegen generate` (also required again whenever `project.yml` changes), then open `MeziantouMusic.xcodeproj` in Xcode 16 or later. The generated project is not committed to the repository.
2. Select the **MeziantouMusic** target, open **Signing & Capabilities**, and select your Apple Development team. Xcode automatically creates the provisioning profile for the `net.meziantou.music.ios` bundle identifier.
3. Connect and unlock the device, choose it as the run destination, then press **Run**.
4. On first launch, enter the Meziantou Music Server URL in Settings. Local HTTP servers are supported for a user-provided server address, and iOS asks for local-network access when required.

The app declares the `audio` background mode, so playback continues after the app is backgrounded. Device installation requires your own signing team; no signing identities, provisioning profiles, or team IDs are stored in this repository.

## Build from the command line

```bash
xcodegen generate
xcodebuild -project MeziantouMusic.xcodeproj \
  -scheme MeziantouMusic \
  -sdk iphonesimulator \
  -configuration Debug \
  CODE_SIGNING_ALLOWED=NO \
  build
```

## Project generation

`project.yml` is the source of truth for the Xcode project. After changing it, regenerate `MeziantouMusic.xcodeproj`:

```bash
xcodegen generate
```
