import AppKit
import MusicPlayerCore
import Observation
import OSLog
import SwiftUI

struct Toast: Identifiable, Equatable {
    enum Kind {
        case info
        case success
        case error
    }

    let id = UUID()
    let message: String
    let kind: Kind
}

/// Application state: settings, playlists, offline caching and server actions.
@MainActor
@Observable
final class AppModel {
    private(set) var settings = AppSettings()
    private(set) var playlists: [PlaylistSummary] = []
    private(set) var selectedPlaylistId: String?
    private(set) var selectedPlaylistTracks: [TrackInfo] = [] {
        didSet { tracksVersion += 1 }
    }

    /// Incremented whenever `selectedPlaylistTracks` is assigned, so views can cheaply detect changes.
    private(set) var tracksVersion = 0
    private(set) var invalidPlaylists: [InvalidPlaylistInfo] = []
    private(set) var isOnline = true
    private(set) var networkType = NetworkType.normal
    private(set) var cachedTrackIds: Set<String> = []
    private(set) var offlinePlaylistIds: Set<String> = []
    private(set) var playlistDownloadProgress: [String: PlaylistDownloadProgress] = [:]
    private(set) var isInitialized = false
    private(set) var toasts: [Toast] = []
    private var loadingCount = 0

    /// Track shown in the song details sheet.
    var songDetailsTrack: TrackInfo?
    /// Incremented to ask the track list to scroll to the playing track.
    private(set) var scrollToCurrentTrackRequest = 0
    /// Incremented to ask the track list to focus the search field.
    private(set) var searchFocusRequest = 0
    var isQueueVisible = false
    /// Whether a window of the app is on screen (not minimized, hidden, covered or on another Space).
    private(set) var isUIVisible = true

    var isLoading: Bool {
        loadingCount > 0
    }

    @ObservationIgnored let store: LibraryStore
    @ObservationIgnored let player: PlayerController
    @ObservationIgnored let coverLoader: CoverLoader
    @ObservationIgnored private let downloads: DownloadManager
    @ObservationIgnored private let networkMonitor = NetworkMonitor()
    @ObservationIgnored private let apiBox = APIClientBox()
    @ObservationIgnored private var syncTask: Task<Void, Never>?
    @ObservationIgnored private var lastPlaylistSyncDate = Date.distantPast
    @ObservationIgnored private var scanMonitorTask: Task<Void, Never>?
    @ObservationIgnored private var memoryPressureSource: (any DispatchSourceMemoryPressure)?
    /// Whether the track list is displayed; when the main window is closed its tracks are released.
    @ObservationIgnored private var isTrackListShown = true

    init(store: LibraryStore = LibraryStore(rootDirectory: LibraryStore.defaultRootDirectory())) {
        self.store = store
        let apiBox = apiBox
        coverLoader = CoverLoader(store: store, clientProvider: { apiBox.client })
        player = PlayerController(store: store, clientProvider: { apiBox.client }, coverLoader: coverLoader)
        downloads = DownloadManager(store: store, clientProvider: { apiBox.client })

        player.onError = { [weak self] message in
            self?.showToast(message, kind: .error)
        }
        downloads.onEvent = { [weak self] event in
            self?.handleDownloadEvent(event)
        }
        networkMonitor.onChange = { [weak self] isOnline, networkType in
            self?.handleNetworkChange(isOnline: isOnline, networkType: networkType)
        }
    }

    var apiClient: APIClient {
        apiBox.client
    }

    private var api: APIClient {
        get { apiBox.client }
        set { apiBox.client = newValue }
    }

    // MARK: Initialization

    func initialize() async {
        guard !isInitialized else {
            return
        }

        do {
            try await store.initialize()
        } catch {
            showToast("Failed to open the local library: \(error.localizedDescription)", kind: .error)
        }

        settings = await store.settings()
        api = APIClient(baseUrl: settings.serverUrl)
        applySettingsToPlayer()

        networkMonitor.start()
        isOnline = networkMonitor.isOnline
        networkType = networkMonitor.networkType
        applyNetworkToPlayer()

        await downloads.refreshCacheState()
        setCachedTrackIds(await store.cachedTrackIds())
        offlinePlaylistIds = await store.offlinePlaylistIds()
        let removed = await store.verifyOfflinePlaylistsIntegrity()
        if !removed.isEmpty {
            offlinePlaylistIds = await store.offlinePlaylistIds()
        }

        isInitialized = true
        if !settings.serverUrl.isEmpty {
            await loadInitialData()
        }

        startPeriodicSync()
        startMemoryPressureMonitoring()
    }

