import Foundation

// MARK: - API models (match the server REST API JSON)

public struct PlaylistSummary: Codable, Hashable, Sendable, Identifiable {
    public var id: String
    public var name: String
    public var trackCount: Int
    public var duration: Double
    public var size: Int64
    public var created: String
    public var changed: String
    public var sortOrder: Int

    public init(id: String, name: String, trackCount: Int = 0, duration: Double = 0, size: Int64 = 0, created: String = "", changed: String = "", sortOrder: Int = 0) {
        self.id = id
        self.name = name
        self.trackCount = trackCount
        self.duration = duration
        self.size = size
        self.created = created
        self.changed = changed
        self.sortOrder = sortOrder
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(String.self, forKey: .id)
        name = try container.decodeIfPresent(String.self, forKey: .name) ?? ""
        trackCount = try container.decodeIfPresent(Int.self, forKey: .trackCount) ?? 0
        duration = try container.decodeIfPresent(Double.self, forKey: .duration) ?? 0
        size = try container.decodeIfPresent(Int64.self, forKey: .size) ?? 0
        created = try container.decodeIfPresent(String.self, forKey: .created) ?? ""
        changed = try container.decodeIfPresent(String.self, forKey: .changed) ?? ""
        sortOrder = try container.decodeIfPresent(Int.self, forKey: .sortOrder) ?? 0
    }
}

public struct PlaylistsResponse: Codable, Sendable {
    public var playlists: [PlaylistSummary]
}

public struct TrackInfo: Codable, Hashable, Sendable, Identifiable {
    public var id: String
    public var title: String
    public var path: String
    public var artists: String?
    public var album: String?
    public var duration: Double
    public var track: Int?
    public var year: Int?
    public var genre: String?
    public var bitRate: Int?
    public var size: Int64
    public var contentType: String?
    public var addedDate: String?
    public var isrc: String?
    public var replayGainTrackGain: Double?
    public var replayGainTrackPeak: Double?
    public var replayGainAlbumGain: Double?
    public var replayGainAlbumPeak: Double?

    public init(
        id: String,
        title: String,
        path: String = "",
        artists: String? = nil,
        album: String? = nil,
        duration: Double = 0,
        track: Int? = nil,
        year: Int? = nil,
        genre: String? = nil,
        bitRate: Int? = nil,
        size: Int64 = 0,
        contentType: String? = nil,
        addedDate: String? = nil,
        isrc: String? = nil,
        replayGainTrackGain: Double? = nil,
        replayGainTrackPeak: Double? = nil,
        replayGainAlbumGain: Double? = nil,
        replayGainAlbumPeak: Double? = nil
    ) {
        self.id = id
        self.title = title
        self.path = path
        self.artists = artists
        self.album = album
        self.duration = duration
        self.track = track
        self.year = year
        self.genre = genre
        self.bitRate = bitRate
        self.size = size
        self.contentType = contentType
        self.addedDate = addedDate
        self.isrc = isrc
        self.replayGainTrackGain = replayGainTrackGain
        self.replayGainTrackPeak = replayGainTrackPeak
        self.replayGainAlbumGain = replayGainAlbumGain
        self.replayGainAlbumPeak = replayGainAlbumPeak
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(String.self, forKey: .id)
        title = try container.decodeIfPresent(String.self, forKey: .title) ?? ""
        path = try container.decodeIfPresent(String.self, forKey: .path) ?? ""
        artists = try container.decodeIfPresent(String.self, forKey: .artists)
        album = try container.decodeIfPresent(String.self, forKey: .album)
        duration = try container.decodeIfPresent(Double.self, forKey: .duration) ?? 0
        track = try container.decodeIfPresent(Int.self, forKey: .track)
        year = try container.decodeIfPresent(Int.self, forKey: .year)
        genre = try container.decodeIfPresent(String.self, forKey: .genre)
        bitRate = try container.decodeIfPresent(Int.self, forKey: .bitRate)
        size = try container.decodeIfPresent(Int64.self, forKey: .size) ?? 0
        contentType = try container.decodeIfPresent(String.self, forKey: .contentType)
        addedDate = try container.decodeIfPresent(String.self, forKey: .addedDate)
        isrc = try container.decodeIfPresent(String.self, forKey: .isrc)
        replayGainTrackGain = try container.decodeIfPresent(Double.self, forKey: .replayGainTrackGain)
        replayGainTrackPeak = try container.decodeIfPresent(Double.self, forKey: .replayGainTrackPeak)
        replayGainAlbumGain = try container.decodeIfPresent(Double.self, forKey: .replayGainAlbumGain)
        replayGainAlbumPeak = try container.decodeIfPresent(Double.self, forKey: .replayGainAlbumPeak)
    }

