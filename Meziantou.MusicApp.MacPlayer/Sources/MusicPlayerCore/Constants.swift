import Foundation

public struct QualityOption: Hashable, Sendable, Identifiable {
    public var label: String
    public var quality: StreamingQuality

    public var id: String {
        label
    }

    public static let all: [QualityOption] = [
        QualityOption(label: "Original (Raw)", quality: StreamingQuality(format: .raw)),
        QualityOption(label: "FLAC (Lossless)", quality: StreamingQuality(format: .flac)),
        QualityOption(label: "MP3 320kbps", quality: StreamingQuality(format: .mp3, maxBitRate: 320)),
        QualityOption(label: "MP3 256kbps", quality: StreamingQuality(format: .mp3, maxBitRate: 256)),
        QualityOption(label: "MP3 192kbps", quality: StreamingQuality(format: .mp3, maxBitRate: 192)),
        QualityOption(label: "MP3 128kbps", quality: StreamingQuality(format: .mp3, maxBitRate: 128)),
        QualityOption(label: "Opus 192kbps", quality: StreamingQuality(format: .opus, maxBitRate: 192)),
        QualityOption(label: "Opus 160kbps", quality: StreamingQuality(format: .opus, maxBitRate: 160)),
        QualityOption(label: "Opus 128kbps", quality: StreamingQuality(format: .opus, maxBitRate: 128)),
        QualityOption(label: "Opus 96kbps", quality: StreamingQuality(format: .opus, maxBitRate: 96)),
        QualityOption(label: "Opus 64kbps", quality: StreamingQuality(format: .opus, maxBitRate: 64)),
        QualityOption(label: "OGG 192kbps", quality: StreamingQuality(format: .ogg, maxBitRate: 192)),
        QualityOption(label: "OGG 128kbps", quality: StreamingQuality(format: .ogg, maxBitRate: 128)),
        QualityOption(label: "M4A/AAC 256kbps", quality: StreamingQuality(format: .m4a, maxBitRate: 256)),
        QualityOption(label: "M4A/AAC 128kbps", quality: StreamingQuality(format: .m4a, maxBitRate: 128)),
    ]
}

public enum PlaybackConstants {
    /// Interval between two background playlist synchronizations.
    public static let playlistSyncInterval: Duration = .seconds(5 * 60)
    /// Delay after which a paused player releases its audio resources.
    public static let idleReleaseDelay: Duration = .seconds(5 * 60)
    /// Maximum number of recently played tracks remembered.
    public static let recentlyPlayedMaxCount = 300
    /// Maximum number of cover images kept in the cache (covers of downloaded tracks are never evicted).
    public static let coverCacheMaxEntries = 300
    /// Volume step used by keyboard shortcuts and the scroll wheel.
    public static let volumeStep: Double = 0.05
    /// Maximum volume (200%).
    public static let maxVolume: Double = 2
}
