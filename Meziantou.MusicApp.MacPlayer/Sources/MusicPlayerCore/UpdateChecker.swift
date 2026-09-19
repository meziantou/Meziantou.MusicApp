import Foundation

/// A `major.minor.patch` version, as used by the macOS player releases (`macos-v1.2.3`).
public struct AppVersion: Comparable, Hashable, Sendable, CustomStringConvertible {
    public var components: [Int]

    /// Parses "1.2.3", "v1.2.3" or "macos-v1.2.3". Missing components are treated as 0 when comparing.
    public init?(_ value: String) {
        var text = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.hasPrefix(UpdateChecker.tagPrefix) {
            text.removeFirst(UpdateChecker.tagPrefix.count)
        } else if text.hasPrefix("v") {
            text.removeFirst()
        }

        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        let components = parts.compactMap { Int($0) }
        guard !parts.isEmpty, components.count == parts.count, components.allSatisfy({ $0 >= 0 }) else {
            return nil
        }

        self.components = components
    }

    public var description: String {
        components.map(String.init).joined(separator: ".")
    }

    public static func < (lhs: AppVersion, rhs: AppVersion) -> Bool {
        let count = max(lhs.components.count, rhs.components.count)
        for index in 0..<count {
            let left = index < lhs.components.count ? lhs.components[index] : 0
            let right = index < rhs.components.count ? rhs.components[index] : 0
            if left != right {
                return left < right
            }
        }

        return false
    }

    public static func == (lhs: AppVersion, rhs: AppVersion) -> Bool {
        !(lhs < rhs) && !(rhs < lhs)
    }

    public func hash(into hasher: inout Hasher) {
        var components = components
        while components.last == 0 {
            components.removeLast()
        }

        hasher.combine(components)
    }
}

/// A published release of the macOS player.
public struct AppRelease: Equatable, Sendable {
    public var version: AppVersion
    public var tag: String
    public var url: URL
}

/// Finds the latest macOS player release on GitHub.
public struct UpdateChecker: Sendable {
    public static let tagPrefix = "macos-v"
    public static let defaultReleasesUrl = URL(string: "https://api.github.com/repos/meziantou/meziantou.musicapp/releases?per_page=50")!

    private let releasesUrl: URL
    private let session: URLSession

    public init(releasesUrl: URL = UpdateChecker.defaultReleasesUrl, session: URLSession = APIClient.defaultSession) {
        self.releasesUrl = releasesUrl
        self.session = session
    }

    /// The version of the running app (`CFBundleShortVersionString`).
    public static var currentVersion: AppVersion? {
        (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String).flatMap(AppVersion.init)
    }

    /// Returns the latest published release, or nil when there is none.
    /// The repository also hosts releases of other components, so only `macos-v*` tags are considered.
    public func latestRelease() async throws -> AppRelease? {
        var request = URLRequest(url: releasesUrl)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        request.timeoutInterval = 30

        let (data, response) = try await session.data(for: request)
        if let httpResponse = response as? HTTPURLResponse, !(200..<300).contains(httpResponse.statusCode) {
            throw APIError.http(statusCode: httpResponse.statusCode, message: nil)
        }

        let releases = try JSONDecoder().decode([GitHubRelease].self, from: data)
        return Self.latestRelease(in: releases)
    }

    /// Returns the latest release when it is newer than `currentVersion`.
    public func availableUpdate(currentVersion: AppVersion) async throws -> AppRelease? {
        guard let release = try await latestRelease(), release.version > currentVersion else {
            return nil
        }

        return release
    }

    static func latestRelease(in releases: [GitHubRelease]) -> AppRelease? {
        releases
            .filter { !$0.draft && !$0.prerelease && $0.tagName.hasPrefix(tagPrefix) }
            .compactMap { release in
                AppVersion(release.tagName).map { AppRelease(version: $0, tag: release.tagName, url: release.htmlUrl) }
            }
            .max { $0.version < $1.version }
    }
}

struct GitHubRelease: Decodable {
    var tagName: String
    var htmlUrl: URL
    var draft: Bool
    var prerelease: Bool

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case htmlUrl = "html_url"
        case draft
        case prerelease
    }
}