    private func loadInitialData() async {
        beginLoading()
        defer { endLoading() }

        var currentPlaylists: [PlaylistSummary]
        if isOnline {
            currentPlaylists = await syncPlaylistsInternal(refreshAllTracks: true)
        } else {
            currentPlaylists = await store.cachedPlaylistSummaries()
            playlists = currentPlaylists
            playlistDownloadProgress = [:]
            for playlist in currentPlaylists where offlinePlaylistIds.contains(playlist.id) {
                if let tracks = await store.cachedPlaylist(id: playlist.id)?.tracks {
                    playlistDownloadProgress[playlist.id] = progress(for: tracks)
                }
            }
        }

        let state = await store.playbackState()

        // Restore the last viewed playlist, then the playing one, then the first one
        let lastViewedId = UserDefaults.standard.string(forKey: DefaultsKeys.lastViewedPlaylistId)
        let viewedId = [lastViewedId, state.currentPlaylistId, currentPlaylists.first?.id]
            .compactMap { $0 }
            .first { id in currentPlaylists.contains { $0.id == id } }

        var viewedTracks: [TrackInfo] = []
        if let viewedId {
            selectedPlaylistId = viewedId
            viewedTracks = await loadPlaylistTracks(playlistId: viewedId, knownPlaylists: currentPlaylists)
        }

        var playingTracks: [TrackInfo] = []
        if let playingId = state.currentPlaylistId {
            if playingId == viewedId {
                playingTracks = viewedTracks
            } else if isOnline, let response = try? await api.playlistTracks(playlistId: playingId) {
                playingTracks = response.tracks
            } else {
                playingTracks = await store.cachedPlaylist(id: playingId)?.tracks ?? []
            }
        }

        player.restore(state, playlistTracks: playingTracks)

        if isOnline, let status = try? await api.scanStatus() {
            invalidPlaylists = status.invalidPlaylists
        }
    }

    // MARK: Toasts & loading

    func showToast(_ message: String, kind: Toast.Kind = .info) {
        if kind == .error {
            Logger.app.error("\(message, privacy: .public)")
        }

        let toast = Toast(message: message, kind: kind)
        toasts.append(toast)
        Task {
            try? await Task.sleep(for: .seconds(3))
            toasts.removeAll { $0.id == toast.id }
        }
    }

    private func beginLoading() {
        loadingCount += 1
    }

    private func endLoading() {
        loadingCount = max(0, loadingCount - 1)
    }

    // MARK: Network

    private func handleNetworkChange(isOnline newIsOnline: Bool, networkType newNetworkType: NetworkType) {
        let wasOnline = isOnline
        isOnline = newIsOnline
        networkType = newNetworkType
        applyNetworkToPlayer()

        if !wasOnline && newIsOnline {
            showToast("Back online")
            Task { _ = await syncPlaylistsInternal() }
        } else if wasOnline && !newIsOnline {
            showToast("You are offline")
        }
    }

    private func applyNetworkToPlayer() {
        player.isOnline = isOnline
        player.networkType = networkType
        player.quality = settings.streamingQuality(for: networkType)
        coverLoader.isOnline = isOnline
        coverLoader.allowsNetworkForUncachedTracks = networkType != .lowData
    }

    // MARK: Settings

    private func applySettingsToPlayer() {
        player.replayGainMode = settings.replayGainMode
        player.preventDownloadOnLowData = settings.preventDownloadOnLowData
        player.quality = settings.streamingQuality(for: networkType)
        coverLoader.isEnabled = !settings.hideCoverArt
    }

