import Foundation
import Testing
@testable import MusicPlayerCore

struct SearchTests {
    @Test(arguments: [
        ("HELLO", "hello"),
        ("Hello World", "hello world"),
        ("café", "cafe"),
        ("naïve", "naive"),
        ("São Paulo", "sao paulo"),
        ("Zürich", "zurich"),
        ("crème brûlée", "creme brulee"),
        ("Beyoncé", "beyonce"),
        ("ệ", "e"),
        ("", ""),
        ("hello-world", "hello-world"),
        ("test@123", "test@123"),
        ("  Hello   World  ", "hello world"),
        ("Track\t\tName", "track name"),
    ])
    func normalizes(input: String, expected: String) {
        #expect(Search.normalize(input) == expected)
    }

    @Test(arguments: [
        ("Hello World", "Hello World"),
        ("HeLLo WoRLd", "hElLo wOrLd"),
        ("Hello World", "lo Wo"),
        ("Hello World", "world hello"),
        ("abc xyz def", "abc def"),
        ("café", "cafe"),
        ("cafe", "café"),
        ("café", "cafè"),
        ("Hello World", ""),
        ("  Hello World  ", "hello"),
        ("Hello World", "Hello   World"),
        ("abc", "abc "),
        ("Björk", "bjork"),
        ("Sigur Rós", "sigur ros"),
        ("Crème de la Crème", "creme de la creme"),
        ("P.Y.T. (Pretty Young Thing)", "pretty"),
        ("Rock & Roll", "roll"),
        ("Así fue", "asi fue"),
        ("🎵 Music", "music"),
        ("2023 Album", "2023"),
    ])
    func matches(text: String, query: String) {
        #expect(Search.matches(text, query: query))
    }

    @Test(arguments: [
        ("Hello World", "hello moon"),
        ("abc xyz", "abc def"),
        ("Hello World", "Goodbye"),
        ("Bohemian Rhapsody", "hotel california"),
        ("", ""),
    ])
    func doesNotMatch(text: String, query: String) {
        #expect(!Search.matches(text, query: query))
    }

    @Test func nilTextNeverMatches() {
        #expect(!Search.matches(nil, query: "query"))
        #expect(!Search.matches(nil, query: ""))
    }

    @Test func filtersTracksOnTitleArtistAlbumAndIsrc() {
        let tracks = [
            TrackInfo(id: "1", title: "Bohemian Rhapsody", artists: "Queen", album: "A Night at the Opera"),
            TrackInfo(id: "2", title: "Jóga", artists: "Björk", album: "Homogenic", isrc: "GBAAA9700001"),
            TrackInfo(id: "3", title: "Hoppípolla", artists: "Sigur Rós", album: "Takk..."),
        ]
        let haystacks = tracks.map(Search.haystack)
        #expect(Search.filter(tracks, haystacks: haystacks, query: "bjork").map(\.id) == ["2"])
        #expect(Search.filter(tracks, haystacks: haystacks, query: "opera queen").map(\.id) == ["1"])
        #expect(Search.filter(tracks, haystacks: haystacks, query: "gbaaa97").map(\.id) == ["2"])
        #expect(Search.filter(tracks, haystacks: haystacks, query: " ").count == 3)
    }
}

struct TrackSortingTests {
    private func track(_ id: String, title: String = "", artists: String? = nil, added: String?) -> TrackInfo {
        TrackInfo(id: id, title: title, artists: artists, addedDate: added)
    }

    @Test func sortsByAddedDateDescending() {
        let tracks = [
            track("old", added: "2024-01-01T00:00:00Z"),
            track("new", added: "2024-03-01T00:00:00Z"),
            track("mid", added: "2024-02-01T00:00:00Z"),
        ]
        #expect(TrackSorting.sort(tracks, by: .added, direction: .descending).map(\.id) == ["new", "mid", "old"])
    }

    @Test func preservesOriginalOrderWhenDatesAreEqual() {
        let tracks = ["a", "b", "c"].map { track($0, added: "2024-01-01T00:00:00Z") }
        #expect(TrackSorting.sort(tracks, by: .added, direction: .descending).map(\.id) == ["a", "b", "c"])
    }

    @Test func sortsByTitle() {
        let tracks = [track("1", title: "b", added: nil), track("2", title: "A", added: nil), track("3", title: "c", added: nil)]
        #expect(TrackSorting.sort(tracks, by: .title, direction: .ascending).map(\.id) == ["2", "1", "3"])
        #expect(TrackSorting.sort(tracks, by: .title, direction: .descending).map(\.id) == ["3", "1", "2"])
    }

