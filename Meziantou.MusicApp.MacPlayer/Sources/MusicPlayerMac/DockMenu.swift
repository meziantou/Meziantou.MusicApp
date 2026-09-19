import AppKit
import MusicPlayerCore

/// Builds the menu shown when right-clicking the Dock icon. macOS asks for it every time,
/// so it always reflects the current playback state.
@MainActor
enum DockMenu {
    static func make(model: AppModel) -> NSMenu {
        let player = model.player
        let menu = NSMenu()

        if let track = player.currentTrack {
            let nowPlaying = NSMenuItem(title: track.title, action: nil, keyEquivalent: "")
            nowPlaying.isEnabled = false
            if let artists = track.artists, !artists.isEmpty {
                nowPlaying.title = "\(track.title) — \(artists)"
            }

            menu.addItem(nowPlaying)
            menu.addItem(.separator())
        }

        menu.addItem(ActionMenuItem(title: player.isPlaying ? "Pause" : "Play", isEnabled: player.currentTrack != nil) {
            player.togglePlayPause()
        })
        menu.addItem(ActionMenuItem(title: "Next", isEnabled: player.currentTrack != nil) {
            player.next()
        })
        menu.addItem(ActionMenuItem(title: "Previous", isEnabled: player.currentTrack != nil) {
            player.previous()
        })

        menu.addItem(.separator())
        menu.addItem(ActionMenuItem(title: "Volume Up", isEnabled: player.volume < PlaybackConstants.maxVolume) {
            player.setVolume(player.volume + PlaybackConstants.volumeStep)
        })
        menu.addItem(ActionMenuItem(title: "Volume Down", isEnabled: player.volume > 0) {
            player.setVolume(player.volume - PlaybackConstants.volumeStep)
        })
        menu.addItem(ActionMenuItem(title: player.isMuted ? "Unmute" : "Mute") {
            player.toggleMute()
        })

        menu.addItem(.separator())
        let shuffle = ActionMenuItem(title: "Shuffle") {
            player.setShuffle(!player.shuffleEnabled)
        }
        shuffle.state = player.shuffleEnabled ? .on : .off
        menu.addItem(shuffle)
        menu.addItem(ActionMenuItem(title: "Repeat: \(player.repeatMode.label)") {
            player.cycleRepeatMode()
        })

        if !model.playlists.isEmpty {
            menu.addItem(.separator())
            let playlists = NSMenuItem(title: "Playlists", action: nil, keyEquivalent: "")
            playlists.submenu = playlistsMenu(model: model)
            menu.addItem(playlists)
        }

        return menu
    }

    private static func playlistsMenu(model: AppModel) -> NSMenu {
        let menu = NSMenu()
        for playlist in model.playlists {
            // Offline, only playlists downloaded for offline use can be played
            let isAvailable = model.isOnline || model.offlinePlaylistIds.contains(playlist.id)
            let item = ActionMenuItem(title: playlist.name, isEnabled: isAvailable) {
                Task { await model.playPlaylist(playlist.id) }
            }
            item.state = playlist.id == model.player.playingPlaylistId && model.player.currentTrack != nil ? .on : .off
            menu.addItem(item)
        }

        return menu
    }
}

/// A menu item that runs a closure when chosen.
@MainActor
final class ActionMenuItem: NSMenuItem {
    private let handler: @MainActor () -> Void

    init(title: String, isEnabled: Bool = true, handler: @escaping @MainActor () -> Void) {
        self.handler = handler
        super.init(title: title, action: #selector(runHandler), keyEquivalent: "")
        target = self
        self.isEnabled = isEnabled
    }

    @available(*, unavailable)
    required init(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    @objc private func runHandler() {
        handler()
    }
}