    func updateSettings(_ newSettings: AppSettings) async {
        var normalized = newSettings
        normalized.serverUrl = normalized.serverUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        let previous = settings

        settings = normalized
        await store.saveSettings(normalized)
        api = APIClient(baseUrl: normalized.serverUrl)
        applySettingsToPlayer()
        showToast("Settings saved")

        if normalized.downloadQuality != previous.downloadQuality {
            await store.clearCachedTracks()
            await downloads.refreshCacheState()
            setCachedTrackIds([])

            var redownloadCount = 0
            for playlistId in await store.offlinePlaylistIds() {
                if let cached = await store.cachedPlaylist(id: playlistId), !cached.tracks.isEmpty {
                    playlistDownloadProgress[playlistId] = PlaylistDownloadProgress(cached: 0, total: cached.tracks.count)
                    await downloads.queuePlaylistDownload(cached.tracks, playlistId: playlistId, quality: normalized.downloadQuality)
                    redownloadCount += 1
                }
            }

            if redownloadCount > 0 {
                showToast("Redownloading \(redownloadCount) offline playlists")
            }
        }

        if normalized.serverUrl != previous.serverUrl && !normalized.serverUrl.isEmpty {
            beginLoading()
            defer { endLoading() }
            if previous.serverUrl.isEmpty {
                await loadInitialData()
            } else {
                _ = await syncPlaylistsInternal()
            }
        }
    }

    func testConnection(serverUrl: String) async -> Bool {
        await APIClient(baseUrl: serverUrl).testConnection()
    }

    // MARK: Playlists

    func selectPlaylist(_ playlistId: String) async {
        guard playlistId != selectedPlaylistId || selectedPlaylistTracks.isEmpty else {
            return
        }

        selectedPlaylistId = playlistId
        UserDefaults.standard.set(playlistId, forKey: DefaultsKeys.lastViewedPlaylistId)
        beginLoading()
        defer { endLoading() }
        _ = await loadPlaylistTracks(playlistId: playlistId)
    }

    func syncPlaylists() async {
        _ = await syncPlaylistsInternal()
    }

    private func startPeriodicSync() {
        syncTask?.cancel()
        syncTask = Task {
            while !Task.isCancelled {
                try? await Task.sleep(for: PlaybackConstants.playlistSyncInterval)
                if isOnline && !settings.serverUrl.isEmpty && NSApp.isActive {
                    _ = await syncPlaylistsInternal()
                }
            }
        }
    }

    /// Called when the app becomes active, like the web player does when the tab becomes visible.
    func applicationDidBecomeActive() {
        // Switching back and forth between apps should not refetch the playlists every time
        guard isInitialized, isOnline, !settings.serverUrl.isEmpty, Date().timeIntervalSince(lastPlaylistSyncDate) >= PlaybackConstants.activationSyncMinInterval else {
            return
        }

        Task { _ = await syncPlaylistsInternal() }
    }

