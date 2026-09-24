import AVFoundation
import MediaPlayer
import MusicPlayerCore
import Observation

@MainActor
@Observable
final class MobilePlayerController {
    private let store: LibraryStore
    private let clientProvider: @MainActor () -> APIClient
    private let player = AVPlayer()
    private var timeObserver: Any?
    private var endObserver: (any NSObjectProtocol)?
    private var currentFile: URL?
    private var currentFileIsTemporary = false
    private var queue = PlayQueue()

    private(set) var currentTrack: TrackInfo?
    private(set) var currentTime: TimeInterval = 0
    private(set) var duration: TimeInterval = 0
    private(set) var isPlaying = false
    private(set) var isLoadingTrack = false
    private(set) var volume = 1.0
    private(set) var isMuted = false
    var quality = StreamingQuality.raw
    var isOnline = true {
        didSet { queue.isOnline = isOnline }
    }
    var cachedTrackIds: Set<String> = [] {
        didSet { queue.cachedTrackIds = cachedTrackIds }
    }
    var onError: ((String) -> Void)?
    var onTrackChanged: ((TrackInfo) -> Void)?

    var shuffleEnabled: Bool {
        queue.shuffleEnabled
    }

    var repeatMode: RepeatMode {
        queue.repeatMode
    }

    init(store: LibraryStore, clientProvider: @escaping @MainActor () -> APIClient) {
        self.store = store
        self.clientProvider = clientProvider
        configureAudioSession()
        configureRemoteCommands()
        timeObserver = player.addPeriodicTimeObserver(forInterval: CMTime(seconds: 0.5, preferredTimescale: 600), queue: .main) { [weak self] time in
            Task { @MainActor in
                self?.updateTime(time.seconds)
            }
        }
    }

    func restore(_ state: PlaybackState, tracks: [TrackInfo]) {
        volume = state.volume
        isMuted = state.isMuted
        queue.restore(
            playlistId: state.currentPlaylistId,
            playlist: tracks,
            shuffleEnabled: state.shuffleEnabled,
            shuffleOrder: state.shuffleOrder,
            repeatMode: state.repeatMode,
            items: state.queue,
            currentIndex: state.currentTrackIndex)

        if let track = queue.currentTrack, track.id == state.currentTrackId || state.currentTrackId == nil {
            loadCurrentTrack(startTime: state.currentTime, autoplay: state.isPlaying)
        } else if let trackId = state.currentTrackId, let playlistIndex = tracks.firstIndex(where: { $0.id == trackId }), queue.play(playlistIndex: playlistIndex) {
            loadCurrentTrack(startTime: state.currentTime, autoplay: state.isPlaying)
        }
    }

    /// Starts playback of a playlist, beginning at the given index within `tracks`.
    func play(playlistId: String, tracks: [TrackInfo], startIndex: Int) {
        queue.setPlaylist(id: playlistId, tracks: tracks)
        guard queue.play(playlistIndex: startIndex) else {
            return
        }

        loadCurrentTrack(autoplay: true)
    }

    func setShuffle(_ enabled: Bool) {
        queue.setShuffle(enabled)
    }

    func cycleRepeatMode() {
        queue.setRepeatMode(queue.repeatMode.next)
    }

    private func loadCurrentTrack(startTime: TimeInterval = 0, autoplay: Bool = true) {
        guard let track = queue.currentTrack else {
            return
        }

        currentTrack = track
        currentTime = startTime
        duration = track.duration
        isLoadingTrack = true
        Task {
            do {
                let source = try await source(for: track)
                guard currentTrack?.id == track.id else {
                    if source.isTemporary {
                        try? FileManager.default.removeItem(at: source.url)
                    }

                    return
                }

                replaceItem(source, startTime: startTime)
                isLoadingTrack = false
                onTrackChanged?(track)
                updateNowPlaying()
                if autoplay {
                    play()
                }
            } catch {
                isLoadingTrack = false
                onError?(error.localizedDescription)
            }
        }
    }

    func play() {
        guard currentTrack != nil else {
            return
        }

        do {
            try AVAudioSession.sharedInstance().setActive(true)
            player.play()
            isPlaying = true
            updateNowPlaying()
        } catch {
            onError?("Cannot activate audio: \(error.localizedDescription)")
        }
    }

    func pause() {
        player.pause()
        isPlaying = false
        updateNowPlaying()
    }

    func togglePlayPause() {
        isPlaying ? pause() : play()
    }

    func previous() {
        if currentTime > 3 {
            seek(to: 0)
        } else if queue.previous() {
            loadCurrentTrack(autoplay: true)
        }
    }

    func next() {
        guard queue.next(force: true) else {
            return
        }

        loadCurrentTrack(autoplay: true)
    }

