import AppKit
import MediaPlayer
import MusicPlayerCore

/// Publishes the current track to Control Center / the Now Playing widget and handles media keys.
@MainActor
final class NowPlayingService {
    private let coverLoader: CoverLoader
    private var info: [String: Any] = [:]
    private var artworkTrackId: String?

    init(coverLoader: CoverLoader) {
        self.coverLoader = coverLoader
    }

    func registerCommands(
        play: @escaping @MainActor @Sendable () -> Void,
        pause: @escaping @MainActor @Sendable () -> Void,
        togglePlayPause: @escaping @MainActor @Sendable () -> Void,
        next: @escaping @MainActor @Sendable () -> Void,
        previous: @escaping @MainActor @Sendable () -> Void,
        seek: @escaping @MainActor @Sendable (TimeInterval) -> Void,
        skip: @escaping @MainActor @Sendable (TimeInterval) -> Void
    ) {
        let center = MPRemoteCommandCenter.shared()
        Self.register(center.playCommand, play)
        Self.register(center.pauseCommand, pause)
        Self.register(center.togglePlayPauseCommand, togglePlayPause)
        Self.register(center.nextTrackCommand, next)
        Self.register(center.previousTrackCommand, previous)
        center.skipForwardCommand.preferredIntervals = [10]
        Self.register(center.skipForwardCommand) { skip(10) }
        center.skipBackwardCommand.preferredIntervals = [10]
        Self.register(center.skipBackwardCommand) { skip(-10) }
        center.changePlaybackPositionCommand.addTarget { @Sendable event in
            guard let event = event as? MPChangePlaybackPositionCommandEvent else {
                return .commandFailed
            }

            let position = event.positionTime
            Task { @MainActor in
                seek(position)
            }
            return .success
        }
    }

    /// Handlers can be invoked on any thread, so they must not be main-actor isolated.
    private nonisolated static func register(_ command: MPRemoteCommand, _ action: @escaping @MainActor @Sendable () -> Void) {
        command.addTarget { @Sendable _ in
            Task { @MainActor in
                action()
            }
            return .success
        }
    }

    func updateTrack(_ track: TrackInfo, elapsed: TimeInterval, duration: TimeInterval, isPlaying: Bool) {
        info[MPMediaItemPropertyTitle] = track.title
        info[MPMediaItemPropertyArtist] = track.artists ?? "Unknown Artist"
        info[MPMediaItemPropertyAlbumTitle] = track.album ?? "Unknown Album"
        if artworkTrackId != track.id {
            artworkTrackId = track.id
            info[MPMediaItemPropertyArtwork] = nil
            loadArtwork(trackId: track.id)
        }

        updatePlayback(elapsed: elapsed, duration: duration, isPlaying: isPlaying)
    }

    func updatePlayback(elapsed: TimeInterval, duration: TimeInterval, isPlaying: Bool) {
        guard !info.isEmpty else {
            return
        }

        info[MPMediaItemPropertyPlaybackDuration] = duration
        info[MPNowPlayingInfoPropertyElapsedPlaybackTime] = elapsed
        info[MPNowPlayingInfoPropertyPlaybackRate] = isPlaying ? 1.0 : 0.0
        let center = MPNowPlayingInfoCenter.default()
        center.nowPlayingInfo = info
        center.playbackState = isPlaying ? .playing : .paused
    }

    /// MediaPlayer calls the request handler on a background queue, so it must not be main-actor isolated.
    private nonisolated static func makeArtwork(_ image: NSImage) -> MPMediaItemArtwork {
        return MPMediaItemArtwork(boundsSize: image.size) { @Sendable _ in image }
    }

    private func loadArtwork(trackId: String) {
        Task {
            guard let image = await coverLoader.image(trackId: trackId, pixelSize: CoverLoader.downloadSize, isTrackCached: false), artworkTrackId == trackId else {
                return
            }

            info[MPMediaItemPropertyArtwork] = Self.makeArtwork(image)
            MPNowPlayingInfoCenter.default().nowPlayingInfo = info
        }
    }
}