    @discardableResult
    private func syncPlaylistsInternal(refreshAllTracks: Bool = false) async -> [PlaylistSummary] {
        guard isOnline, api.isConfigured else {
            return playlists
        }

        lastPlaylistSyncDate = Date()

        // Decoding playlists allocates a lot of short-lived memory: give the freed pages back to the system
        defer { malloc_zone_pressure_relief(nil, 0) }

        do {
            let sorted = try await api.playlists().playlists.sorted { $0.sortOrder < $1.sortOrder }
            if sorted.isEmpty, let status = try? await api.scanStatus(), !status.isInitialScanCompleted {
                // The server is still indexing: keep the cached data and retry later
                return await store.cachedPlaylistSummaries()
            }

            playlists = sorted
            let currentIds = Set(sorted.map(\.id))

            // Forget playlists that were deleted on the server
            for cached in await store.cachedPlaylistSummaries() where !currentIds.contains(cached.id) {
                await store.deleteCachedPlaylist(id: cached.id)
                if offlinePlaylistIds.contains(cached.id) {
                    await store.setPlaylistOffline(id: cached.id, enabled: false)
                    offlinePlaylistIds.remove(cached.id)
                }

                playlistDownloadProgress[cached.id] = nil
            }

            for playlist in sorted {
                let cachedSummary = await store.cachedPlaylistSummary(id: playlist.id)
                let needsUpdate = cachedSummary.map { isOlder($0.changed, than: playlist.changed) } ?? true
                let isOffline = offlinePlaylistIds.contains(playlist.id)
                let refreshesTracks = refreshAllTracks || needsUpdate
                // Tracks of a cached playlist are only needed for offline playlists (diff and progress).
                // An unchanged playlist whose tracks are all downloaded has nothing to resume: don't decode it.
                let needsCachedTracks = isOffline && (refreshesTracks || playlistDownloadProgress[playlist.id]?.isComplete != true)
                let cached = needsCachedTracks ? await store.cachedPlaylist(id: playlist.id) : nil
                if refreshesTracks {
                    let tracks = try await api.playlistTracks(playlistId: playlist.id).tracks
                    await store.saveCachedPlaylist(playlist, tracks: tracks)

                    if isOffline {
                        playlistDownloadProgress[playlist.id] = progress(for: tracks)
                        let uncached = tracks.filter { !cachedTrackIds.contains($0.id) }
                        if !uncached.isEmpty {
                            await downloads.queuePlaylistDownload(uncached, playlistId: playlist.id, quality: settings.downloadQuality)
                        }

                        if let cached {
                            let newIds = Set(tracks.map(\.id))
                            for track in cached.tracks where !newIds.contains(track.id) {
                                await store.removePlaylist(playlist.id, fromTrack: track.id)
                            }

                            setCachedTrackIds(await store.cachedTrackIds())
                        }
                    }

                    if selectedPlaylistId == playlist.id && isTrackListShown && selectedPlaylistTracks != tracks {
                        selectedPlaylistTracks = tracks
                    }
                } else if isOffline, let cached {
                    // Unchanged playlist: make sure the progress is known and resume interrupted downloads
                    let progress = progress(for: cached.tracks)
                    if playlistDownloadProgress[playlist.id] != progress {
                        playlistDownloadProgress[playlist.id] = progress
                    }

                    let uncached = cached.tracks.filter { !cachedTrackIds.contains($0.id) }
                    if !uncached.isEmpty {
                        await downloads.queuePlaylistDownload(uncached, playlistId: playlist.id, quality: settings.downloadQuality)
                    }
                }
            }

            return sorted
        } catch {
            if let status = try? await api.scanStatus() {
                if !status.isInitialScanCompleted {
                    return await store.cachedPlaylistSummaries()
                }
            } else {
                // The server is unreachable: show what we have
                let cached = await store.cachedPlaylistSummaries()
                if !cached.isEmpty {
                    showToast("Server unavailable, showing cached data")
                    playlists = cached
                    return cached
                }
            }

            return []
        }
    }

    @discardableResult
    private func loadPlaylistTracks(playlistId: String, knownPlaylists: [PlaylistSummary]? = nil) async -> [TrackInfo] {
        // Show cached data immediately
        if let cached = await store.cachedPlaylist(id: playlistId) {
            if selectedPlaylistId == playlistId {
                selectedPlaylistTracks = cached.tracks
            }

            return cached.tracks
        }

        guard isOnline else {
            selectedPlaylistTracks = []
            showToast("Playlist not available offline", kind: .error)
            return []
        }

        do {
            let tracks = try await api.playlistTracks(playlistId: playlistId).tracks
            if tracks.isEmpty, let status = try? await api.scanStatus(), !status.isInitialScanCompleted {
                return []
            }

            if selectedPlaylistId == playlistId {
                selectedPlaylistTracks = tracks
            }

            if let playlist = (knownPlaylists ?? playlists).first(where: { $0.id == playlistId }) {
                await store.saveCachedPlaylist(playlist, tracks: tracks)
            }

            return tracks
        } catch {
            showToast("Failed to load tracks", kind: .error)
            return []
        }
    }

    private func isOlder(_ lhs: String, than rhs: String) -> Bool {
        guard let left = DateParsing.parse(lhs), let right = DateParsing.parse(rhs) else {
            return lhs != rhs
        }

        return left < right
    }

    private func progress(for tracks: [TrackInfo]) -> PlaylistDownloadProgress {
        PlaylistDownloadProgress(cached: tracks.count { cachedTrackIds.contains($0.id) }, total: tracks.count)
    }

    // MARK: Playback

    /// Plays a track of the selected playlist; `orderedTracks` is the list as displayed (sorted).
    func playTrack(_ track: TrackInfo, orderedTracks: [TrackInfo]) {
        guard let selectedPlaylistId, isTrackAvailable(track) else {
            return
        }

        player.quality = settings.streamingQuality(for: networkType)
        player.setPlaylist(id: selectedPlaylistId, tracks: orderedTracks)
        if let index = orderedTracks.firstIndex(where: { $0.id == track.id }) {
            player.play(playlistIndex: index)
        }
    }