    /// The file name of the track on the server, used when saving the raw file.
    public var downloadFileName: String {
        let normalizedPath = path.replacingOccurrences(of: "\\", with: "/")
        if let fileName = normalizedPath.split(separator: "/").last?.trimmingCharacters(in: .whitespaces), !fileName.isEmpty {
            return fileName
        }

        let invalidCharacters = CharacterSet(charactersIn: "<>:\"/\\|?*").union(.controlCharacters)
        let safeTitle = title.unicodeScalars
            .map { invalidCharacters.contains($0) ? "_" : String($0) }
            .joined()
            .trimmingCharacters(in: .whitespaces)
        if !safeTitle.isEmpty {
            return "\(safeTitle).bin"
        }

        return "\(id).bin"
    }
}

public struct PlaylistTracksResponse: Codable, Sendable {
    public var id: String
    public var name: String
    public var tracks: [TrackInfo]

    public init(id: String, name: String, tracks: [TrackInfo]) {
        self.id = id
        self.name = name
        self.tracks = tracks
    }
}

public struct InvalidPlaylistInfo: Codable, Hashable, Sendable {
    public var path: String
    public var errorMessage: String

    public init(path: String, errorMessage: String) {
        self.path = path
        self.errorMessage = errorMessage
    }

    /// The playlist file name, without its directory.
    public var fileName: String {
        let name = path.split(whereSeparator: { $0 == "/" || $0 == "\\" }).last.map(String.init) ?? ""
        return name.isEmpty ? path : name
    }
}

public struct ScanStatusResponse: Codable, Sendable, Equatable {
    public var isScanning: Bool
    public var isInitialScanCompleted: Bool
    public var scanCount: Int
    public var lastScanDate: String?
    public var percentage: Double?
    /// A .NET TimeSpan serialized as `[d.]hh:mm:ss[.fffffff]`.
    public var estimatedCompletionTime: String?
    public var processedFiles: Int?
    public var totalFiles: Int?
    public var processedPlaylists: Int?
    public var totalPlaylists: Int?
    public var activeScanGeneration: Int64?
    public var lastCompletedScanGeneration: Int64?
    public var invalidPlaylists: [InvalidPlaylistInfo]

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        isScanning = try container.decodeIfPresent(Bool.self, forKey: .isScanning) ?? false
        isInitialScanCompleted = try container.decodeIfPresent(Bool.self, forKey: .isInitialScanCompleted) ?? false
        scanCount = try container.decodeIfPresent(Int.self, forKey: .scanCount) ?? 0
        lastScanDate = try container.decodeIfPresent(String.self, forKey: .lastScanDate)
        percentage = try container.decodeIfPresent(Double.self, forKey: .percentage)
        estimatedCompletionTime = try container.decodeIfPresent(String.self, forKey: .estimatedCompletionTime)
        processedFiles = try container.decodeIfPresent(Int.self, forKey: .processedFiles)
        totalFiles = try container.decodeIfPresent(Int.self, forKey: .totalFiles)
        processedPlaylists = try container.decodeIfPresent(Int.self, forKey: .processedPlaylists)
        totalPlaylists = try container.decodeIfPresent(Int.self, forKey: .totalPlaylists)
        activeScanGeneration = try container.decodeIfPresent(Int64.self, forKey: .activeScanGeneration)
        lastCompletedScanGeneration = try container.decodeIfPresent(Int64.self, forKey: .lastCompletedScanGeneration)
        invalidPlaylists = try container.decodeIfPresent([InvalidPlaylistInfo].self, forKey: .invalidPlaylists) ?? []
    }

    /// Estimated remaining scan time in seconds, parsed from the .NET TimeSpan format.
    public var estimatedRemainingSeconds: TimeInterval? {
        estimatedCompletionTime.flatMap(TimeSpanParser.parse)
    }
}

