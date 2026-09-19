import AVFoundation
import CoreAudio
import Foundation
import MusicPlayerCore
import Observation
import OSLog

/// Owns the play queue and the audio engine, and exposes the playback state to the UI.
@MainActor
@Observable
final class PlayerController {
    private struct LoadedFile {
        let trackId: String
        let url: URL
        let quality: StreamingQuality
        /// Temporary files are deleted when no longer needed; downloaded tracks are not.
        let isTemporary: Bool
    }

    // MARK: Observable state

    private(set) var currentTrack: TrackInfo?
    private(set) var currentQuality: StreamingQuality?
    private(set) var isPlaying = false
    private(set) var isLoadingTrack = false
    private(set) var currentTime: TimeInterval = 0
    private(set) var duration: TimeInterval = 0
    private(set) var volume: Double = 1
    private(set) var isMuted = false
    private(set) var queue = PlayQueue()
    private(set) var outputDevices: [AudioOutputDevice] = []
    private(set) var selectedOutputDeviceId: AudioDeviceID?

    var shuffleEnabled: Bool {
        queue.shuffleEnabled
    }

    var repeatMode: RepeatMode {
        queue.repeatMode
    }

    /// The playlist the queue plays from.
    var playingPlaylistId: String? {
        queue.playlistId
    }

    // MARK: Configuration

    var quality = StreamingQuality.raw
    var replayGainMode = ReplayGainMode.off {
        didSet { applyReplayGain() }
    }

    var preventDownloadOnLowData = false

    /// Whether the window is on screen. When it is not, `currentTime` is not published (nothing displays it)
    /// and background work runs less often.
    var isUIVisible = true {
        didSet {
            if isUIVisible && !oldValue {
                currentTime = playbackTime
            }
        }
    }

    /// The actual playback position, even while `currentTime` is not being published.
    private var playbackTime: TimeInterval {
        engine.hasFile ? engine.currentTime : currentTime
    }
    var networkType = NetworkType.normal
    var isOnline = true
    var cachedTrackIds: Set<String> = []

    /// Reports errors to the user.
    var onError: ((String) -> Void)?

    @ObservationIgnored private let engine = AudioEngine()
    @ObservationIgnored private let store: LibraryStore
    @ObservationIgnored private let clientProvider: () -> APIClient
    @ObservationIgnored private let nowPlaying: NowPlayingService
    @ObservationIgnored private var loadedFile: LoadedFile?
    @ObservationIgnored private var preloadedFile: LoadedFile?
    @ObservationIgnored private var preloadTask: Task<Void, Never>?
    @ObservationIgnored private var loadToken = 0
    @ObservationIgnored private var progressTask: Task<Void, Never>?
    @ObservationIgnored private var idleReleaseTask: Task<Void, Never>?
    @ObservationIgnored private var saveTask: Task<Void, Never>?
    @ObservationIgnored private var lastSaveDate = Date.distantPast
    @ObservationIgnored private var preloadedFileWasScheduled = false

    init(store: LibraryStore, clientProvider: @escaping () -> APIClient, coverLoader: CoverLoader) {
        self.store = store
        self.clientProvider = clientProvider
        nowPlaying = NowPlayingService(coverLoader: coverLoader)

        engine.onFinished = { [weak self] in
            self?.handleTrackEnded()
        }
        engine.onAdvancedToNext = { [weak self] in
            self?.handleGaplessAdvance()
        }

        nowPlaying.registerCommands(
            play: { [weak self] in self?.play() },
            pause: { [weak self] in self?.pause() },
            togglePlayPause: { [weak self] in self?.togglePlayPause() },
            next: { [weak self] in self?.next() },
            previous: { [weak self] in self?.previous() },
            seek: { [weak self] time in self?.seek(to: time) },
            skip: { [weak self] offset in self?.skip(by: offset) })
        refreshOutputDevices()
    }

    // MARK: Queue

    /// The items after the current one.
    var lookahead: ArraySlice<QueueItem> {
        queue.lookahead
    }

    func setPlaylist(id: String, tracks: [TrackInfo], shuffleOrder: [Int]? = nil) {
        updateQueueFilters()
        mutateQueue { $0.setPlaylist(id: id, tracks: tracks, shuffleOrder: shuffleOrder) }
    }