    /// Shows a playlist and plays it from the start, in the order used by the track list.
    func playPlaylist(_ playlistId: String) async {
        await selectPlaylist(playlistId)
        guard selectedPlaylistId == playlistId, !selectedPlaylistTracks.isEmpty else {
            return
        }

        let defaults = UserDefaults.standard
        let sortOption = defaults.string(forKey: DefaultsKeys.trackSortOption).flatMap(TrackSortOption.init(rawValue:)) ?? .added
        let sortDirection = defaults.string(forKey: DefaultsKeys.trackSortDirection).flatMap(TrackSortDirection.init(rawValue:)) ?? sortOption.defaultDirection
        player.quality = settings.streamingQuality(for: networkType)
        player.setPlaylist(id: playlistId, tracks: TrackSorting.sort(selectedPlaylistTracks, by: sortOption, direction: sortDirection))
        player.playFromStart(where: isTrackAvailable)
    }

    func addToQueue(_ track: TrackInfo, indexInPlaylist: Int) {
        guard let selectedPlaylistId else {
            return
        }

        player.addToQueue(track, playlistId: selectedPlaylistId, indexInPlaylist: indexInPlaylist)
        showToast("Added \"\(track.title)\" to queue")
    }

    func requestSearchFocus() {
        searchFocusRequest += 1
    }

    func isTrackAvailable(_ track: TrackInfo) -> Bool {
        isOnline || cachedTrackIds.contains(track.id)
    }

    /// Shows the playing playlist and scrolls to the playing track.
    func revealCurrentTrack() async {
        guard let playingPlaylistId = player.playingPlaylistId else {
            return
        }

        if selectedPlaylistId != playingPlaylistId, playlists.contains(where: { $0.id == playingPlaylistId }) {
            await selectPlaylist(playingPlaylistId)
        }

        scrollToCurrentTrackRequest += 1
    }

    // MARK: Downloads

    func downloadTrack(_ track: TrackInfo) async {
        guard let selectedPlaylistId else {
            return
        }

        await downloads.queueDownload(track, playlistId: selectedPlaylistId, quality: settings.downloadQuality)
        showToast("Downloading \"\(track.title)\"")
    }

    func deleteDownloadedTrack(_ track: TrackInfo) async {
        let affectedPlaylistIds = await store.cachedTrack(id: track.id)?.playlistIds ?? []
        await downloads.deleteTrack(trackId: track.id)
        var ids = cachedTrackIds
        ids.remove(track.id)
        setCachedTrackIds(ids)
        for playlistId in affectedPlaylistIds {
            if var progress = playlistDownloadProgress[playlistId], progress.cached > 0 {
                progress.cached -= 1
                playlistDownloadProgress[playlistId] = progress
            }
        }

        showToast("Removed \"\(track.title)\" from downloads")
    }

    /// Saves the original file of a track wherever the user chooses.
    func saveRawFile(_ track: TrackInfo, to destination: URL) async {
        do {
            let file = try await api.downloadSong(songId: track.id, quality: .raw)
            try? FileManager.default.removeItem(at: destination)
            try FileManager.default.moveItem(at: file, to: destination)
            showToast("Saved \"\(destination.lastPathComponent)\"", kind: .success)
        } catch {
            showToast("Failed to download raw file", kind: .error)
        }
    }

    func startPlaylistCaching(_ playlistId: String) async {
        guard isOnline else {
            showToast("Cannot download while offline", kind: .error)
            return
        }

        await store.setPlaylistOffline(id: playlistId, enabled: true)
        offlinePlaylistIds.insert(playlistId)

        let tracks: [TrackInfo]
        do {
            tracks = try await api.playlistTracks(playlistId: playlistId).tracks
            if let playlist = playlists.first(where: { $0.id == playlistId }) {
                await store.saveCachedPlaylist(playlist, tracks: tracks)
            }
        } catch {
            showToast("Failed to start download", kind: .error)
            return
        }

        playlistDownloadProgress[playlistId] = progress(for: tracks)
        let uncached = tracks.filter { !cachedTrackIds.contains($0.id) }
        if uncached.isEmpty {
            showToast("Playlist already cached", kind: .success)
            return
        }

        showToast("Downloading \(uncached.count) tracks...")
        await downloads.queuePlaylistDownload(uncached, playlistId: playlistId, quality: settings.downloadQuality)
        // Already downloaded tracks shared with other playlists are now linked to this one too
        for track in tracks where cachedTrackIds.contains(track.id) {
            await store.addPlaylist(playlistId, toTrack: track.id)
        }
    }

