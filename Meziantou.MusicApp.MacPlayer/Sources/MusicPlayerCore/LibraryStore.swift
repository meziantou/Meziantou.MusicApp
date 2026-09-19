import CryptoKit
import Foundation

public struct CachedTrackEntry: Codable, Equatable, Sendable {
    public var trackId: String
    public var playlistIds: [String]
    public var quality: StreamingQuality
    public var cachedAt: Date
    public var fileName: String
    public var size: Int64
}

public struct StorageUsage: Sendable {
    public var usedBytes: Int64
    public var availableBytes: Int64

    public var quotaBytes: Int64 {
        usedBytes + availableBytes
    }
}

/// File-based persistence for the player (the native equivalent of the web player's IndexedDB).
///
/// Layout of the root directory:
/// - `settings.json`, `playback-state.json`, `offline-playlists.json`, `recently-played.json`
/// - `Playlists/index.json`: summaries of the cached playlists
/// - `Playlists/<hash>.json`: playlist metadata and tracks
/// - `Tracks/index.json` and `Tracks/<hash>.<ext>`: downloaded tracks
/// - `Covers/index.json`, `Covers/missing.json` and `Covers/<hash>`: cover art
public actor LibraryStore {
    public nonisolated let rootDirectory: URL

    private var playlistsDirectory: URL { rootDirectory.appendingPathComponent("Playlists", isDirectory: true) }
    private var tracksDirectory: URL { rootDirectory.appendingPathComponent("Tracks", isDirectory: true) }
    private var coversDirectory: URL { rootDirectory.appendingPathComponent("Covers", isDirectory: true) }

    private var trackIndex: [String: CachedTrackEntry] = [:]
    /// Summaries of the cached playlists, so listing them does not decode every track.
    private var playlistIndex: [String: PlaylistSummary] = [:]
    private var coverIndex: [String: Date] = [:]
    private var missingCovers: Set<String> = []
    private var offlinePlaylists: Set<String> = []
    private var recentlyPlayed: [String: Date] = [:]
    private var coverSavesSinceEviction = 0
    private var coverIndexDirty = false
    private var trackIndexDirty = false
    private var missingCoversDirty = false
    private var flushTask: Task<Void, Never>?

    /// Index changes are written together after this delay: rewriting a whole index for every downloaded
    /// track or cover makes downloading a large playlist quadratic.
    private static let flushDelay: Duration = .seconds(2)

    private let fileManager = FileManager.default
    private let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .millisecondsSince1970
        return encoder
    }()

    private let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .millisecondsSince1970
        return decoder
    }()

    public init(rootDirectory: URL) {
        self.rootDirectory = rootDirectory
    }

    /// The default location: `~/Library/Application Support/Meziantou Music`,
    /// or the `MEZIANTOU_MUSIC_DATA_DIR` environment variable when set (useful for development).
    public static func defaultRootDirectory() -> URL {
        if let override = ProcessInfo.processInfo.environment["MEZIANTOU_MUSIC_DATA_DIR"], !override.isEmpty {
            return URL(fileURLWithPath: override, isDirectory: true)
        }

        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
        return base.appendingPathComponent("Meziantou Music", isDirectory: true)
    }

    /// Creates the directories and loads the indexes.
    public func initialize() throws {
        for directory in [rootDirectory, playlistsDirectory, tracksDirectory, coversDirectory] {
            try fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
        }

        trackIndex = read([String: CachedTrackEntry].self, from: tracksDirectory.appendingPathComponent("index.json")) ?? [:]
        coverIndex = read([String: Date].self, from: coversDirectory.appendingPathComponent("index.json")) ?? [:]
        missingCovers = read(Set<String>.self, from: coversDirectory.appendingPathComponent("missing.json")) ?? []
        offlinePlaylists = read(Set<String>.self, from: rootDirectory.appendingPathComponent("offline-playlists.json")) ?? []
        recentlyPlayed = read([String: Date].self, from: rootDirectory.appendingPathComponent("recently-played.json")) ?? [:]

        if let index = read([String: PlaylistSummary].self, from: playlistIndexUrl) {
            playlistIndex = index
        } else {
            // Build the index once from the playlist files (data written before the index existed)
            let files = (try? fileManager.contentsOfDirectory(at: playlistsDirectory, includingPropertiesForKeys: nil)) ?? []
            for file in files where file.pathExtension == "json" && file != playlistIndexUrl {
                if let playlist = read(CachedPlaylist.self, from: file)?.playlist {
                    playlistIndex[playlist.id] = playlist
                }
            }

            persistPlaylistIndex()
        }

        // Drop index entries whose file disappeared
        let missingFiles = trackIndex.values.filter { !fileManager.fileExists(atPath: trackFileUrl(fileName: $0.fileName).path) }
        if !missingFiles.isEmpty {
            for entry in missingFiles {
                trackIndex.removeValue(forKey: entry.trackId)
            }

            scheduleTrackIndexSave()
        }
    }

    // MARK: Settings & playback state

    public func settings() -> AppSettings {
        read(AppSettings.self, from: rootDirectory.appendingPathComponent("settings.json")) ?? AppSettings()
    }

    public func saveSettings(_ settings: AppSettings) {
        write(settings, to: rootDirectory.appendingPathComponent("settings.json"))
    }

    public func playbackState() -> PlaybackState {
        read(PlaybackState.self, from: rootDirectory.appendingPathComponent("playback-state.json")) ?? PlaybackState()
    }

    public func savePlaybackState(_ state: PlaybackState) {
        write(state, to: rootDirectory.appendingPathComponent("playback-state.json"))
    }

    // MARK: Cached playlists

    public func cachedPlaylist(id: String) -> CachedPlaylist? {
        read(CachedPlaylist.self, from: playlistFileUrl(id: id))
    }

    public func saveCachedPlaylist(_ playlist: PlaylistSummary, tracks: [TrackInfo]) {
        write(CachedPlaylist(playlist: playlist, tracks: tracks), to: playlistFileUrl(id: playlist.id))
        if playlistIndex[playlist.id] != playlist {
            playlistIndex[playlist.id] = playlist
            persistPlaylistIndex()
        }
    }

    /// The summary of a cached playlist, without loading its tracks.
    public func cachedPlaylistSummary(id: String) -> PlaylistSummary? {
        playlistIndex[id]
    }

    /// Summaries of all cached playlists, sorted by their order, without loading their tracks.
    public func cachedPlaylistSummaries() -> [PlaylistSummary] {
        playlistIndex.values.sorted { $0.sortOrder < $1.sortOrder }
    }

    public func deleteCachedPlaylist(id: String) {
        try? fileManager.removeItem(at: playlistFileUrl(id: id))
        if playlistIndex.removeValue(forKey: id) != nil {
            persistPlaylistIndex()
        }
    }

    // MARK: Offline playlists

    public func offlinePlaylistIds() -> Set<String> {
        offlinePlaylists
    }

    public func setPlaylistOffline(id: String, enabled: Bool) {
        if enabled {
            offlinePlaylists.insert(id)
        } else {
            offlinePlaylists.remove(id)
        }

        write(offlinePlaylists, to: rootDirectory.appendingPathComponent("offline-playlists.json"))
    }

    /// Removes offline markers of playlists that are no longer cached. Returns the removed ids.
    public func verifyOfflinePlaylistsIntegrity() -> [String] {
        let cachedIds = Set(playlistIndex.keys)
        let orphaned = offlinePlaylists.filter { !cachedIds.contains($0) }
        if !orphaned.isEmpty {
            offlinePlaylists.subtract(orphaned)
            write(offlinePlaylists, to: rootDirectory.appendingPathComponent("offline-playlists.json"))
        }

        return Array(orphaned)
    }

    // MARK: Cached tracks

    public func cachedTrackIds() -> Set<String> {
        Set(trackIndex.keys)
    }

    public func cachedTrack(id: String) -> CachedTrackEntry? {
        trackIndex[id]
    }

    public func cachedTrackFileUrl(id: String) -> URL? {
        trackIndex[id].map { trackFileUrl(fileName: $0.fileName) }
    }

    public func cachedTracks(playlistId: String) -> [CachedTrackEntry] {
        trackIndex.values.filter { $0.playlistIds.contains(playlistId) }
    }

    /// Moves a downloaded file into the cache.
    public func saveCachedTrack(trackId: String, playlistIds: [String], quality: StreamingQuality, file: URL) throws {
        let fileExtension = file.pathExtension.isEmpty ? "audio" : file.pathExtension
        let fileName = "\(Self.hash(trackId)).\(fileExtension)"
        let destination = trackFileUrl(fileName: fileName)

        if let existing = trackIndex[trackId] {
            try? fileManager.removeItem(at: trackFileUrl(fileName: existing.fileName))
        }

        try? fileManager.removeItem(at: destination)
        try fileManager.moveItem(at: file, to: destination)
        let size = (try? destination.resourceValues(forKeys: [.fileSizeKey]).fileSize).map(Int64.init) ?? 0
        let mergedPlaylistIds = Array(Set(playlistIds).union(trackIndex[trackId]?.playlistIds ?? [])).sorted()
        trackIndex[trackId] = CachedTrackEntry(trackId: trackId, playlistIds: mergedPlaylistIds, quality: quality, cachedAt: Date(), fileName: fileName, size: size)
        scheduleTrackIndexSave()
    }

    public func addPlaylist(_ playlistId: String, toTrack trackId: String) {
        guard var entry = trackIndex[trackId], !entry.playlistIds.contains(playlistId) else {
            return
        }

        entry.playlistIds.append(playlistId)
        trackIndex[trackId] = entry
        scheduleTrackIndexSave()
    }

    /// Unlinks a playlist from a track, deleting the track when no playlist references it anymore.
    public func removePlaylist(_ playlistId: String, fromTrack trackId: String) {
        guard var entry = trackIndex[trackId] else {
            return
        }

        entry.playlistIds.removeAll { $0 == playlistId }
        if entry.playlistIds.isEmpty {
            deleteCachedTrack(id: trackId)
        } else {
            trackIndex[trackId] = entry
            scheduleTrackIndexSave()
        }
    }

    public func deleteCachedTrack(id: String) {
        guard let entry = trackIndex.removeValue(forKey: id) else {
            return
        }

        try? fileManager.removeItem(at: trackFileUrl(fileName: entry.fileName))
        scheduleTrackIndexSave()
    }

    public func clearCachedTracks() {
        for entry in trackIndex.values {
            try? fileManager.removeItem(at: trackFileUrl(fileName: entry.fileName))
        }

        trackIndex = [:]
        scheduleTrackIndexSave()
    }

    /// Removes tracks that do not belong to any offline playlist. Returns the number of removed tracks.
    @discardableResult
    public func cleanupOrphanedTracks() -> Int {
        var removedCount = 0
        for entry in Array(trackIndex.values) {
            let validPlaylistIds = entry.playlistIds.filter { offlinePlaylists.contains($0) }
            if validPlaylistIds.isEmpty {
                trackIndex.removeValue(forKey: entry.trackId)
                try? fileManager.removeItem(at: trackFileUrl(fileName: entry.fileName))
                removedCount += 1
            } else if validPlaylistIds.count != entry.playlistIds.count {
                var updated = entry
                updated.playlistIds = validPlaylistIds
                trackIndex[entry.trackId] = updated
            }
        }

        scheduleTrackIndexSave()
        return removedCount
    }

    // MARK: Covers

    public func cachedCover(trackId: String) -> Data? {
        guard coverIndex[trackId] != nil, let data = try? Data(contentsOf: coverFileUrl(trackId: trackId)) else {
            return nil
        }

        // Refresh the timestamp so frequently used covers survive the LRU eviction
        coverIndex[trackId] = Date()
        coverIndexDirty = true
        return data
    }

    /// Whether a cover is cached, without reading it or refreshing its timestamp.
    public func hasCachedCover(trackId: String) -> Bool {
        coverIndex[trackId] != nil
    }

    public func saveCover(trackId: String, data: Data) {
        do {
            try data.write(to: coverFileUrl(trackId: trackId), options: .atomic)
        } catch {
            return
        }

        coverIndex[trackId] = Date()
        missingCovers.remove(trackId)
        coverIndexDirty = true

        // Amortize the eviction to avoid scanning the index on every save
        coverSavesSinceEviction += 1
        if coverSavesSinceEviction >= 50 {
            coverSavesSinceEviction = 0
            evictOldCovers()
        }

        scheduleCoverIndexSave()
    }

    public func addMissingCover(trackId: String) {
        missingCovers.insert(trackId)
        missingCoversDirty = true
        scheduleFlush()
    }

    public func isCoverMissing(trackId: String) -> Bool {
        missingCovers.contains(trackId)
    }

    public func coverCount() -> Int {
        coverIndex.count
    }

    /// Evicts the least recently used covers above the limit. Covers of downloaded tracks are never evicted.
    public func evictOldCovers(maxEntries: Int = PlaybackConstants.coverCacheMaxEntries) {
        guard coverIndex.count > maxEntries else {
            return
        }

        let evictable = coverIndex.filter { trackIndex[$0.key] == nil }.sorted { $0.value < $1.value }
        let protectedCount = coverIndex.count - evictable.count
        let budget = max(0, maxEntries - protectedCount)
        let toDelete = evictable.count - budget
        guard toDelete > 0 else {
            return
        }

        for (trackId, _) in evictable.prefix(toDelete) {
            coverIndex.removeValue(forKey: trackId)
            try? fileManager.removeItem(at: coverFileUrl(trackId: trackId))
        }

        coverIndexDirty = true
        scheduleCoverIndexSave()
    }

    public func clearCovers() {
        for trackId in coverIndex.keys {
            try? fileManager.removeItem(at: coverFileUrl(trackId: trackId))
        }

        coverIndex = [:]
        missingCovers = []
        coverIndexDirty = true
        missingCoversDirty = true
        scheduleFlush()
    }

    /// Writes pending index changes, including LRU timestamp updates (call it before the app quits).
    public func flush() {
        flushTask?.cancel()
        flushTask = nil
        if trackIndexDirty {
            trackIndexDirty = false
            write(trackIndex, to: tracksDirectory.appendingPathComponent("index.json"))
        }

        if coverIndexDirty {
            coverIndexDirty = false
            write(coverIndex, to: coversDirectory.appendingPathComponent("index.json"))
        }

        if missingCoversDirty {
            missingCoversDirty = false
            write(missingCovers, to: coversDirectory.appendingPathComponent("missing.json"))
        }
    }

    private func scheduleFlush() {
        guard flushTask == nil else {
            return
        }

        flushTask = Task {
            try? await Task.sleep(for: Self.flushDelay)
            guard !Task.isCancelled else {
                return
            }

            flush()
        }
    }

    // MARK: Recently played

    public func addRecentlyPlayed(trackId: String, maxCount: Int = PlaybackConstants.recentlyPlayedMaxCount) {
        recentlyPlayed[trackId] = Date()
        if recentlyPlayed.count > maxCount {
            let keep = recentlyPlayed.sorted { $0.value > $1.value }.prefix(maxCount)
            recentlyPlayed = Dictionary(uniqueKeysWithValues: keep.map { ($0.key, $0.value) })
        }

        write(recentlyPlayed, to: rootDirectory.appendingPathComponent("recently-played.json"))
    }

    public func recentlyPlayedIds(maxCount: Int = PlaybackConstants.recentlyPlayedMaxCount) -> [String] {
        recentlyPlayed.sorted { $0.value > $1.value }.prefix(maxCount).map(\.key)
    }

    // MARK: Maintenance

    /// Clears downloaded tracks, cached playlists, covers and offline markers.
    public func clearAll() {
        clearCachedTracks()
        clearCovers()
        for id in Array(playlistIndex.keys) {
            deleteCachedPlaylist(id: id)
        }

        offlinePlaylists = []
        write(offlinePlaylists, to: rootDirectory.appendingPathComponent("offline-playlists.json"))
    }

    public func storageUsage() -> StorageUsage {
        let used = Self.directorySize(rootDirectory)
        let available = (try? rootDirectory.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey]).volumeAvailableCapacityForImportantUsage) ?? 0
        return StorageUsage(usedBytes: used, availableBytes: available)
    }

    // MARK: Helpers

    private func playlistFileUrl(id: String) -> URL {
        playlistsDirectory.appendingPathComponent("\(Self.hash(id)).json")
    }

    private var playlistIndexUrl: URL {
        playlistsDirectory.appendingPathComponent("index.json")
    }

    private func persistPlaylistIndex() {
        write(playlistIndex, to: playlistIndexUrl)
    }

    private func trackFileUrl(fileName: String) -> URL {
        tracksDirectory.appendingPathComponent(fileName)
    }

    private func coverFileUrl(trackId: String) -> URL {
        coversDirectory.appendingPathComponent(Self.hash(trackId))
    }

    private func scheduleTrackIndexSave() {
        trackIndexDirty = true
        scheduleFlush()
    }

    private func scheduleCoverIndexSave() {
        if coverIndexDirty {
            scheduleFlush()
        }
    }

    private func read<T: Decodable>(_ type: T.Type, from url: URL) -> T? {
        guard let data = try? Data(contentsOf: url) else {
            return nil
        }

        return try? decoder.decode(type, from: data)
    }

    private func write<T: Encodable>(_ value: T, to url: URL) {
        guard let data = try? encoder.encode(value) else {
            return
        }

        try? data.write(to: url, options: .atomic)
    }

    static func hash(_ value: String) -> String {
        let digest = SHA256.hash(data: Data(value.utf8))
        return String(unsafeUninitializedCapacity: SHA256.byteCount * 2) { buffer in
            var index = 0
            for byte in digest {
                buffer[index] = hexDigits[Int(byte >> 4)]
                buffer[index + 1] = hexDigits[Int(byte & 0x0F)]
                index += 2
            }

            return index
        }
    }

    private static let hexDigits = Array("0123456789abcdef".utf8)

    static func directorySize(_ url: URL) -> Int64 {
        guard let enumerator = FileManager.default.enumerator(at: url, includingPropertiesForKeys: [.totalFileAllocatedSizeKey, .fileSizeKey]) else {
            return 0
        }

        var total: Int64 = 0
        for case let fileUrl as URL in enumerator {
            let values = try? fileUrl.resourceValues(forKeys: [.totalFileAllocatedSizeKey, .fileSizeKey])
            total += Int64(values?.totalFileAllocatedSize ?? values?.fileSize ?? 0)
        }

        return total
    }
}