public struct CacheCleanupResponse: Codable, Sendable {
    public var deletedFileCount: Int
    public var failedFileCount: Int
}

public struct LyricsResponse: Codable, Sendable {
    public var lyrics: String?
}

struct ErrorResponse: Codable, Sendable {
    var error: String?
}

// MARK: - Application models

public enum AudioFormat: String, Codable, Sendable, CaseIterable {
    case raw
    case mp3
    case opus
    case ogg
    case m4a
    case flac
}

public struct StreamingQuality: Codable, Hashable, Sendable {
    public var format: AudioFormat
    public var maxBitRate: Int?

    public init(format: AudioFormat, maxBitRate: Int? = nil) {
        self.format = format
        self.maxBitRate = maxBitRate
    }

    public static let raw = StreamingQuality(format: .raw)

    /// A short label such as "OPUS 160" displayed in the player bar.
    public var badge: String {
        if let maxBitRate {
            return "\(format.rawValue.uppercased()) \(maxBitRate)"
        }

        return format.rawValue.uppercased()
    }
}

public enum ReplayGainMode: String, Codable, Sendable, CaseIterable {
    case off
    case track
    case album
}

public enum RepeatMode: String, Codable, Sendable, CaseIterable {
    case off
    case all
    case one

    /// Cycles off → all → one → off.
    public var next: RepeatMode {
        switch self {
        case .off: .all
        case .all: .one
        case .one: .off
        }
    }
}

public enum NetworkType: String, Codable, Sendable {
    case normal
    case lowData
    case unknown
}

public struct AppSettings: Codable, Equatable, Sendable {
    public var serverUrl: String = ""
    public var normalQuality = StreamingQuality(format: .opus, maxBitRate: 160)
    public var lowDataQuality = StreamingQuality(format: .opus, maxBitRate: 160)
    public var downloadQuality = StreamingQuality(format: .opus, maxBitRate: 160)
    public var preventDownloadOnLowData = false
    public var hideCoverArt = false
    public var hideTrackIndex = false
    public var hideTrackDuration = false
    public var hideTrackCacheStatus = false
    public var disablePlayingAnimation = false
    public var showPlaylistFileSize = false
    public var replayGainMode = ReplayGainMode.off
    public var showReplayGainWarning = true
    /// Shows playback controls in the menu bar; the Dock icon is then hidden while no window is open.
    public var showInMenuBar = false

    public init() {
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let defaults = AppSettings()
        serverUrl = try container.decodeIfPresent(String.self, forKey: .serverUrl) ?? defaults.serverUrl
        normalQuality = try container.decodeIfPresent(StreamingQuality.self, forKey: .normalQuality) ?? defaults.normalQuality
        lowDataQuality = try container.decodeIfPresent(StreamingQuality.self, forKey: .lowDataQuality) ?? defaults.lowDataQuality
        downloadQuality = try container.decodeIfPresent(StreamingQuality.self, forKey: .downloadQuality) ?? defaults.downloadQuality
        preventDownloadOnLowData = try container.decodeIfPresent(Bool.self, forKey: .preventDownloadOnLowData) ?? defaults.preventDownloadOnLowData
        hideCoverArt = try container.decodeIfPresent(Bool.self, forKey: .hideCoverArt) ?? defaults.hideCoverArt
        hideTrackIndex = try container.decodeIfPresent(Bool.self, forKey: .hideTrackIndex) ?? defaults.hideTrackIndex
        hideTrackDuration = try container.decodeIfPresent(Bool.self, forKey: .hideTrackDuration) ?? defaults.hideTrackDuration
        hideTrackCacheStatus = try container.decodeIfPresent(Bool.self, forKey: .hideTrackCacheStatus) ?? defaults.hideTrackCacheStatus
        disablePlayingAnimation = try container.decodeIfPresent(Bool.self, forKey: .disablePlayingAnimation) ?? defaults.disablePlayingAnimation
        showPlaylistFileSize = try container.decodeIfPresent(Bool.self, forKey: .showPlaylistFileSize) ?? defaults.showPlaylistFileSize
        replayGainMode = try container.decodeIfPresent(ReplayGainMode.self, forKey: .replayGainMode) ?? defaults.replayGainMode
        showReplayGainWarning = try container.decodeIfPresent(Bool.self, forKey: .showReplayGainWarning) ?? defaults.showReplayGainWarning
        showInMenuBar = try container.decodeIfPresent(Bool.self, forKey: .showInMenuBar) ?? defaults.showInMenuBar
    }