    /// Plays a track of the current playlist (identified by its index in the playlist).
    func play(playlistIndex: Int) {
        updateQueueFilters()
        guard queue.play(playlistIndex: playlistIndex), let track = queue.currentTrack else {
            return
        }

        loadTrack(track, autoPlay: true)
    }

    /// Plays at a play-order position, optionally starting at a given time (used to resume).
    func play(atPosition position: Int, autoPlay: Bool, startTime: TimeInterval = 0) {
        updateQueueFilters()
        guard queue.play(atPosition: position), let track = queue.currentTrack else {
            return
        }

        loadTrack(track, autoPlay: autoPlay, startTime: startTime)
    }

    /// Starts the playlist from its first playable track in play order (the shuffle order when shuffle is on).
    func playFromStart(where isAvailable: (TrackInfo) -> Bool) {
        updateQueueFilters()
        let playlist = queue.playlist
        guard let position = playlist.indices.first(where: { isAvailable(playlist[queue.playlistIndex(forPosition: $0)]) }) else {
            onError?("No playable track in this playlist")
            return
        }

        play(atPosition: position, autoPlay: true)
    }

    func addToQueue(_ track: TrackInfo, playlistId: String, indexInPlaylist: Int) {
        mutateQueue { $0.addToQueue(track, playlistId: playlistId, indexInPlaylist: indexInPlaylist) }
    }

    func removeFromQueue(itemId: UUID) {
        guard let index = queue.items.firstIndex(where: { $0.id == itemId }) else {
            return
        }

        mutateQueue { $0.remove(at: index) }
    }

    func moveQueueItem(itemId: UUID, toItemId targetId: UUID) {
        guard let from = queue.items.firstIndex(where: { $0.id == itemId }), let to = queue.items.firstIndex(where: { $0.id == targetId }) else {
            return
        }

        mutateQueue { $0.move(from: from, to: to) }
    }

    /// Jumps to a queue item.
    func playQueueItem(itemId: UUID) {
        guard let index = queue.items.firstIndex(where: { $0.id == itemId }) else {
            return
        }

        updateQueueFilters()
        guard queue.jump(to: index), let track = queue.currentTrack else {
            return
        }

        loadTrack(track, autoPlay: true)
    }

    func setShuffle(_ enabled: Bool) {
        mutateQueue { $0.setShuffle(enabled) }
    }

    func cycleRepeatMode() {
        queue.setRepeatMode(queue.repeatMode.next)
        if queue.repeatMode == .one {
            // The same track plays again: drop the gaplessly scheduled next track
            cancelPreload()
        }

        scheduleStateSave()
    }

    /// Restores the persisted volume and queue, then loads the current track (playing it if it was playing).
    /// `playlistTracks` are the current tracks of the playlist the queue was playing from, if known.
    func restore(_ state: PlaybackState, playlistTracks: [TrackInfo]) {
        volume = min(PlaybackConstants.maxVolume, max(0, state.volume))
        isMuted = state.isMuted
        engine.setVolume(volume, muted: isMuted)
        updateQueueFilters()
        queue.restore(
            playlistId: state.currentPlaylistId,
            playlist: playlistTracks,
            shuffleEnabled: state.shuffleEnabled,
            shuffleOrder: state.shuffleOrder,
            repeatMode: state.repeatMode,
            items: state.queue,
            currentIndex: state.currentTrackIndex)

        if let track = queue.currentTrack, track.id == state.currentTrackId || state.currentTrackId == nil {
            loadTrack(track, autoPlay: state.isPlaying, startTime: state.currentTime)
        } else if let trackId = state.currentTrackId, let playlistIndex = playlistTracks.firstIndex(where: { $0.id == trackId }), queue.play(playlistIndex: playlistIndex), let track = queue.currentTrack {
            loadTrack(track, autoPlay: state.isPlaying, startTime: state.currentTime)
        }
    }

    // MARK: Transport

    func play() {
        guard let currentTrack, !isLoadingTrack else {
            return
        }

        cancelIdleRelease()
        guard engine.hasFile else {
            loadTrack(currentTrack, autoPlay: true, startTime: currentTime)
            return
        }

        do {
            try engine.play()
            setPlaying(true)
        } catch {
            onError?("Playback failed: \(error.localizedDescription)")
        }
    }

