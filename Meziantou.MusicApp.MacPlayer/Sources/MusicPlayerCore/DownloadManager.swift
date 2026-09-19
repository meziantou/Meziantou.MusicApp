import Foundation

public enum DownloadEvent: Sendable {
    /// A track was downloaded; `playlistIds` lists the playlists it was downloaded for.
    case completed(trackId: String, playlistIds: [String])
    case failed(trackId: String, message: String)
}

/// Downloads tracks for offline playback with a bounded number of concurrent downloads.
@MainActor
public final class DownloadManager {
    private struct PendingDownload {
        var track: TrackInfo
        var playlistIds: Set<String>
        var quality: StreamingQuality
    }

    private let store: LibraryStore
    private let clientProvider: @MainActor () -> APIClient
    private var pendingOrder: [String] = []
    private var pending: [String: PendingDownload] = [:]
    private var active: Set<String> = []
    private var cachedTrackIds: Set<String> = []
    private let maxConcurrentDownloads: Int

    public var onEvent: ((DownloadEvent) -> Void)?

    public init(store: LibraryStore, clientProvider: @escaping @MainActor () -> APIClient) {
        self.store = store
        self.clientProvider = clientProvider
        maxConcurrentDownloads = max(2, min(8, ProcessInfo.processInfo.activeProcessorCount / 2))
    }

    public func refreshCacheState() async {
        cachedTrackIds = await store.cachedTrackIds()
    }

    public func isTrackCached(_ trackId: String) -> Bool {
        cachedTrackIds.contains(trackId)
    }

    public func isTrackDownloading(_ trackId: String) -> Bool {
        active.contains(trackId) || pending[trackId] != nil
    }

    /// Number of queued and in-progress downloads.
    public var queueSize: Int {
        pending.count + active.count
    }

    public func queueDownload(_ track: TrackInfo, playlistId: String, quality: StreamingQuality) async {
        if cachedTrackIds.contains(track.id) {
            await store.addPlaylist(playlistId, toTrack: track.id)
            return
        }

        if pending[track.id] != nil {
            pending[track.id]?.playlistIds.insert(playlistId)
            return
        }

        pending[track.id] = PendingDownload(track: track, playlistIds: [playlistId], quality: quality)
        pendingOrder.append(track.id)
        processQueue()
    }

    public func queuePlaylistDownload(_ tracks: [TrackInfo], playlistId: String, quality: StreamingQuality) async {
        for track in tracks {
            await queueDownload(track, playlistId: playlistId, quality: quality)
        }
    }

    public func cancelDownload(trackId: String) {
        pending.removeValue(forKey: trackId)
        pendingOrder.removeAll { $0 == trackId }
    }

    public func cancelPlaylistDownloads(playlistId: String) {
        var cancelledTrackIds = Set<String>()
        for trackId in Array(pending.keys) {
            pending[trackId]?.playlistIds.remove(playlistId)
            if pending[trackId]?.playlistIds.isEmpty == true {
                pending.removeValue(forKey: trackId)
                cancelledTrackIds.insert(trackId)
            }
        }

        // Filter the order once: removing the tracks one by one is quadratic for large playlists
        if !cancelledTrackIds.isEmpty {
            pendingOrder.removeAll { cancelledTrackIds.contains($0) }
        }
    }

    public func clearQueue() {
        pending = [:]
        pendingOrder = []
    }

    public func deleteTrack(trackId: String) async {
        await store.deleteCachedTrack(id: trackId)
        cachedTrackIds.remove(trackId)
    }

    public func deletePlaylistTracks(playlistId: String) async {
        for entry in await store.cachedTracks(playlistId: playlistId) {
            await store.removePlaylist(playlistId, fromTrack: entry.trackId)
            if await store.cachedTrack(id: entry.trackId) == nil {
                cachedTrackIds.remove(entry.trackId)
            }
        }
    }

    private func processQueue() {
        while active.count < maxConcurrentDownloads, !pendingOrder.isEmpty {
            let trackId = pendingOrder.removeFirst()
            guard let download = pending.removeValue(forKey: trackId) else {
                continue
            }

            active.insert(trackId)
            let client = clientProvider()
            Task {
                await self.download(download, client: client)
                self.active.remove(trackId)
                self.processQueue()
            }
        }
    }

    private func download(_ download: PendingDownload, client: APIClient) async {
        let trackId = download.track.id
        let playlistIds = download.playlistIds.sorted()
        do {
            let file = try await client.downloadSong(songId: trackId, quality: download.quality)
            try await store.saveCachedTrack(trackId: trackId, playlistIds: playlistIds, quality: download.quality, file: file)
            await downloadCoverIfNeeded(trackId: trackId, client: client)
            cachedTrackIds.insert(trackId)
            onEvent?(.completed(trackId: trackId, playlistIds: playlistIds))
        } catch {
            onEvent?(.failed(trackId: trackId, message: error.localizedDescription))
        }
    }

    private func downloadCoverIfNeeded(trackId: String, client: APIClient) async {
        guard await !store.hasCachedCover(trackId: trackId), await !store.isCoverMissing(trackId: trackId) else {
            return
        }

        do {
            if let data = try await client.coverData(songId: trackId, size: 256) {
                await store.saveCover(trackId: trackId, data: data)
            } else {
                await store.addMissingCover(trackId: trackId)
            }
        } catch {
            // Covers are best-effort
        }
    }
}
