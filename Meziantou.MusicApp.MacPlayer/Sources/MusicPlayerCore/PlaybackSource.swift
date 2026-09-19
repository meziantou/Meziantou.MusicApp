import Foundation

/// Where a track should be played from.
public enum PlaybackSource: Equatable, Sendable {
    /// Play the downloaded copy.
    case cache
    /// Stream from the server with the given quality.
    case stream(StreamingQuality)
    /// The track cannot be played in the current conditions.
    case unavailable(reason: String)
}

/// Implements the playback algorithm described in the README:
/// offline → cache only, low data → cache or low quality (or nothing), normal → cache or normal quality.
public enum PlaybackSourceResolver {
    public static func resolve(
        cachedQuality: StreamingQuality?,
        desiredQuality: StreamingQuality,
        isOnline: Bool,
        networkType: NetworkType,
        preventDownloadOnLowData: Bool
    ) -> PlaybackSource {
        if let cachedQuality, shouldUseCache(cachedQuality: cachedQuality, desiredQuality: desiredQuality, isOnline: isOnline) {
            return .cache
        }

        if !isOnline {
            return cachedQuality == nil ? .unavailable(reason: "Track is not available offline") : .cache
        }

        if networkType == .lowData && preventDownloadOnLowData {
            return .unavailable(reason: "Skipping track: Low data mode prevents download")
        }

        return .stream(desiredQuality)
    }

    /// Whether a cached copy is good enough compared to the desired streaming quality.
    public static func shouldUseCache(cachedQuality: StreamingQuality, desiredQuality: StreamingQuality, isOnline: Bool) -> Bool {
        if !isOnline {
            return true
        }

        if cachedQuality.format == .raw {
            return true
        }

        if desiredQuality.format == .raw {
            return false
        }

        if cachedQuality.format != desiredQuality.format {
            return false
        }

        if let desiredBitRate = desiredQuality.maxBitRate {
            guard let cachedBitRate = cachedQuality.maxBitRate, cachedBitRate >= desiredBitRate else {
                return false
            }
        }

        return true
    }

    /// The quality to request when the system cannot decode `quality` for this track
    /// (e.g. Ogg containers on older macOS versions, or raw WMA files).
    /// Returns nil when there is no better alternative.
    public static func fallbackQuality(for quality: StreamingQuality) -> StreamingQuality? {
        switch quality.format {
        case .opus, .ogg:
            return StreamingQuality(format: .m4a, maxBitRate: quality.maxBitRate ?? 192)
        case .raw:
            return StreamingQuality(format: .flac)
        case .flac:
            return StreamingQuality(format: .m4a, maxBitRate: 256)
        case .mp3, .m4a:
            return nil
        }
    }
}