    func pause() {
        engine.pause()
        currentTime = engine.currentTime
        setPlaying(false)
        scheduleIdleRelease()
        saveState()
    }

    func togglePlayPause() {
        isPlaying ? pause() : play()
    }

    func next() {
        updateQueueFilters()
        guard queue.hasNext, queue.next(force: true), let track = queue.currentTrack else {
            return
        }

        loadTrack(track, autoPlay: true)
    }

    func previous() {
        if playbackTime > 3 {
            seek(to: 0)
            return
        }

        guard queue.previous(), let track = queue.currentTrack else {
            return
        }

        loadTrack(track, autoPlay: true)
    }

    func seek(to time: TimeInterval) {
        let clamped = max(0, min(time, duration > 0 ? duration : time))
        engine.seek(to: clamped)
        currentTime = clamped
        preloadedFileWasScheduled = false
        nowPlaying.updatePlayback(elapsed: clamped, duration: duration, isPlaying: isPlaying)
        scheduleStateSave()
    }

    func skip(by offset: TimeInterval) {
        seek(to: playbackTime + offset)
    }

    func setVolume(_ newVolume: Double) {
        volume = min(PlaybackConstants.maxVolume, max(0, newVolume))
        if isMuted && volume > 0 {
            isMuted = false
        }

        engine.setVolume(volume, muted: isMuted)
        scheduleStateSave()
    }

    func toggleMute() {
        isMuted.toggle()
        engine.setVolume(volume, muted: isMuted)
        scheduleStateSave()
    }

    // MARK: Output devices

    func refreshOutputDevices() {
        outputDevices = AudioOutputDevices.all()
        if let selectedOutputDeviceId, !outputDevices.contains(where: { $0.id == selectedOutputDeviceId }) {
            selectOutputDevice(nil)
        }
    }

    /// Routes playback to a device (AirPlay speakers included), or back to the system default when nil.
    func selectOutputDevice(_ deviceId: AudioDeviceID?) {
        selectedOutputDeviceId = deviceId
        engine.setOutputDevice(deviceId)
    }

    // MARK: Persistence

    func currentPlaybackState() -> PlaybackState {
        var state = PlaybackState()
        state.currentPlaylistId = queue.playlistId
        state.currentTrackIndex = queue.currentIndex
        state.currentTrackId = currentTrack?.id
        state.currentTime = engine.hasFile ? engine.currentTime : currentTime
        state.isPlaying = isPlaying
        state.volume = volume
        state.isMuted = isMuted
        state.shuffleEnabled = queue.shuffleEnabled
        state.repeatMode = queue.repeatMode
        state.shuffleOrder = queue.shuffleOrder
        state.queue = queue.items
        return state
    }

    func saveState() {
        lastSaveDate = Date()
        saveTask?.cancel()
        saveTask = nil
        let state = currentPlaybackState()
        let store = store
        Task {
            await store.savePlaybackState(state)
        }
    }

    // MARK: Loading

    private func loadTrack(_ track: TrackInfo, autoPlay: Bool, startTime: TimeInterval = 0) {
        Logger.player.debug("Loading \(track.id, privacy: .public) autoPlay=\(autoPlay) startTime=\(startTime)")
        loadToken += 1
        let token = loadToken
        cancelIdleRelease()

        let reusablePreload = preloadedFile?.trackId == track.id ? preloadedFile : nil
        if reusablePreload != nil {
            preloadedFile = nil
        }

        cancelPreload()
        engine.stop()
        releaseLoadedFile()

        currentTrack = track
        currentQuality = nil
        currentTime = startTime
        duration = track.duration
        isLoadingTrack = true
        setPlaying(false)
        nowPlaying.updateTrack(track, elapsed: startTime, duration: track.duration, isPlaying: false)

        Task {
            let file: LoadedFile
            do {
                if let reusablePreload {
                    file = reusablePreload
                } else {
                    file = try await resolveFile(for: track)
                }
            } catch {
                guard token == loadToken else {
                    return
                }

                isLoadingTrack = false
                onError?(error.localizedDescription)
                return
            }

            guard token == loadToken else {
                if file.isTemporary {
                    try? FileManager.default.removeItem(at: file.url)
                }

                return
            }

            await open(file, track: track, token: token, autoPlay: autoPlay, startTime: startTime)
        }
    }

