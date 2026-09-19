import AppKit
import ImageIO
import MusicPlayerCore

/// A decoded image that can cross actors: `CGImage` is immutable, unlike `NSImage` which is not `Sendable` in every SDK.
struct DecodedImage: @unchecked Sendable {
    let cgImage: CGImage

    @MainActor
    var nsImage: NSImage {
        NSImage(cgImage: cgImage, size: NSSize(width: cgImage.width, height: cgImage.height))
    }
}

/// Loads cover art from the disk cache or the server, with an in-memory cache and request de-duplication.
///
/// Covers are downloaded once per track at `downloadSize` and decoded at the size they are displayed:
/// a 256×256 bitmap uses 256 KB, while a table row only needs about 64×64 (16 KB).
@MainActor
final class CoverLoader {
    /// The size requested from the server (and stored on disk), large enough for every place a cover is shown.
    static let downloadSize = 256

    private let store: LibraryStore
    private let clientProvider: () -> APIClient
    /// Decoded images, limited by their size in bytes.
    private let memoryCache = NSCache<NSString, NSImage>()
    private var inFlight: [String: Task<DecodedImage?, Never>] = [:]

    /// Whether network requests are allowed for tracks that are not downloaded (false in Low Data Mode).
    var allowsNetworkForUncachedTracks = true
    var isOnline = true
    var isEnabled = true

    init(store: LibraryStore, clientProvider: @escaping () -> APIClient) {
        self.store = store
        self.clientProvider = clientProvider
        memoryCache.totalCostLimit = 24 * 1024 * 1024
    }

    func cachedImage(trackId: String, pixelSize: Int) -> NSImage? {
        memoryCache.object(forKey: Self.key(trackId: trackId, pixelSize: pixelSize))
    }

    /// Returns the cover of a track decoded to at most `pixelSize` pixels on its longest side.
    func image(trackId: String, pixelSize: Int, isTrackCached: Bool) async -> NSImage? {
        guard isEnabled, !trackId.isEmpty else {
            return nil
        }

        let key = Self.key(trackId: trackId, pixelSize: pixelSize)
        if let image = memoryCache.object(forKey: key) {
            return image
        }

        let keyString = key as String
        if let task = inFlight[keyString] {
            return await task.value?.nsImage
        }

        let store = store
        let client = clientProvider()
        let canDownload = isOnline && (allowsNetworkForUncachedTracks || isTrackCached)
        let task = Task<DecodedImage?, Never> {
            guard let data = await Self.coverData(trackId: trackId, store: store, client: client, canDownload: canDownload) else {
                return nil
            }

            return await Self.decode(data, maxPixelSize: pixelSize)
        }

        inFlight[keyString] = task
        let decoded = await task.value
        inFlight[keyString] = nil
        guard let decoded else {
            return nil
        }

        let image = decoded.nsImage
        memoryCache.setObject(image, forKey: key, cost: decoded.cgImage.bytesPerRow * decoded.cgImage.height)
        return image
    }

    func clearMemoryCache() {
        memoryCache.removeAllObjects()
    }

    private static func key(trackId: String, pixelSize: Int) -> NSString {
        "\(trackId):\(pixelSize)" as NSString
    }

    private static func coverData(trackId: String, store: LibraryStore, client: APIClient, canDownload: Bool) async -> Data? {
        if let data = await store.cachedCover(trackId: trackId) {
            return data
        }

        guard canDownload, await !store.isCoverMissing(trackId: trackId) else {
            return nil
        }

        do {
            guard let data = try await client.coverData(songId: trackId, size: downloadSize) else {
                await store.addMissingCover(trackId: trackId)
                return nil
            }

            await store.saveCover(trackId: trackId, data: data)
            return data
        } catch {
            return nil
        }
    }

    /// Decodes a thumbnail off the main thread, without ever decoding the full-size bitmap.
    private nonisolated static func decode(_ data: Data, maxPixelSize: Int) async -> DecodedImage? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil) else {
            return nil
        }

        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceShouldCacheImmediately: true,
            kCGImageSourceThumbnailMaxPixelSize: maxPixelSize,
        ]
        guard let cgImage = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else {
            return nil
        }

        return DecodedImage(cgImage: cgImage)
    }
}