    /// Stops caching a playlist and deletes its downloaded tracks (the caller confirms first).
    func stopPlaylistCaching(_ playlistId: String) async {
        downloads.cancelPlaylistDownloads(playlistId: playlistId)
        await store.setPlaylistOffline(id: playlistId, enabled: false)
        offlinePlaylistIds.remove(playlistId)
        await downloads.deletePlaylistTracks(playlistId: playlistId)
        playlistDownloadProgress[playlistId] = nil
        await store.cleanupOrphanedTracks()
        await downloads.refreshCacheState()
        setCachedTrackIds(await store.cachedTrackIds())
        showToast("Removed offline playlist")
    }

    private func handleDownloadEvent(_ event: DownloadEvent) {
        switch event {
        case let .completed(trackId, playlistIds):
            if !cachedTrackIds.contains(trackId) {
                var ids = cachedTrackIds
                ids.insert(trackId)
                setCachedTrackIds(ids)
            }

            for playlistId in playlistIds {
                if var progress = playlistDownloadProgress[playlistId], progress.cached < progress.total {
                    progress.cached += 1
                    playlistDownloadProgress[playlistId] = progress
                }
            }
        case let .failed(_, message):
            showToast("Download failed: \(message)", kind: .error)
        }
    }

    private func setCachedTrackIds(_ ids: Set<String>) {
        cachedTrackIds = ids
        player.cachedTrackIds = ids
    }

    // MARK: Cache maintenance

    func storageUsage() async -> StorageUsage {
        await store.storageUsage()
    }

    func clearAllCachedData() async {
        downloads.clearQueue()
        await store.clearAll()
        await downloads.refreshCacheState()
        setCachedTrackIds([])
        offlinePlaylistIds = []
        playlistDownloadProgress = [:]
        coverLoader.clearMemoryCache()
        showToast("All cached data cleared")
    }

    func clearCovers() async {
        await store.clearCovers()
        coverLoader.clearMemoryCache()
        showToast("Cover cache cleared", kind: .success)
    }

    func cleanupOrphanedTracks() async {
        let count = await store.cleanupOrphanedTracks()
        await downloads.refreshCacheState()
        setCachedTrackIds(await store.cachedTrackIds())
        showToast("Removed \(count) orphaned tracks", kind: .success)
    }

    func forceRefreshPlaylists() async {
        beginLoading()
        defer { endLoading() }
        await syncPlaylistsInternal(refreshAllTracks: true)
        showToast("Playlists refreshed", kind: .success)
    }

    // MARK: Server actions

    func triggerLibraryScan(force: Bool) async {
        guard isOnline else {
            showToast("Cannot trigger scan while offline", kind: .error)
            return
        }

        guard api.isConfigured else {
            showToast("Server is not configured", kind: .error)
            return
        }

        let baseline = try? await api.scanStatus()
        do {
            try await api.triggerScan(force: force)
        } catch {
            showToast(force ? "Failed to trigger force library scan" : "Failed to trigger library scan", kind: .error)
            return
        }

        showToast(force ? "Force library scan started" : "Library scan started", kind: .success)
        monitorScan(baseline: baseline)
    }

    /// Polls the scan status and refreshes the playlists once the scan completes.
    private func monitorScan(baseline: ScanStatusResponse?) {
        scanMonitorTask?.cancel()
        scanMonitorTask = Task {
            try? await Task.sleep(for: .seconds(1))
            var observedActiveScan = false
            for _ in 0..<120 {
                guard !Task.isCancelled else {
                    return
                }

                guard let status = try? await api.scanStatus() else {
                    return
                }

                invalidPlaylists = status.invalidPlaylists
                observedActiveScan = observedActiveScan || status.isScanning

                let generationAdvanced = baseline?.lastCompletedScanGeneration.map { base in (status.lastCompletedScanGeneration ?? base) > base } ?? false
                let dateAdvanced: Bool = {
                    guard let base = baseline?.lastScanDate.flatMap(DateParsing.parse), let current = status.lastScanDate.flatMap(DateParsing.parse) else {
                        return false
                    }

                    return current > base
                }()

                if generationAdvanced || dateAdvanced || (observedActiveScan && !status.isScanning) {
                    await syncPlaylistsInternal(refreshAllTracks: true)
                    return
                }

                try? await Task.sleep(for: .seconds(2))
            }

            await syncPlaylistsInternal(refreshAllTracks: true)
        }
    }

