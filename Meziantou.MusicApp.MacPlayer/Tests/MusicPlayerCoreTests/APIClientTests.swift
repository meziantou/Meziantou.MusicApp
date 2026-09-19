import Foundation
import Testing
@testable import MusicPlayerCore

/// Serves canned responses; each test registers handlers for its own host so tests can run in parallel.
final class MockURLProtocol: URLProtocol, @unchecked Sendable {
    typealias Handler = @Sendable (URLRequest) -> (Int, [String: String], Data)

    private static let lock = NSLock()
    nonisolated(unsafe) private static var handlers: [String: Handler] = [:]

    static func register(host: String, handler: @escaping Handler) {
        lock.lock()
        defer { lock.unlock() }
        handlers[host] = handler
    }

    static func session() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [MockURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    override class func canInit(with request: URLRequest) -> Bool {
        true
    }

    override class func canonicalRequest(for request: URLRequest) -> URLRequest {
        request
    }

    override func startLoading() {
        Self.lock.lock()
        let handler = request.url?.host.flatMap { Self.handlers[$0] }
        Self.lock.unlock()

        guard let handler, let url = request.url else {
            client?.urlProtocol(self, didFailWithError: URLError(.cannotConnectToHost))
            return
        }

        let (statusCode, headers, data) = handler(request)
        let response = HTTPURLResponse(url: url, statusCode: statusCode, httpVersion: "HTTP/1.1", headerFields: headers)!
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: data)
        client?.urlProtocolDidFinishLoading(self)
    }

    override func stopLoading() {
    }
}

struct APIClientTests {
    @Test func trimsTrailingSlashes() {
        #expect(APIClient(baseUrl: " https://music.example.com// ").baseUrl == "https://music.example.com")
    }

    @Test func buildsStreamUrls() throws {
        let client = APIClient(baseUrl: "https://music.example.com/")
        #expect(try client.songStreamUrl(songId: "a b/c", quality: .raw).absoluteString == "https://music.example.com/api/songs/a%20b%2Fc/data")
        #expect(try client.songStreamUrl(songId: "1", quality: StreamingQuality(format: .opus, maxBitRate: 160)).absoluteString == "https://music.example.com/api/songs/1/data?format=opus&maxBitRate=160")
        #expect(try client.songStreamUrl(songId: "1", quality: StreamingQuality(format: .flac)).absoluteString == "https://music.example.com/api/songs/1/data?format=flac")
    }

    @Test func buildsCoverUrls() throws {
        let client = APIClient(baseUrl: "http://localhost:5000")
        #expect(try client.songCoverUrl(songId: "1", size: 256).absoluteString == "http://localhost:5000/api/songs/1/cover?size=256")
        #expect(try client.songCoverUrl(songId: "1").absoluteString == "http://localhost:5000/api/songs/1/cover")
    }