    func seek(to time: TimeInterval) {
        let clamped = max(0, min(duration, time))
        player.seek(to: CMTime(seconds: clamped, preferredTimescale: 600))
        currentTime = clamped
        updateNowPlaying()
    }

    func setVolume(_ value: Double) {
        volume = max(0, min(1, value))
        if volume > 0 {
            isMuted = false
        }

        player.volume = isMuted ? 0 : Float(volume)
    }

    func toggleMute() {
        isMuted.toggle()
        player.volume = isMuted ? 0 : Float(volume)
    }

    func playbackState(playlistId: String?) -> PlaybackState {
        var state = PlaybackState()
        state.currentPlaylistId = queue.playlistId ?? playlistId
        state.currentTrackId = currentTrack?.id
        state.currentTrackIndex = queue.currentIndex
        state.currentTime = currentTime
        state.isPlaying = isPlaying
        state.volume = volume
        state.isMuted = isMuted
        state.shuffleEnabled = queue.shuffleEnabled
        state.repeatMode = queue.repeatMode
        state.shuffleOrder = queue.shuffleOrder
        state.queue = queue.items
        return state
    }

    private func source(for track: TrackInfo) async throws -> AudioSource {
        if let cached = await store.cachedTrackFileUrl(id: track.id) {
            return AudioSource(url: cached, isTemporary: false)
        }

        guard isOnline else {
            throw MobilePlayerError.unavailableOffline
        }

        return AudioSource(url: try await clientProvider().downloadSong(songId: track.id, quality: quality), isTemporary: true)
    }

    private func replaceItem(_ source: AudioSource, startTime: TimeInterval) {
        if currentFileIsTemporary, let currentFile {
            try? FileManager.default.removeItem(at: currentFile)
        }

        currentFile = source.url
        currentFileIsTemporary = source.isTemporary
        let item = AVPlayerItem(url: source.url)
        player.replaceCurrentItem(with: item)
        player.volume = isMuted ? 0 : Float(volume)
        if startTime > 0 {
            player.seek(to: CMTime(seconds: startTime, preferredTimescale: 600))
        }

        if let endObserver {
            NotificationCenter.default.removeObserver(endObserver)
        }

        endObserver = NotificationCenter.default.addObserver(forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in
            Task { @MainActor in
                self?.next()
            }
        }
    }

    private func updateTime(_ time: TimeInterval) {
        guard time.isFinite else {
            return
        }

        currentTime = time
        if let item = player.currentItem, item.duration.seconds.isFinite {
            duration = item.duration.seconds
        }

        updateNowPlaying()
    }

    private func configureAudioSession() {
        do {
            try AVAudioSession.sharedInstance().setCategory(.playback, mode: .default, policy: .longFormAudio)
        } catch {
            onError?("Cannot configure audio: \(error.localizedDescription)")
        }
    }

    private func configureRemoteCommands() {
        let commands = MPRemoteCommandCenter.shared()
        commands.playCommand.addTarget { [weak self] _ in
            Task { @MainActor in self?.play() }
            return .success
        }
        commands.pauseCommand.addTarget { [weak self] _ in
            Task { @MainActor in self?.pause() }
            return .success
        }
        commands.nextTrackCommand.addTarget { [weak self] _ in
            Task { @MainActor in self?.next() }
            return .success
        }
        commands.previousTrackCommand.addTarget { [weak self] _ in
            Task { @MainActor in self?.previous() }
            return .success
        }
        commands.changePlaybackPositionCommand.addTarget { [weak self] event in
            guard let event = event as? MPChangePlaybackPositionCommandEvent else {
                return .commandFailed
            }

            Task { @MainActor in self?.seek(to: event.positionTime) }
            return .success
        }
    }

    private func updateNowPlaying() {
        guard let track = currentTrack else {
            return
        }

        MPNowPlayingInfoCenter.default().nowPlayingInfo = [
            MPMediaItemPropertyTitle: track.title,
            MPMediaItemPropertyArtist: track.artists ?? "Unknown Artist",
            MPMediaItemPropertyAlbumTitle: track.album ?? "Unknown Album",
            MPMediaItemPropertyPlaybackDuration: duration,
            MPNowPlayingInfoPropertyElapsedPlaybackTime: currentTime,
            MPNowPlayingInfoPropertyPlaybackRate: isPlaying ? 1 : 0,
        ]
        MPNowPlayingInfoCenter.default().playbackState = isPlaying ? .playing : .paused
    }
}

private struct AudioSource {
    let url: URL
    let isTemporary: Bool
}

private enum MobilePlayerError: LocalizedError {
    case unavailableOffline

    var errorDescription: String? {
        "This track is not available offline."
    }
}