    func cleanupTranscodingCache() async {
        guard isOnline else {
            showToast("Cannot clean transcoding cache while offline", kind: .error)
            return
        }

        guard api.isConfigured else {
            showToast("Server is not configured", kind: .error)
            return
        }

        do {
            let response = try await api.cleanupTranscodingCache()
            if response.failedFileCount > 0 {
                showToast("Transcoding cache cleanup completed (\(response.deletedFileCount) deleted, \(response.failedFileCount) failed)", kind: .error)
            } else if response.deletedFileCount > 0 {
                showToast("Transcoding cache cleaned (\(response.deletedFileCount) files removed)", kind: .success)
            } else {
                showToast("Transcoding cache is already clean")
            }
        } catch {
            showToast("Failed to clean transcoding cache", kind: .error)
        }
    }

    func scanStatus() async -> ScanStatusResponse? {
        guard isOnline, api.isConfigured else {
            return nil
        }

        return try? await api.scanStatus()
    }

    // MARK: Memory

    /// Called when the app's windows appear or disappear from the screen.
    func setUIVisible(_ visible: Bool) {
        guard visible != isUIVisible else {
            return
        }

        isUIVisible = visible
        player.isUIVisible = visible
        if !visible {
            releaseMemory()
        } else if !isTrackListShown {
            Task { await reloadTrackList() }
        }
    }

    /// The main window was closed: release the displayed tracks. They are reloaded from the disk cache
    /// when a window is shown again.
    func mainWindowDidClose() {
        guard isTrackListShown else {
            return
        }

        isTrackListShown = false
        selectedPlaylistTracks = []
        releaseMemory()
        // SwiftUI tears the window's views down after this notification: release again once it is done
        Task {
            try? await Task.sleep(for: .seconds(2))
            releaseMemory()
        }
    }

    private func reloadTrackList() async {
        isTrackListShown = true
        guard isInitialized, let selectedPlaylistId, selectedPlaylistTracks.isEmpty else {
            return
        }

        _ = await loadPlaylistTracks(playlistId: selectedPlaylistId)
    }

    /// Drops what can be rebuilt (decoded covers) and gives freed memory back to the system.
    func releaseMemory() {
        coverLoader.clearMemoryCache()
        malloc_zone_pressure_relief(nil, 0)
    }

    private func startMemoryPressureMonitoring() {
        let source = DispatchSource.makeMemoryPressureSource(eventMask: [.warning, .critical], queue: .main)
        source.setEventHandler { [weak self] in
            MainActor.assumeIsolated {
                self?.releaseMemory()
            }
        }
        source.resume()
        memoryPressureSource = source
    }

    // MARK: Lifecycle

    func applicationWillTerminate() {
        let state = player.currentPlaybackState()
        player.shutdown()

        // Save synchronously: the main actor is blocked while waiting, so use a detached task
        let store = store
        let semaphore = DispatchSemaphore(value: 0)
        Task.detached {
            await store.savePlaybackState(state)
            await store.flush()
            semaphore.signal()
        }
        _ = semaphore.wait(timeout: .now() + 1)
    }
}

/// Holds the API client shared by the services, replaced when the server URL changes.
@MainActor
private final class APIClientBox {
    var client = APIClient(baseUrl: "")
}

extension Logger {
    static let app = Logger(subsystem: "net.meziantou.music", category: "app")
}

enum DefaultsKeys {
    static let lastViewedPlaylistId = "lastViewedPlaylistId"
    static let showRemainingTime = "showRemainingTime"
    static let skippedUpdateVersion = "skippedUpdateVersion"
    static let trackSortOption = "trackSortOption"
    static let trackSortDirection = "trackSortDirection"
}
