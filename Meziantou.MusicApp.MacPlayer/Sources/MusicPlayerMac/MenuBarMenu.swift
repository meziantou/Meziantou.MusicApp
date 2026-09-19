import AppKit
import MusicPlayerCore
import SwiftUI

/// The menu of the menu bar item: the same actions as the Dock menu, plus a way back to the window.
/// It only reads state that changes when the track or the settings change (not the playback position),
/// so it is not re-evaluated while music plays.
struct MenuBarMenu: View {
    private let model = AppModel.shared
    @Environment(\.openWindow) private var openWindow
    @Environment(\.openSettings) private var openSettings

    var body: some View {
        let player = model.player
        if let track = player.currentTrack {
            if let artists = track.artists, !artists.isEmpty {
                Text("\(track.title) — \(artists)")
            } else {
                Text(track.title)
            }

            Divider()
        }

        Button(player.isPlaying ? "Pause" : "Play") {
            player.togglePlayPause()
        }
        .disabled(player.currentTrack == nil)

        Button("Next") {
            player.next()
        }
        .disabled(player.currentTrack == nil)

        Button("Previous") {
            player.previous()
        }
        .disabled(player.currentTrack == nil)

        Divider()

        Button("Volume Up") {
            player.setVolume(player.volume + PlaybackConstants.volumeStep)
        }
        .disabled(player.volume >= PlaybackConstants.maxVolume)

        Button("Volume Down") {
            player.setVolume(player.volume - PlaybackConstants.volumeStep)
        }
        .disabled(player.volume <= 0)

        Button(player.isMuted ? "Unmute" : "Mute") {
            player.toggleMute()
        }

        Divider()

        Toggle("Shuffle", isOn: Binding(get: { player.shuffleEnabled }, set: { player.setShuffle($0) }))

        Button("Repeat: \(player.repeatMode.label)") {
            player.cycleRepeatMode()
        }

        if !model.playlists.isEmpty {
            Divider()
            Menu("Playlists") {
                ForEach(model.playlists) { playlist in
                    let isPlaying = playlist.id == player.playingPlaylistId && player.currentTrack != nil
                    Toggle(playlist.name, isOn: Binding(get: { isPlaying }, set: { _ in
                        Task { await model.playPlaylist(playlist.id) }
                    }))
                    // Offline, only playlists downloaded for offline use can be played
                    .disabled(!model.isOnline && !model.offlinePlaylistIds.contains(playlist.id))
                }
            }
        }

        Divider()

        Button("Show Meziantou Music") {
            showMainWindow()
        }

        Button("Settings…") {
            NSApp.activate()
            openSettings()
        }

        Divider()

        Button("Quit Meziantou Music") {
            NSApp.terminate(nil)
        }
    }

    private func showMainWindow() {
        // The Dock icon comes back as soon as a window is shown; set it first so the window is ordered in front
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()

        // A closed window is not reused: SwiftUI released its content
        let existing = NSApp.windows.first { window in
            window.identifier?.rawValue.hasPrefix("main") == true && (window.isVisible || window.isMiniaturized)
        }
        if let existing {
            if existing.isMiniaturized {
                existing.deminiaturize(nil)
            }

            existing.makeKeyAndOrderFront(nil)
        } else {
            openWindow(id: "main")
        }
    }
}