    private func open(_ file: LoadedFile, track: TrackInfo, token: Int, autoPlay: Bool, startTime: TimeInterval) async {
        var file = file
        do {
            try engine.load(url: file.url, startTime: startTime)
        } catch {
            // The system cannot decode this format (e.g. Ogg on older macOS): ask the server for another one
            if file.isTemporary {
                try? FileManager.default.removeItem(at: file.url)
            }

            guard isOnline, let fallback = PlaybackSourceResolver.fallbackQuality(for: file.quality) else {
                isLoadingTrack = false
                onError?("Cannot decode \"\(track.title)\"")
                return
            }

            do {
                let url = try await clientProvider().downloadSong(songId: track.id, quality: fallback)
                file = LoadedFile(trackId: track.id, url: url, quality: fallback, isTemporary: true)
                guard token == loadToken else {
                    try? FileManager.default.removeItem(at: url)
                    return
                }

                try engine.load(url: url, startTime: startTime)
            } catch {
                isLoadingTrack = false
                onError?("Cannot play \"\(track.title)\": \(error.localizedDescription)")
                return
            }
        }

        loadedFile = file
        isLoadingTrack = false
        currentQuality = file.quality
        duration = engine.duration
        currentTime = startTime
        applyReplayGain()
        recordRecentlyPlayed(track.id)
        nowPlaying.updateTrack(track, elapsed: startTime, duration: duration, isPlaying: false)

        if autoPlay {
            play()
        } else {
            scheduleIdleRelease()
        }

        saveState()
    }

    /// Finds or downloads the audio file of a track according to the playback algorithm.
    private func resolveFile(for track: TrackInfo) async throws -> LoadedFile {
        let cached = await store.cachedTrack(id: track.id)
        let source = PlaybackSourceResolver.resolve(
            cachedQuality: cached?.quality,
            desiredQuality: quality,
            isOnline: isOnline,
            networkType: networkType,
            preventDownloadOnLowData: preventDownloadOnLowData)

        switch source {
        case .cache:
            if let cached, let url = await store.cachedTrackFileUrl(id: track.id) {
                return LoadedFile(trackId: track.id, url: url, quality: cached.quality, isTemporary: false)
            }

            throw PlayerError.unavailable("Track is not available offline")
        case let .stream(quality):
            let url = try await clientProvider().downloadSong(songId: track.id, quality: quality)
            return LoadedFile(trackId: track.id, url: url, quality: quality, isTemporary: true)
        case let .unavailable(reason):
            throw PlayerError.unavailable(reason)
        }
    }

    private func releaseLoadedFile() {
        if let loadedFile, loadedFile.isTemporary {
            try? FileManager.default.removeItem(at: loadedFile.url)
        }

        loadedFile = nil
    }

    // MARK: Preloading & gapless playback

    /// Preload the next track only near the end of the current one (last 30 s or 10%).
    private func checkForPreload() {
        guard preloadTask == nil, preloadedFile == nil, queue.repeatMode != .one, duration > 0, let next = queue.lookahead.first else {
            return
        }

        let tailWindow = min(30, duration * 0.1)
        guard duration - playbackTime <= tailWindow else {
            return
        }

        let token = loadToken
        let nextTrack = next.track
        preloadTask = Task {
            defer {
                if token == loadToken {
                    preloadTask = nil
                }
            }

            guard let file = try? await resolveFile(for: nextTrack), !Task.isCancelled, token == loadToken else {
                return
            }

            preloadedFile = file
            preloadedFileWasScheduled = engine.scheduleNext(url: file.url)
        }
    }

    private func cancelPreload() {
        preloadTask?.cancel()
        preloadTask = nil
        if preloadedFileWasScheduled {
            engine.cancelScheduledNext()
            preloadedFileWasScheduled = false
        }

        if let preloadedFile, preloadedFile.isTemporary {
            try? FileManager.default.removeItem(at: preloadedFile.url)
        }

        preloadedFile = nil
    }

