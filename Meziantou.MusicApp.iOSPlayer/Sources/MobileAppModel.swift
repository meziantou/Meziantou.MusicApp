import Foundation
import MusicPlayerCore
import Network
import Observation

@MainActor
@Observable
final class MobileAppModel {
    private let store = LibraryStore(rootDirectory: LibraryStore.defaultRootDirectory())
    private let apiBox = APIClientBox()
    private let monitor = NWPathMonitor()
    private var downloadManager: DownloadManager!
    private var syncTask: Task<Void, Never>?

    private(set) var settings = AppSettings()
    private(set) var playlists: [PlaylistSummary] = []
    var selectedPlaylistId: String?
    private(set) var tracks: [TrackInfo] = []
    private(set) var cachedTrackIds: Set<String> = []
    private(set) var offlinePlaylistIds: Set<String> = []
    private(set) var isOnline = true
    private(set) var isInitialized = false
    var message: String?
    var isShowingSettings = false
    let player: MobilePlayerController

    init() {
        player = MobilePlayerController(store: store, clientProvider: { [apiBox] in apiBox.client })
        downloadManager = DownloadManager(store: store, clientProvider: { [apiBox] in apiBox.client })
        player.onError = { [weak self] in self?.show($0) }
        player.onTrackChanged = { [weak self] _ in self?.savePlayback() }
        downloadManager.onEvent = { [weak self] event in
            switch event {
            case let .completed(trackId, _):
                self?.cachedTrackIds.insert(trackId)
                self?.player.cachedTrackIds.insert(trackId)
            case let .failed(_, message):
                self?.show("Download failed: \(message)")
            }
        }
    }

    func initialize() async {
        guard !isInitialized else {
            return
        }

        do {
            try await store.initialize()
        } catch {
            show("Failed to open local storage: \(error.localizedDescription)")
        }

        settings = await store.settings()
        apiBox.client = APIClient(baseUrl: settings.serverUrl)
        cachedTrackIds = await store.cachedTrackIds()
        offlinePlaylistIds = await store.offlinePlaylistIds()
        player.cachedTrackIds = cachedTrackIds
        player.quality = settings.normalQuality
        await downloadManager.refreshCacheState()
        startNetworkMonitoring()
        isInitialized = true
        await refresh()
        let playback = await store.playbackState()
        if let playlistId = playback.currentPlaylistId, let cached = await store.cachedPlaylist(id: playlistId) {
            selectedPlaylistId = playlistId
            tracks = cached.tracks
            player.restore(playback, tracks: tracks)
        }
    }

    func refresh() async {
        guard !settings.serverUrl.isEmpty else {
            playlists = await store.cachedPlaylistSummaries()
            return
        }

        guard isOnline else {
            playlists = await store.cachedPlaylistSummaries()
            return
        }

        do {
            let response = try await apiBox.client.playlists()
            playlists = response.playlists.sorted { $0.sortOrder < $1.sortOrder }
            for playlist in playlists {
                if offlinePlaylistIds.contains(playlist.id) {
                    let response = try await apiBox.client.playlistTracks(playlistId: playlist.id)
                    await store.saveCachedPlaylist(playlist, tracks: response.tracks)
                }
            }
        } catch {
            playlists = await store.cachedPlaylistSummaries()
            show("Server unavailable. Showing cached playlists.")
        }
    }

    func select(_ playlist: PlaylistSummary) async {
        selectedPlaylistId = playlist.id
        UserDefaults.standard.set(playlist.id, forKey: "lastViewedPlaylistId")
        if let cached = await store.cachedPlaylist(id: playlist.id) {
            guard selectedPlaylistId == playlist.id else {
                return
            }

            tracks = cached.tracks
        }

        guard isOnline else {
            return
        }

        do {
            let response = try await apiBox.client.playlistTracks(playlistId: playlist.id)
            guard selectedPlaylistId == playlist.id else {
                return
            }

            tracks = response.tracks
            await store.saveCachedPlaylist(playlist, tracks: response.tracks)
        } catch {
            if tracks.isEmpty {
                show("Failed to load playlist: \(error.localizedDescription)")
            }
        }
    }

    func play(_ track: TrackInfo) {
        guard let index = tracks.firstIndex(of: track), let playlistId = selectedPlaylistId else {
            return
        }

        player.quality = settings.streamingQuality(for: .normal)
        player.isOnline = isOnline
        player.play(playlistId: playlistId, tracks: tracks, startIndex: index)
    }

    func toggleShuffle() {
        player.setShuffle(!player.shuffleEnabled)
        savePlayback()
    }

    func cycleRepeatMode() {
        player.cycleRepeatMode()
        savePlayback()
    }

    func toggleOffline(_ playlist: PlaylistSummary) async {
        if offlinePlaylistIds.contains(playlist.id) {
            downloadManager.cancelPlaylistDownloads(playlistId: playlist.id)
            await store.setPlaylistOffline(id: playlist.id, enabled: false)
            await downloadManager.deletePlaylistTracks(playlistId: playlist.id)
            offlinePlaylistIds.remove(playlist.id)
            cachedTrackIds = await store.cachedTrackIds()
            player.cachedTrackIds = cachedTrackIds
            return
        }

        guard isOnline else {
            show("Connect to the server before downloading a playlist.")
            return
        }

        do {
            let response = try await apiBox.client.playlistTracks(playlistId: playlist.id)
            await store.saveCachedPlaylist(playlist, tracks: response.tracks)
            await store.setPlaylistOffline(id: playlist.id, enabled: true)
            offlinePlaylistIds.insert(playlist.id)
            await downloadManager.queuePlaylistDownload(response.tracks, playlistId: playlist.id, quality: settings.downloadQuality)
        } catch {
            show("Failed to download playlist: \(error.localizedDescription)")
        }
    }

    func saveSettings(_ settings: AppSettings) async {
        self.settings = settings
        await store.saveSettings(settings)
        apiBox.client = APIClient(baseUrl: settings.serverUrl)
        player.quality = settings.normalQuality
        await refresh()
    }

    func savePlayback() {
        let state = player.playbackState(playlistId: selectedPlaylistId)
        Task {
            await store.savePlaybackState(state)
        }
    }

    func applicationDidBecomeActive() {
        Task { await refresh() }
    }

    private func startNetworkMonitoring() {
        monitor.pathUpdateHandler = { [weak self] path in
            Task { @MainActor in
                self?.isOnline = path.status == .satisfied
                self?.player.isOnline = path.status == .satisfied
            }
        }
        monitor.start(queue: DispatchQueue(label: "net.meziantou.music.ios.network"))
        syncTask = Task {
            while !Task.isCancelled {
                try? await Task.sleep(for: PlaybackConstants.playlistSyncInterval)
                await refresh()
            }
        }
    }

    private func show(_ message: String) {
        self.message = message
        Task {
            try? await Task.sleep(for: .seconds(4))
            if self.message == message {
                self.message = nil
            }
        }
    }
}

@MainActor
private final class APIClientBox {
    var client = APIClient(baseUrl: "")
}