    /// The streaming quality to use for the given network type.
    public func streamingQuality(for networkType: NetworkType) -> StreamingQuality {
        networkType == .lowData ? lowDataQuality : normalQuality
    }
}

public enum QueueItemSource: String, Codable, Sendable {
    case manual
    case playlist
}

public struct QueueItem: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID
    public var track: TrackInfo
    public var playlistId: String
    public var indexInPlaylist: Int
    public var source: QueueItemSource

    public init(track: TrackInfo, playlistId: String, indexInPlaylist: Int, source: QueueItemSource) {
        self.id = UUID()
        self.track = track
        self.playlistId = playlistId
        self.indexInPlaylist = indexInPlaylist
        self.source = source
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        track = try container.decode(TrackInfo.self, forKey: .track)
        playlistId = try container.decode(String.self, forKey: .playlistId)
        indexInPlaylist = try container.decodeIfPresent(Int.self, forKey: .indexInPlaylist) ?? 0
        // Queue items persisted without a source are treated as manually added
        source = try container.decodeIfPresent(QueueItemSource.self, forKey: .source) ?? .manual
    }
}

public struct PlaybackState: Codable, Equatable, Sendable {
    public var currentPlaylistId: String?
    public var currentTrackIndex: Int = -1
    public var currentTrackId: String?
    public var currentTime: Double = 0
    public var isPlaying = false
    public var volume: Double = 1
    public var isMuted = false
    public var shuffleEnabled = false
    public var repeatMode = RepeatMode.off
    public var shuffleOrder: [Int] = []
    public var queue: [QueueItem] = []

    public init() {
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        currentPlaylistId = try container.decodeIfPresent(String.self, forKey: .currentPlaylistId)
        currentTrackIndex = try container.decodeIfPresent(Int.self, forKey: .currentTrackIndex) ?? -1
        currentTrackId = try container.decodeIfPresent(String.self, forKey: .currentTrackId)
        currentTime = try container.decodeIfPresent(Double.self, forKey: .currentTime) ?? 0
        isPlaying = try container.decodeIfPresent(Bool.self, forKey: .isPlaying) ?? false
        volume = try container.decodeIfPresent(Double.self, forKey: .volume) ?? 1
        isMuted = try container.decodeIfPresent(Bool.self, forKey: .isMuted) ?? false
        shuffleEnabled = try container.decodeIfPresent(Bool.self, forKey: .shuffleEnabled) ?? false
        repeatMode = try container.decodeIfPresent(RepeatMode.self, forKey: .repeatMode) ?? .off
        shuffleOrder = try container.decodeIfPresent([Int].self, forKey: .shuffleOrder) ?? []
        queue = try container.decodeIfPresent([QueueItem].self, forKey: .queue) ?? []
    }
}

public struct CachedPlaylist: Codable, Sendable {
    public var playlist: PlaylistSummary
    public var tracks: [TrackInfo]
    public var lastUpdated: Date

    public init(playlist: PlaylistSummary, tracks: [TrackInfo], lastUpdated: Date = Date()) {
        self.playlist = playlist
        self.tracks = tracks
        self.lastUpdated = lastUpdated
    }
}

public struct PlaylistDownloadProgress: Equatable, Sendable {
    public var cached: Int
    public var total: Int

    public init(cached: Int, total: Int) {
        self.cached = cached
        self.total = total
    }

    public var isComplete: Bool {
        cached >= total
    }
}
