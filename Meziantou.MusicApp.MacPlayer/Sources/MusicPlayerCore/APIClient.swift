import Foundation

public enum APIError: LocalizedError, Equatable {
    case notConfigured
    case invalidUrl
    case http(statusCode: Int, message: String?)

    public var errorDescription: String? {
        switch self {
        case .notConfigured:
            "Server is not configured"
        case .invalidUrl:
            "Invalid server URL"
        case let .http(statusCode, message):
            message ?? "HTTP \(statusCode)"
        }
    }

    public var statusCode: Int? {
        if case let .http(statusCode, _) = self {
            return statusCode
        }

        return nil
    }
}

/// Client for the Meziantou Music Server REST API (`/api/...`).
public struct APIClient: Sendable {
    public static let coverAcceptHeader = "image/avif,image/webp,image/png,image/jpeg;q=0.8,*/*;q=0.5"

    public let baseUrl: String
    private let session: URLSession

    public init(baseUrl: String, session: URLSession = .shared) {
        var url = baseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        while url.hasSuffix("/") {
            url.removeLast()
        }

        self.baseUrl = url
        self.session = session
    }

    public var isConfigured: Bool {
        !baseUrl.isEmpty
    }

    // MARK: URLs

    public func url(_ path: String, query: [URLQueryItem] = []) throws -> URL {
        guard isConfigured else {
            throw APIError.notConfigured
        }

        guard var components = URLComponents(string: baseUrl + path) else {
            throw APIError.invalidUrl
        }

        if !query.isEmpty {
            components.queryItems = query
        }

        guard let url = components.url, url.scheme != nil else {
            throw APIError.invalidUrl
        }

        return url
    }

    public func songStreamUrl(songId: String, quality: StreamingQuality) throws -> URL {
        var query: [URLQueryItem] = []
        if quality.format != .raw {
            query.append(URLQueryItem(name: "format", value: quality.format.rawValue))
            if let maxBitRate = quality.maxBitRate, maxBitRate > 0 {
                query.append(URLQueryItem(name: "maxBitRate", value: String(maxBitRate)))
            }
        }

        return try url("/api/songs/\(Self.encodePathSegment(songId))/data", query: query)
    }

    public func songCoverUrl(songId: String, size: Int? = nil) throws -> URL {
        let query = size.map { [URLQueryItem(name: "size", value: String($0))] } ?? []
        return try url("/api/songs/\(Self.encodePathSegment(songId))/cover", query: query)
    }

    static func encodePathSegment(_ value: String) -> String {
        var allowed = CharacterSet.urlPathAllowed
        allowed.remove(charactersIn: "/?#[]@!$&'()*+,;=")
        return value.addingPercentEncoding(withAllowedCharacters: allowed) ?? value
    }

    // MARK: Endpoints

    public func playlists() async throws -> PlaylistsResponse {
        try await getJSON("/api/playlists.json")
    }

    public func playlistTracks(playlistId: String) async throws -> PlaylistTracksResponse {
        try await getJSON("/api/playlists/\(Self.encodePathSegment(playlistId)).json")
    }

    public func scanStatus() async throws -> ScanStatusResponse {
        try await getJSON("/api/scan/status.json")
    }

    @discardableResult
    public func triggerScan(force: Bool = false) async throws -> ScanStatusResponse {
        let query = force ? [URLQueryItem(name: "force", value: "true")] : []
        return try await send(try url("/api/scan.json", query: query), method: "POST")
    }

    public func cleanupTranscodingCache() async throws -> CacheCleanupResponse {
        try await send(try url("/api/cache/transcoding/cleanup.json"), method: "POST")
    }

    public func songLyrics(songId: String) async throws -> LyricsResponse {
        try await getJSON("/api/songs/\(Self.encodePathSegment(songId))/lyrics.json")
    }

    public func testConnection() async -> Bool {
        do {
            _ = try await playlists()
            return true
        } catch {
            return false
        }
    }

    /// Downloads a cover image. Returns nil when the server has no cover for the song (HTTP 404).
    public func coverData(songId: String, size: Int) async throws -> Data? {
        var request = URLRequest(url: try songCoverUrl(songId: songId, size: size), timeoutInterval: 30)
        request.setValue(Self.coverAcceptHeader, forHTTPHeaderField: "Accept")
        let (data, response) = try await session.data(for: request)
        let statusCode = (response as? HTTPURLResponse)?.statusCode ?? 200
        if statusCode == 404 {
            return nil
        }

        guard (200..<300).contains(statusCode) else {
            throw APIError.http(statusCode: statusCode, message: nil)
        }

        return data
    }

    /// Downloads a song to a temporary file. The caller owns the returned file.
    public func downloadSong(songId: String, quality: StreamingQuality) async throws -> URL {
        let request = URLRequest(url: try songStreamUrl(songId: songId, quality: quality), timeoutInterval: 120)
        let (temporaryUrl, response) = try await session.download(for: request)
        let statusCode = (response as? HTTPURLResponse)?.statusCode ?? 200
        guard (200..<300).contains(statusCode) else {
            try? FileManager.default.removeItem(at: temporaryUrl)
            throw APIError.http(statusCode: statusCode, message: nil)
        }

        // URLSession deletes its temporary file when this method returns, so move it
        let destination = FileManager.default.temporaryDirectory
            .appendingPathComponent("MeziantouMusic-\(UUID().uuidString)")
            .appendingPathExtension(Self.fileExtension(for: quality, response: response))
        try FileManager.default.moveItem(at: temporaryUrl, to: destination)
        return destination
    }

    /// A file extension that helps AVFoundation identify the container.
    static func fileExtension(for quality: StreamingQuality, response: URLResponse?) -> String {
        switch quality.format {
        case .mp3: return "mp3"
        case .opus: return "opus"
        case .ogg: return "ogg"
        case .m4a: return "m4a"
        case .flac: return "flac"
        case .raw:
            return AudioFileTypes.fileExtension(forMimeType: response?.mimeType)
                ?? response?.url?.pathExtension.nilIfEmpty
                ?? "audio"
        }
    }

    // MARK: Helpers

    private func getJSON<T: Decodable>(_ path: String) async throws -> T {
        try await send(try url(path), method: "GET")
    }

    private func send<T: Decodable>(_ url: URL, method: String) async throws -> T {
        var request = URLRequest(url: url, timeoutInterval: 30)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        let (data, response) = try await session.data(for: request)
        let statusCode = (response as? HTTPURLResponse)?.statusCode ?? 200
        guard (200..<300).contains(statusCode) else {
            let message = (try? JSONDecoder().decode(ErrorResponse.self, from: data))?.error
            throw APIError.http(statusCode: statusCode, message: message)
        }

        return try JSONDecoder().decode(T.self, from: data)
    }
}

public enum AudioFileTypes {
    public static func fileExtension(forMimeType mimeType: String?) -> String? {
        switch mimeType?.lowercased() {
        case "audio/mpeg", "audio/mp3": "mp3"
        case "audio/flac", "audio/x-flac": "flac"
        case "audio/mp4", "audio/m4a", "audio/x-m4a": "m4a"
        case "audio/aac": "aac"
        case "audio/ogg", "audio/vorbis": "ogg"
        case "audio/opus": "opus"
        case "audio/wav", "audio/x-wav", "audio/wave": "wav"
        default: nil
        }
    }
}

extension String {
    var nilIfEmpty: String? {
        isEmpty ? nil : self
    }
}