    @Test func sortsMissingArtistsFirst() {
        let tracks = [track("1", artists: "Queen", added: nil), track("2", artists: nil, added: nil)]
        #expect(TrackSorting.sort(tracks, by: .artist, direction: .ascending).map(\.id) == ["2", "1"])
    }

    @Test func handlesDotNetDates() {
        let tracks = [
            track("a", added: "2024-01-01T10:00:00.1234567"),
            track("b", added: "2024-01-01T10:00:01.5+02:00"),
            track("c", added: "2024-01-01T12:00:00Z"),
        ]
        #expect(TrackSorting.sort(tracks, by: .added, direction: .ascending).map(\.id) == ["b", "a", "c"])
    }
}

struct FormattingTests {
    @Test(arguments: [
        (0.0, "0:00"),
        (-5.0, "0:00"),
        (Double.nan, "0:00"),
        (59.9, "0:59"),
        (61.0, "1:01"),
        (3600.0, "1:00:00"),
        (3725.0, "1:02:05"),
    ])
    func formatsDuration(seconds: Double, expected: String) {
        #expect(Formatting.duration(seconds) == expected)
    }

    @Test(arguments: [
        (Int64(0), "0 B"),
        (Int64(512), "512 B"),
        (Int64(1024), "1 KB"),
        (Int64(1536), "1.5 KB"),
        (Int64(5 * 1024 * 1024), "5 MB"),
        (Int64(3 * 1024 * 1024 * 1024), "3 GB"),
    ])
    func formatsBytes(bytes: Int64, expected: String) {
        #expect(Formatting.bytes(bytes) == expected)
    }

    @Test(arguments: [
        ("00:01:30", 90.0),
        ("01:00:00.5000000", 3600.5),
        ("1.02:00:00", 93600.0),
        ("-00:00:10", -10.0),
    ])
    func parsesTimeSpans(value: String, expected: Double) {
        #expect(TimeSpanParser.parse(value) == expected)
    }

    @Test func rejectsInvalidTimeSpan() {
        #expect(TimeSpanParser.parse("soon") == nil)
    }

    @Test func parsesDates() throws {
        let date = try #require(DateParsing.parse("2024-03-01T12:30:00.1234567Z"))
        #expect(date.timeIntervalSince1970 == 1_709_296_200.123)
        #expect(DateParsing.parse("2024-01-01") != nil)
        #expect(DateParsing.parse("not a date") == nil)
    }
}

struct ReplayGainTests {
    private let track = TrackInfo(id: "1", title: "t", replayGainTrackGain: -6, replayGainAlbumGain: -3)

    @Test func offReturnsUnityGain() {
        #expect(ReplayGain.linearGain(for: track, mode: .off) == 1)
    }

    @Test func usesTrackGain() {
        #expect(abs(ReplayGain.linearGain(for: track, mode: .track) - 0.501) < 0.001)
    }

    @Test func usesAlbumGain() {
        #expect(abs(ReplayGain.linearGain(for: track, mode: .album) - 0.708) < 0.001)
    }

    @Test func albumModeFallsBackToTrackGain() {
        let trackOnly = TrackInfo(id: "1", title: "t", replayGainTrackGain: 0)
        #expect(ReplayGain.linearGain(for: trackOnly, mode: .album) == 1)
    }

    @Test func limitsGainToAvoidClipping() {
        let loud = TrackInfo(id: "1", title: "t", replayGainTrackGain: 20)
        #expect(ReplayGain.linearGain(for: loud, mode: .track) == ReplayGain.maxLinearGain)
    }

    @Test func warnsAboutMissingData() {
        let albumOnly = TrackInfo(id: "1", title: "t", replayGainAlbumGain: -3)
        let none = TrackInfo(id: "1", title: "t")
        #expect(ReplayGain.missingDataWarning(for: albumOnly, mode: .track) == "Missing Track ReplayGain (Album ReplayGain available)")
        #expect(ReplayGain.missingDataWarning(for: albumOnly, mode: .album) == nil)
        #expect(ReplayGain.missingDataWarning(for: none, mode: .album) == "Missing Track and Album ReplayGain")
        #expect(ReplayGain.missingDataWarning(for: none, mode: .off) == nil)
    }

    @Test func volumeCurve() {
        #expect(Volume.perceptualAmplitude(0) == 0)
        #expect(Volume.perceptualAmplitude(1) == 1)
        #expect(abs(Volume.perceptualAmplitude(0.5) - 0.316) < 0.001)
        #expect(abs(Volume.perceptualAmplitude(2) - 3.162) < 0.001)
    }
}

struct PlaybackSourceTests {
    private let opus160 = StreamingQuality(format: .opus, maxBitRate: 160)

