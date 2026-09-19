import Foundation
import Testing
@testable import MusicPlayerCore

private func makeStore() async throws -> LibraryStore {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("MusicPlayerCoreTests-\(UUID().uuidString)", isDirectory: true)
    let store = LibraryStore(rootDirectory: directory)
    try await store.initialize()
    return store
}

private func makeAudioFile(_ bytes: [UInt8] = [1, 2, 3], fileExtension: String = "mp3") throws -> URL {
    let url = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).\(fileExtension)")
    try Data(bytes).write(to: url)
    return url
}

struct LibraryStoreTests {
    @Test func roundTripsSettings() async throws {
        let store = try await makeStore()
        #expect(await store.settings() == AppSettings())

        var settings = AppSettings()
        settings.serverUrl = "https://music.example.com"
        settings.replayGainMode = .album
        settings.showInMenuBar = true
        await store.saveSettings(settings)
        #expect(await store.settings() == settings)
    }

    @Test func settingsDecodeMissingKeysWithDefaultsAndIgnoreUnknownKeys() throws {
        let settings = try JSONDecoder().decode(AppSettings.self, from: Data(#"{"serverUrl":"http://x","equalizerGains":[30]}"#.utf8))
        #expect(settings.serverUrl == "http://x")
        #expect(settings.normalQuality == StreamingQuality(format: .opus, maxBitRate: 160))
        #expect(settings.showReplayGainWarning)
        #expect(!settings.showInMenuBar)
    }

    @Test func roundTripsPlaybackState() async throws {
        let store = try await makeStore()
        var state = PlaybackState()
        state.currentPlaylistId = "p"
        state.currentTime = 42
        state.repeatMode = .all
        state.queue = [QueueItem(track: TrackInfo(id: "1", title: "t"), playlistId: "p", indexInPlaylist: 0, source: .playlist)]
        await store.savePlaybackState(state)
        #expect(await store.playbackState() == state)
    }

    @Test func queueItemsWithoutSourceAreManual() throws {
        let item = try JSONDecoder().decode(QueueItem.self, from: Data(#"{"track":{"id":"1","title":"t","path":"","duration":1,"size":1},"playlistId":"p","indexInPlaylist":2}"#.utf8))
        #expect(item.source == .manual)
    }

    @Test func storesPlaylistsSortedByOrder() async throws {
        let store = try await makeStore()
        await store.saveCachedPlaylist(PlaylistSummary(id: "b/2", name: "B", sortOrder: 2), tracks: [])
        await store.saveCachedPlaylist(PlaylistSummary(id: "a:1", name: "A", sortOrder: 1), tracks: [TrackInfo(id: "t", title: "T")])
        #expect(await store.cachedPlaylistSummaries().map(\.id) == ["a:1", "b/2"])
        #expect(await store.cachedPlaylistSummary(id: "b/2")?.name == "B")
        #expect(await store.cachedPlaylist(id: "a:1")?.tracks.map(\.id) == ["t"])

        await store.deleteCachedPlaylist(id: "a:1")
        #expect(await store.cachedPlaylist(id: "a:1") == nil)
        #expect(await store.cachedPlaylistSummaries().map(\.id) == ["b/2"])

        let reopened = LibraryStore(rootDirectory: store.rootDirectory)
        try await reopened.initialize()
        #expect(await reopened.cachedPlaylistSummaries().map(\.id) == ["b/2"])
    }

    @Test func buildsPlaylistIndexFromExistingFiles() async throws {
        let store = try await makeStore()
        await store.saveCachedPlaylist(PlaylistSummary(id: "p1", name: "P1", sortOrder: 1), tracks: [TrackInfo(id: "t", title: "T")])
        try FileManager.default.removeItem(at: store.rootDirectory.appendingPathComponent("Playlists/index.json"))

        let reopened = LibraryStore(rootDirectory: store.rootDirectory)
        try await reopened.initialize()
        #expect(await reopened.cachedPlaylistSummaries().map(\.id) == ["p1"])
        #expect(await reopened.cachedPlaylist(id: "p1")?.tracks.map(\.id) == ["t"])
    }

    @Test func cachesTracksPerPlaylist() async throws {
        let store = try await makeStore()
        try await store.saveCachedTrack(trackId: "t1", playlistIds: ["p1"], quality: .raw, file: try makeAudioFile())
        await store.addPlaylist("p2", toTrack: "t1")
        #expect(await store.cachedTrackIds() == ["t1"])
        #expect(await store.cachedTrack(id: "t1")?.playlistIds.sorted() == ["p1", "p2"])
        let fileUrl = try #require(await store.cachedTrackFileUrl(id: "t1"))
        #expect(try Data(contentsOf: fileUrl) == Data([1, 2, 3]))

        await store.removePlaylist("p1", fromTrack: "t1")
        #expect(await store.cachedTrack(id: "t1")?.playlistIds == ["p2"])

        await store.removePlaylist("p2", fromTrack: "t1")
        #expect(await store.cachedTrack(id: "t1") == nil)
        #expect(!FileManager.default.fileExists(atPath: fileUrl.path))
    }

    @Test func reloadsIndexesFromDisk() async throws {
        let store = try await makeStore()
        try await store.saveCachedTrack(trackId: "t1", playlistIds: ["p1"], quality: .raw, file: try makeAudioFile())
        await store.setPlaylistOffline(id: "p1", enabled: true)
        await store.flush()

        let reopened = LibraryStore(rootDirectory: store.rootDirectory)
        try await reopened.initialize()
        #expect(await reopened.cachedTrackIds() == ["t1"])
        #expect(await reopened.offlinePlaylistIds() == ["p1"])
    }

    @Test func writesIndexChangesAfterADelay() async throws {
        let store = try await makeStore()
        try await store.saveCachedTrack(trackId: "t1", playlistIds: ["p1"], quality: .raw, file: try makeAudioFile())
        await store.saveCover(trackId: "t1", data: Data([1]))
        await store.addMissingCover(trackId: "t2")
        let trackIndexUrl = store.rootDirectory.appendingPathComponent("Tracks/index.json")
        #expect(!FileManager.default.fileExists(atPath: trackIndexUrl.path))

        try await Task.sleep(for: .seconds(3))
        let reopened = LibraryStore(rootDirectory: store.rootDirectory)
        try await reopened.initialize()
        #expect(await reopened.cachedTrackIds() == ["t1"])
        #expect(await reopened.coverCount() == 1)
        #expect(await reopened.isCoverMissing(trackId: "t2"))
    }

    @Test func cleansUpOrphanedTracks() async throws {
        let store = try await makeStore()
        await store.setPlaylistOffline(id: "p1", enabled: true)
        try await store.saveCachedTrack(trackId: "kept", playlistIds: ["p1", "gone"], quality: .raw, file: try makeAudioFile())
        try await store.saveCachedTrack(trackId: "orphan", playlistIds: ["gone"], quality: .raw, file: try makeAudioFile())

        #expect(await store.cleanupOrphanedTracks() == 1)
        #expect(await store.cachedTrackIds() == ["kept"])
        #expect(await store.cachedTrack(id: "kept")?.playlistIds == ["p1"])
    }

    @Test func verifiesOfflinePlaylistIntegrity() async throws {
        let store = try await makeStore()
        await store.saveCachedPlaylist(PlaylistSummary(id: "p1", name: "P1"), tracks: [])
        await store.setPlaylistOffline(id: "p1", enabled: true)
        await store.setPlaylistOffline(id: "p2", enabled: true)
        #expect(await store.verifyOfflinePlaylistsIntegrity() == ["p2"])
        #expect(await store.offlinePlaylistIds() == ["p1"])
    }

    @Test func evictsLeastRecentlyUsedCoversButKeepsDownloadedOnes() async throws {
        let store = try await makeStore()
        try await store.saveCachedTrack(trackId: "downloaded", playlistIds: ["p"], quality: .raw, file: try makeAudioFile())
        await store.saveCover(trackId: "downloaded", data: Data([0]))
        for index in 0..<5 {
            await store.saveCover(trackId: "c\(index)", data: Data([UInt8(index)]))
        }

        // Touch c0 so it becomes the most recently used
        #expect(await store.cachedCover(trackId: "c0") == Data([0]))
        await store.evictOldCovers(maxEntries: 3)

        #expect(await store.coverCount() == 3)
        #expect(await store.cachedCover(trackId: "downloaded") != nil)
        #expect(await store.cachedCover(trackId: "c0") != nil)
        #expect(await store.cachedCover(trackId: "c1") == nil)
    }

    @Test func tracksMissingCovers() async throws {
        let store = try await makeStore()
        await store.addMissingCover(trackId: "x")
        #expect(await store.isCoverMissing(trackId: "x"))
        #expect(await !store.hasCachedCover(trackId: "x"))
        await store.saveCover(trackId: "x", data: Data([1]))
        #expect(await !store.isCoverMissing(trackId: "x"))
        #expect(await store.hasCachedCover(trackId: "x"))
        await store.addMissingCover(trackId: "y")
        await store.clearCovers()
        #expect(await !store.isCoverMissing(trackId: "y"))
        #expect(await !store.hasCachedCover(trackId: "x"))
        #expect(await store.cachedCover(trackId: "x") == nil)
    }

    @Test func limitsRecentlyPlayed() async throws {
        let store = try await makeStore()
        for index in 0..<5 {
            await store.addRecentlyPlayed(trackId: "t\(index)", maxCount: 3)
            try await Task.sleep(for: .milliseconds(2))
        }

        #expect(await store.recentlyPlayedIds() == ["t4", "t3", "t2"])
    }

    @Test func clearAllRemovesEverything() async throws {
        let store = try await makeStore()
        await store.saveCachedPlaylist(PlaylistSummary(id: "p1", name: "P1"), tracks: [])
        await store.setPlaylistOffline(id: "p1", enabled: true)
        try await store.saveCachedTrack(trackId: "t1", playlistIds: ["p1"], quality: .raw, file: try makeAudioFile())
        await store.saveCover(trackId: "t1", data: Data([1]))

        await store.clearAll()
        #expect(await store.cachedTrackIds().isEmpty)
        #expect(await store.cachedPlaylistSummaries().isEmpty)
        #expect(await store.offlinePlaylistIds().isEmpty)
        #expect(await store.coverCount() == 0)
        #expect(await store.storageUsage().usedBytes >= 0)
    }
}

@MainActor
struct DownloadManagerTests {
    @Test func downloadsTrackAndCoverThenReportsCompletion() async throws {
        MockURLProtocol.register(host: "downloads.test") { request in
            if request.url?.path.hasSuffix("/cover") == true {
                return (200, ["Content-Type": "image/jpeg"], Data([7]))
            }

            #expect(request.url?.query == "format=mp3&maxBitRate=128")
            return (200, ["Content-Type": "audio/mpeg"], Data([1, 2, 3, 4]))
        }

        let store = try await makeStore()
        let client = APIClient(baseUrl: "https://downloads.test", session: MockURLProtocol.session())
        let manager = DownloadManager(store: store) { client }
        let completed = AsyncStream.makeStream(of: DownloadEvent.self)
        manager.onEvent = { completed.continuation.yield($0) }

        await manager.queueDownload(TrackInfo(id: "t1", title: "t"), playlistId: "p1", quality: StreamingQuality(format: .mp3, maxBitRate: 128))
        await manager.queueDownload(TrackInfo(id: "t1", title: "t"), playlistId: "p2", quality: StreamingQuality(format: .mp3, maxBitRate: 128))

        var iterator = completed.stream.makeAsyncIterator()
        let event = await iterator.next()
        guard case let .completed(trackId, _) = event else {
            Issue.record("Unexpected event \(String(describing: event))")
            return
        }

        #expect(trackId == "t1")
        #expect(manager.isTrackCached("t1"))
        #expect(await store.cachedTrack(id: "t1")?.quality == StreamingQuality(format: .mp3, maxBitRate: 128))
        #expect(await store.cachedCover(trackId: "t1") == Data([7]))

        // Already cached: only links the playlist
        await manager.queueDownload(TrackInfo(id: "t1", title: "t"), playlistId: "p3", quality: .raw)
        #expect(await store.cachedTrack(id: "t1")?.playlistIds.contains("p3") == true)
        #expect(manager.queueSize == 0)
    }

    @Test func reportsFailures() async throws {
        MockURLProtocol.register(host: "failing.test") { _ in
            (500, [:], Data())
        }

        let store = try await makeStore()
        let client = APIClient(baseUrl: "https://failing.test", session: MockURLProtocol.session())
        let manager = DownloadManager(store: store) { client }
        let events = AsyncStream.makeStream(of: DownloadEvent.self)
        manager.onEvent = { events.continuation.yield($0) }

        await manager.queueDownload(TrackInfo(id: "t1", title: "t"), playlistId: "p1", quality: .raw)
        var iterator = events.stream.makeAsyncIterator()
        guard case let .failed(trackId, message) = await iterator.next() else {
            Issue.record("Expected a failure")
            return
        }

        #expect(trackId == "t1")
        #expect(message == "HTTP 500")
        #expect(!manager.isTrackCached("t1"))
    }

    @Test func cancelsPlaylistDownloads() async throws {
        let store = try await makeStore()
        let manager = DownloadManager(store: store) { APIClient(baseUrl: "https://never.test", session: MockURLProtocol.session()) }
        let tracks = (0..<20).map { TrackInfo(id: "t\($0)", title: "t") }
        await manager.queuePlaylistDownload(tracks, playlistId: "p1", quality: .raw)
        #expect(manager.queueSize == 20)
        manager.cancelPlaylistDownloads(playlistId: "p1")
        #expect(manager.queueSize <= 8)
    }
}