    @Test func throwsWhenNotConfigured() {
        #expect(throws: APIError.notConfigured) {
            try APIClient(baseUrl: "").songCoverUrl(songId: "1")
        }
    }

    @Test func decodesPlaylists() async throws {
        MockURLProtocol.register(host: "playlists.test") { request in
            #expect(request.url?.path == "/api/playlists.json")
            let json = """
            {"playlists":[{"id":"p1","name":"Rock","trackCount":2,"duration":400,"size":1024,"created":"2024-01-01T00:00:00Z","changed":"2024-02-01T00:00:00.1234567Z","sortOrder":1}]}
            """
            return (200, ["Content-Type": "application/json"], Data(json.utf8))
        }

        let client = APIClient(baseUrl: "https://playlists.test", session: MockURLProtocol.session())
        let response = try await client.playlists()
        #expect(response.playlists == [PlaylistSummary(id: "p1", name: "Rock", trackCount: 2, duration: 400, size: 1024, created: "2024-01-01T00:00:00Z", changed: "2024-02-01T00:00:00.1234567Z", sortOrder: 1)])
    }

    @Test func decodesTracksWithNullFields() async throws {
        MockURLProtocol.register(host: "tracks.test") { request in
            #expect(request.url?.path == "/api/playlists/my%20list.json" || request.url?.path == "/api/playlists/my list.json")
            let json = """
            {"id":"my list","name":"My list","trackCount":1,"duration":10,"size":5,"created":"2024-01-01T00:00:00Z","changed":"2024-01-01T00:00:00Z",
             "tracks":[{"id":"t1","title":"Song","path":"a/b.flac","artists":null,"artistId":null,"album":"Album","albumId":null,"duration":10,"track":null,"year":2020,
                        "genre":null,"bitRate":900,"size":5,"contentType":"audio/flac","addedDate":null,"isrc":null,"replayGainTrackGain":-7.5,
                        "replayGainTrackPeak":0.98,"replayGainAlbumGain":null,"replayGainAlbumPeak":null}]}
            """
            return (200, [:], Data(json.utf8))
        }

        let client = APIClient(baseUrl: "https://tracks.test", session: MockURLProtocol.session())
        let response = try await client.playlistTracks(playlistId: "my list")
        let track = try #require(response.tracks.first)
        #expect(track.title == "Song")
        #expect(track.artists == nil)
        #expect(track.replayGainTrackGain == -7.5)
        #expect(track.downloadFileName == "b.flac")
    }

    @Test func decodesScanStatus() async throws {
        MockURLProtocol.register(host: "scan.test") { request in
            #expect(request.httpMethod == "POST")
            #expect(request.url?.query == "force=true")
            let json = """
            {"isScanning":true,"isInitialScanCompleted":true,"scanCount":3,"lastScanDate":null,"percentage":42.5,"estimatedCompletionTime":"00:02:00",
             "processedFiles":10,"totalFiles":20,"processedPlaylists":null,"totalPlaylists":null,"activeScanGeneration":4,"lastCompletedScanGeneration":3,
             "invalidPlaylists":[{"path":"/music/bad.m3u","errorMessage":"Oops"}]}
            """
            return (200, [:], Data(json.utf8))
        }

        let client = APIClient(baseUrl: "https://scan.test", session: MockURLProtocol.session())
        let status = try await client.triggerScan(force: true)
        #expect(status.isScanning)
        #expect(status.percentage == 42.5)
        #expect(status.estimatedRemainingSeconds == 120)
        #expect(status.invalidPlaylists.first?.fileName == "bad.m3u")
    }

    @Test func surfacesServerErrors() async {
        MockURLProtocol.register(host: "error.test") { _ in
            (404, [:], Data(#"{"error":"Playlist not found"}"#.utf8))
        }

        let client = APIClient(baseUrl: "https://error.test", session: MockURLProtocol.session())
        await #expect(throws: APIError.http(statusCode: 404, message: "Playlist not found")) {
            _ = try await client.playlistTracks(playlistId: "x")
        }
        #expect(await !client.testConnection())
    }

    @Test func coverReturnsNilWhenMissing() async throws {
        MockURLProtocol.register(host: "cover.test") { request in
            #expect(request.value(forHTTPHeaderField: "Accept") == APIClient.coverAcceptHeader)
            return request.url?.path.contains("/missing/") == true ? (404, [:], Data()) : (200, [:], Data([1, 2, 3]))
        }

        let client = APIClient(baseUrl: "https://cover.test", session: MockURLProtocol.session())
        #expect(try await client.coverData(songId: "missing", size: 64) == nil)
        #expect(try await client.coverData(songId: "found", size: 64) == Data([1, 2, 3]))
    }

    @Test func downloadsSongToFileWithExtension() async throws {
        MockURLProtocol.register(host: "download.test") { _ in
            (200, ["Content-Type": "audio/flac"], Data([9, 8, 7]))
        }

        let client = APIClient(baseUrl: "https://download.test", session: MockURLProtocol.session())
        let file = try await client.downloadSong(songId: "1", quality: .raw)
        defer { try? FileManager.default.removeItem(at: file) }
        #expect(file.pathExtension == "flac")
        #expect(try Data(contentsOf: file) == Data([9, 8, 7]))
    }
}