    @Test func offlineUsesCacheOrFails() {
        #expect(PlaybackSourceResolver.resolve(cachedQuality: .raw, desiredQuality: opus160, isOnline: false, networkType: .normal, preventDownloadOnLowData: false) == .cache)
        let result = PlaybackSourceResolver.resolve(cachedQuality: nil, desiredQuality: opus160, isOnline: false, networkType: .normal, preventDownloadOnLowData: false)
        guard case .unavailable = result else {
            Issue.record("Expected unavailable, got \(result)")
            return
        }
    }

    @Test func lowDataWithPreventDownloadDoesNotStream() {
        let result = PlaybackSourceResolver.resolve(cachedQuality: nil, desiredQuality: opus160, isOnline: true, networkType: .lowData, preventDownloadOnLowData: true)
        guard case .unavailable = result else {
            Issue.record("Expected unavailable, got \(result)")
            return
        }
    }

    @Test func streamsWhenNotCached() {
        #expect(PlaybackSourceResolver.resolve(cachedQuality: nil, desiredQuality: opus160, isOnline: true, networkType: .lowData, preventDownloadOnLowData: false) == .stream(opus160))
    }

    @Test func cacheQualityRules() {
        let opus128 = StreamingQuality(format: .opus, maxBitRate: 128)
        #expect(PlaybackSourceResolver.shouldUseCache(cachedQuality: .raw, desiredQuality: opus160, isOnline: true))
        #expect(!PlaybackSourceResolver.shouldUseCache(cachedQuality: opus160, desiredQuality: .raw, isOnline: true))
        #expect(PlaybackSourceResolver.shouldUseCache(cachedQuality: opus160, desiredQuality: opus128, isOnline: true))
        #expect(!PlaybackSourceResolver.shouldUseCache(cachedQuality: opus128, desiredQuality: opus160, isOnline: true))
        #expect(!PlaybackSourceResolver.shouldUseCache(cachedQuality: StreamingQuality(format: .mp3, maxBitRate: 320), desiredQuality: opus160, isOnline: true))
        #expect(PlaybackSourceResolver.shouldUseCache(cachedQuality: opus128, desiredQuality: opus160, isOnline: false))
    }

    @Test func streamsBetterQualityThanCache() {
        let opus128 = StreamingQuality(format: .opus, maxBitRate: 128)
        #expect(PlaybackSourceResolver.resolve(cachedQuality: opus128, desiredQuality: opus160, isOnline: true, networkType: .normal, preventDownloadOnLowData: false) == .stream(opus160))
    }

    @Test func fallbackQualities() {
        #expect(PlaybackSourceResolver.fallbackQuality(for: opus160) == StreamingQuality(format: .m4a, maxBitRate: 160))
        #expect(PlaybackSourceResolver.fallbackQuality(for: .raw) == StreamingQuality(format: .flac))
        #expect(PlaybackSourceResolver.fallbackQuality(for: StreamingQuality(format: .mp3, maxBitRate: 320)) == nil)
    }
}

struct ScrollWheelTests {
    @Test func wheelNotchIsOneStepRegardlessOfAcceleration() {
        #expect(ScrollWheel.steps(deltaX: 0, deltaY: 1, hasPreciseDeltas: false, isDirectionInverted: false) == -1)
        #expect(ScrollWheel.steps(deltaX: 0, deltaY: 7.5, hasPreciseDeltas: false, isDirectionInverted: false) == -1)
        #expect(ScrollWheel.steps(deltaX: 0, deltaY: -3, hasPreciseDeltas: false, isDirectionInverted: false) == 1)
    }

    @Test func naturalScrollingKeepsPhysicalDirection() {
        #expect(ScrollWheel.steps(deltaX: 0, deltaY: -1, hasPreciseDeltas: false, isDirectionInverted: true) == -1)
        #expect(ScrollWheel.steps(deltaX: 0, deltaY: -20, hasPreciseDeltas: true, isDirectionInverted: true) == -2)
    }

    @Test func trackpadProducesFractionalSteps() {
        #expect(ScrollWheel.steps(deltaX: 0, deltaY: 5, hasPreciseDeltas: true, isDirectionInverted: false) == -0.5)
    }

    @Test func horizontalScrollingToTheRightIncreases() {
        #expect(ScrollWheel.steps(deltaX: -10, deltaY: 1, hasPreciseDeltas: true, isDirectionInverted: false) == 1)
        #expect(ScrollWheel.steps(deltaX: 10, deltaY: 0, hasPreciseDeltas: true, isDirectionInverted: false) == -1)
    }

    @Test func noMovementIsNoStep() {
        #expect(ScrollWheel.steps(deltaX: 0, deltaY: 0, hasPreciseDeltas: true, isDirectionInverted: false) == 0)
    }
}