    /// The engine moved on to the gaplessly scheduled file.
    private func handleGaplessAdvance() {
        guard let preloadedFile else {
            return
        }

        updateQueueFilters()
        queue.next(force: true)
        releaseLoadedFile()
        loadedFile = preloadedFile
        self.preloadedFile = nil
        preloadedFileWasScheduled = false

        let track = queue.currentTrack?.id == preloadedFile.trackId ? queue.currentTrack : currentTrack
        currentTrack = track
        currentQuality = preloadedFile.quality
        duration = engine.duration
        currentTime = engine.currentTime
        applyReplayGain()
        if let track {
            recordRecentlyPlayed(track.id)
            nowPlaying.updateTrack(track, elapsed: currentTime, duration: duration, isPlaying: isPlaying)
        }

        saveState()
    }

    private func handleTrackEnded() {
        Logger.player.debug("Track ended")
        if queue.repeatMode == .one {
            engine.seek(to: 0)
            play()
            return
        }

        if queue.hasNext {
            next()
            return
        }

        engine.pause()
        setPlaying(false)
        currentTime = duration
        scheduleIdleRelease()
        saveState()
    }

    /// Applies a queue mutation, dropping the gaplessly scheduled track when the next item changed.
    private func mutateQueue(_ mutation: (inout PlayQueue) -> Void) {
        let nextIdBefore = queue.lookahead.first?.id
        mutation(&queue)
        if queue.lookahead.first?.id != nextIdBefore {
            cancelPreload()
        }

        scheduleStateSave()
    }

    private func updateQueueFilters() {
        queue.cachedTrackIds = cachedTrackIds
        queue.isOnline = isOnline
        queue.networkType = networkType
        queue.preventDownloadOnLowData = preventDownloadOnLowData
    }

    // MARK: Progress, idle release and state saving

    private func setPlaying(_ playing: Bool) {
        Logger.player.debug("isPlaying = \(playing)")
        isPlaying = playing
        nowPlaying.updatePlayback(elapsed: engine.hasFile ? engine.currentTime : currentTime, duration: duration, isPlaying: playing)
        if playing {
            startProgressUpdates()
        } else {
            progressTask?.cancel()
            progressTask = nil
        }
    }

    private func startProgressUpdates() {
        guard progressTask == nil else {
            return
        }

        progressTask = Task {
            while !Task.isCancelled {
                // Publishing the time redraws the player bar: only do it when it can be seen
                if isUIVisible {
                    currentTime = engine.currentTime
                }

                checkForPreload()
                if Date().timeIntervalSince(lastSaveDate) >= 5 {
                    saveState()
                }

                try? await Task.sleep(for: isUIVisible ? .milliseconds(250) : .seconds(1))
            }
        }
    }

    private func scheduleIdleRelease() {
        idleReleaseTask?.cancel()
        idleReleaseTask = Task {
            try? await Task.sleep(for: PlaybackConstants.idleReleaseDelay)
            guard !Task.isCancelled, !isPlaying else {
                return
            }

            engine.suspend()
        }
    }

    private func cancelIdleRelease() {
        idleReleaseTask?.cancel()
        idleReleaseTask = nil
    }

    private func scheduleStateSave() {
        saveTask?.cancel()
        saveTask = Task {
            try? await Task.sleep(for: .seconds(1))
            guard !Task.isCancelled else {
                return
            }

            saveState()
        }
    }

    private func applyReplayGain() {
        guard let currentTrack else {
            engine.setReplayGain(linear: 1)
            return
        }

        engine.setReplayGain(linear: ReplayGain.linearGain(for: currentTrack, mode: replayGainMode))
    }

    private func recordRecentlyPlayed(_ trackId: String) {
        let store = store
        Task {
            await store.addRecentlyPlayed(trackId: trackId)
        }
    }

    /// Stops playback and deletes temporary files (called when the app quits, after saving the state).
    func shutdown() {
        saveTask?.cancel()
        progressTask?.cancel()
        cancelPreload()
        engine.stop()
        releaseLoadedFile()
    }
}

extension Logger {
    static let player = Logger(subsystem: "net.meziantou.music", category: "player")
}

enum PlayerError: LocalizedError {
    case unavailable(String)

    var errorDescription: String? {
        switch self {
        case let .unavailable(reason):
            reason
        }
    }
}
