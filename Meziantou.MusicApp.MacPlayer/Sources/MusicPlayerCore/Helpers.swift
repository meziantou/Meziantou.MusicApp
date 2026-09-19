import Foundation

public enum TrackSortOption: String, Codable, Sendable, CaseIterable {
    case added
    case title
    case artist
    case album

    public var label: String {
        switch self {
        case .added: "Added Date"
        case .title: "Title"
        case .artist: "Artist"
        case .album: "Album"
        }
    }

    /// Added date defaults to newest first, the other options to A → Z.
    public var defaultDirection: TrackSortDirection {
        self == .added ? .descending : .ascending
    }
}

public enum TrackSortDirection: String, Codable, Sendable {
    case ascending
    case descending

    public var toggled: TrackSortDirection {
        self == .ascending ? .descending : .ascending
    }
}

public enum Formatting {
    /// Formats a duration as `m:ss` or `h:mm:ss`.
    public static func duration(_ seconds: Double) -> String {
        guard seconds.isFinite, seconds > 0 else {
            return "0:00"
        }

        let totalSeconds = Int(seconds)
        let hours = totalSeconds / 3600
        let minutes = (totalSeconds % 3600) / 60
        let secs = totalSeconds % 60
        if hours > 0 {
            return String(format: "%d:%02d:%02d", hours, minutes, secs)
        }

        return String(format: "%d:%02d", minutes, secs)
    }

    /// Formats a byte count using binary units, e.g. "1.5 MB".
    public static func bytes(_ bytes: Int64) -> String {
        guard bytes > 0 else {
            return "0 B"
        }

        let units = ["B", "KB", "MB", "GB", "TB"]
        let exponent = min(units.count - 1, Int(log(Double(bytes)) / log(1024)))
        let value = Double(bytes) / pow(1024, Double(exponent))
        let rounded = (value * 10).rounded() / 10
        let text = rounded.rounded() == rounded ? String(Int(rounded)) : String(rounded)
        return "\(text) \(units[exponent])"
    }
}

public enum Search {
    /// Lowercases, removes diacritics and collapses whitespace.
    public static func normalize(_ text: String) -> String {
        text
            .folding(options: [.caseInsensitive, .diacriticInsensitive], locale: Locale(identifier: "en_US_POSIX"))
            .lowercased()
            .split(whereSeparator: { $0.isWhitespace })
            .joined(separator: " ")
    }

    /// Returns true when every word of the query is contained in the text (accent and case insensitive).
    public static func matches(_ text: String?, query: String) -> Bool {
        guard let text, !text.isEmpty else {
            return false
        }

        let fragments = normalize(query).split(separator: " ")
        if fragments.isEmpty {
            return true
        }

        let normalizedText = normalize(text)
        return fragments.allSatisfy { normalizedText.contains($0) }
    }

    /// The normalized text searched for a track: title, artists, album and ISRC.
    public static func haystack(for track: TrackInfo) -> String {
        normalize("\(track.title)\n\(track.artists ?? "")\n\(track.album ?? "")\n\(track.isrc ?? "")")
    }

    /// Filters tracks, reusing precomputed haystacks (same order as `tracks`).
    public static func filter(_ tracks: [TrackInfo], haystacks: [String], query: String) -> [TrackInfo] {
        let fragments = normalize(query).split(separator: " ")
        if fragments.isEmpty {
            return tracks
        }

        var result: [TrackInfo] = []
        for (index, track) in tracks.enumerated() where fragments.allSatisfy({ haystacks[index].contains($0) }) {
            result.append(track)
        }

        return result
    }
}

public enum TrackSorting {
    /// Stable sort of tracks. Ties keep the original playlist order.
    public static func sort(_ tracks: [TrackInfo], by option: TrackSortOption, direction: TrackSortDirection) -> [TrackInfo] {
        sortIndices(tracks, by: option, direction: direction).map { tracks[$0] }
    }

    /// Returns the playlist indices of the tracks in sorted order.
    public static func sortIndices(_ tracks: [TrackInfo], by option: TrackSortOption, direction: TrackSortDirection) -> [Int] {
        let dates: [Double]? = option == .added
            ? tracks.map { track in track.addedDate.flatMap(DateParsing.parse)?.timeIntervalSince1970 ?? 0 }
            : nil

        return tracks.indices.sorted { lhs, rhs in
            let result: ComparisonResult
            switch option {
            case .added:
                let left = dates![lhs]
                let right = dates![rhs]
                result = left < right ? .orderedAscending : (left > right ? .orderedDescending : .orderedSame)
            case .title:
                result = tracks[lhs].title.localizedCompare(tracks[rhs].title)
            case .artist:
                result = (tracks[lhs].artists ?? "").localizedCompare(tracks[rhs].artists ?? "")
            case .album:
                result = (tracks[lhs].album ?? "").localizedCompare(tracks[rhs].album ?? "")
            }

            if result == .orderedSame {
                return lhs < rhs
            }

            return direction == .ascending ? result == .orderedAscending : result == .orderedDescending
        }
    }
}

public enum DateParsing {
    // Guarded by `lock`
    nonisolated(unsafe) private static let fullFormatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    // Guarded by `lock`
    nonisolated(unsafe) private static let noFractionFormatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter
    }()

    // Guarded by `lock`
    nonisolated(unsafe) private static let dateOnlyFormatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withFullDate]
        return formatter
    }()

    private static let lock = NSLock()

    /// Parses ISO 8601 dates as produced by .NET (up to 7 fractional digits, with or without a time zone).
    public static func parse(_ value: String) -> Date? {
        // Sorting a playlist by added date parses every date: avoid the formatters for the usual formats
        var utf8Value = value
        if let date = utf8Value.withUTF8(parseFast) {
            return date
        }

        let text = value.trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty else {
            return nil
        }

        lock.lock()
        defer { lock.unlock() }

        if !text.contains("T") {
            return dateOnlyFormatter.date(from: text)
        }

        var normalized = text
        // Keep at most 3 fractional digits, which is what ISO8601DateFormatter supports
        if let dotIndex = normalized.firstIndex(of: ".") {
            let fractionStart = normalized.index(after: dotIndex)
            let fractionEnd = normalized[fractionStart...].firstIndex(where: { !$0.isNumber }) ?? normalized.endIndex
            let digits = normalized[fractionStart..<fractionEnd]
            normalized.replaceSubrange(fractionStart..<fractionEnd, with: digits.prefix(3))
        }

        let timePart = normalized[normalized.index(after: normalized.firstIndex(of: "T")!)...]
        if !(timePart.hasSuffix("Z") || timePart.contains("+") || timePart.contains("-")) {
            normalized += "Z"
        }

        return fullFormatter.date(from: normalized) ?? noFractionFormatter.date(from: normalized)
    }

    /// Parses `yyyy-MM-dd` and `yyyy-MM-ddTHH:mm:ss[.fffffff][Z|±HH:mm|±HHmm]` (UTC when there is no time zone)
    /// with the same precision as the formatters (milliseconds). Returns nil for any other format.
    private static func parseFast(_ bytes: UnsafeBufferPointer<UInt8>) -> Date? {
        var index = 0

        func consume(_ character: Unicode.Scalar) -> Bool {
            guard index < bytes.count, bytes[index] == UInt8(ascii: character) else {
                return false
            }

            index += 1
            return true
        }

        func number(digits: Int) -> Int? {
            guard index + digits <= bytes.count else {
                return nil
            }

            var value = 0
            for _ in 0..<digits {
                guard let digit = digitValue(bytes[index]) else {
                    return nil
                }

                value = value * 10 + digit
                index += 1
            }

            return value
        }

        guard let year = number(digits: 4), consume("-"), let month = number(digits: 2), consume("-"), let day = number(digits: 2),
              (1...12).contains(month), (1...daysInMonth(month, year: year)).contains(day) else {
            return nil
        }

        var seconds = daysSince1970(year: year, month: month, day: day) * 86_400
        if index == bytes.count {
            return Date(timeIntervalSince1970: Double(seconds))
        }

        guard consume("T"), let hour = number(digits: 2), consume(":"), let minute = number(digits: 2), consume(":"), let second = number(digits: 2),
              hour < 24, minute < 60, second < 60 else {
            return nil
        }

        seconds += hour * 3600 + minute * 60 + second

        // Only milliseconds are kept, like the formatters
        var milliseconds = 0
        if consume(".") {
            var digitCount = 0
            while index < bytes.count, let digit = digitValue(bytes[index]) {
                if digitCount < 3 {
                    milliseconds = milliseconds * 10 + digit
                }

                digitCount += 1
                index += 1
            }

            guard digitCount > 0 else {
                return nil
            }

            for _ in min(digitCount, 3)..<3 {
                milliseconds *= 10
            }
        }

        if consume("Z") {
            // UTC
        } else if consume("+") || consume("-") {
            let sign = bytes[index - 1] == UInt8(ascii: "+") ? 1 : -1
            guard let offsetHours = number(digits: 2) else {
                return nil
            }

            _ = consume(":")
            guard let offsetMinutes = number(digits: 2), offsetHours < 24, offsetMinutes < 60 else {
                return nil
            }

            seconds -= sign * (offsetHours * 3600 + offsetMinutes * 60)
        }

        guard index == bytes.count else {
            return nil
        }

        return Date(timeIntervalSince1970: Double(seconds) + Double(milliseconds) / 1000)
    }

    private static func digitValue(_ byte: UInt8) -> Int? {
        (UInt8(ascii: "0")...UInt8(ascii: "9")).contains(byte) ? Int(byte - UInt8(ascii: "0")) : nil
    }

    private static func daysInMonth(_ month: Int, year: Int) -> Int {
        switch month {
        case 2:
            let isLeapYear = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)
            return isLeapYear ? 29 : 28
        case 4, 6, 9, 11:
            return 30
        default:
            return 31
        }
    }

    /// Days between 1970-01-01 and a date of the proleptic Gregorian calendar.
    private static func daysSince1970(year: Int, month: Int, day: Int) -> Int {
        let shiftedYear = month <= 2 ? year - 1 : year
        let era = (shiftedYear >= 0 ? shiftedYear : shiftedYear - 399) / 400
        let yearOfEra = shiftedYear - era * 400
        let dayOfYear = (153 * ((month + 9) % 12) + 2) / 5 + day - 1
        let dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear
        return era * 146_097 + dayOfEra - 719_468
    }
}

public enum TimeSpanParser {
    /// Parses a .NET TimeSpan (`[-][d.]hh:mm:ss[.fffffff]`) into seconds.
    public static func parse(_ value: String) -> TimeInterval? {
        var text = Substring(value.trimmingCharacters(in: .whitespaces))
        var sign = 1.0
        if text.hasPrefix("-") {
            sign = -1
            text = text.dropFirst()
        }

        let parts = text.split(separator: ":")
        guard parts.count == 3 else {
            return nil
        }

        var days = 0.0
        var hoursText = parts[0]
        if let dot = hoursText.firstIndex(of: ".") {
            guard let parsedDays = Double(hoursText[..<dot]) else {
                return nil
            }

            days = parsedDays
            hoursText = hoursText[hoursText.index(after: dot)...]
        }

        guard let hours = Double(hoursText), let minutes = Double(parts[1]), let seconds = Double(parts[2]) else {
            return nil
        }

        return sign * (days * 86400 + hours * 3600 + minutes * 60 + seconds)
    }
}

public enum ReplayGain {
    /// Maximum linear gain applied, to limit clipping (≈ +6 dB).
    public static let maxLinearGain: Double = 2

    /// The linear gain to apply for a track, or 1 when ReplayGain is disabled or unavailable.
    public static func linearGain(for track: TrackInfo, mode: ReplayGainMode) -> Double {
        let gainDb: Double?
        switch mode {
        case .off:
            gainDb = nil
        case .track:
            gainDb = track.replayGainTrackGain
        case .album:
            gainDb = track.replayGainAlbumGain ?? track.replayGainTrackGain
        }

        guard let gainDb, gainDb.isFinite else {
            return 1
        }

        let linear = pow(10, gainDb / 20)
        return linear.isFinite ? min(linear, maxLinearGain) : 1
    }

    /// A tooltip describing missing ReplayGain data, or nil when the track has what the mode needs.
    public static func missingDataWarning(for track: TrackInfo, mode: ReplayGainMode) -> String? {
        let hasTrackGain = track.replayGainTrackGain != nil
        let hasAlbumGain = track.replayGainAlbumGain != nil
        switch mode {
        case .off:
            return nil
        case .track:
            if hasTrackGain {
                return nil
            }

            return hasAlbumGain ? "Missing Track ReplayGain (Album ReplayGain available)" : "Missing Track and Album ReplayGain"
        case .album:
            return hasAlbumGain || hasTrackGain ? nil : "Missing Track and Album ReplayGain"
        }
    }
}

public enum Volume {
    /// Maps a slider value (0...2) to an amplitude using a -10 dB per halving curve:
    /// 100% → 1.0 (0 dB), 50% → ~0.316 (-10 dB), 200% → ~3.16 (+10 dB).
    public static func perceptualAmplitude(_ volume: Double) -> Double {
        guard volume > 0 else {
            return 0
        }

        return pow(volume, log2(10) / 2)
    }
}

public enum ScrollWheel {
    /// Trackpad points that count as one wheel notch.
    public static let pointsPerStep: Double = 10

    /// Converts a scroll event into slider steps: positive when scrolling up or right, whatever the
    /// "natural scrolling" setting, like turning a knob. A wheel notch is one step; trackpad
    /// (precise) scrolling produces fractional steps for smooth adjustments.
    public static func steps(deltaX: Double, deltaY: Double, hasPreciseDeltas: Bool, isDirectionInverted: Bool) -> Double {
        // Use the dominant axis; natural scrolling reports inverted deltas
        let physicalDelta = abs(deltaY) >= abs(deltaX) ? deltaY : -deltaX
        let delta = isDirectionInverted ? -physicalDelta : physicalDelta
        guard delta != 0 else {
            return 0
        }

        if hasPreciseDeltas {
            return delta / pointsPerStep
        }

        // Mouse wheels report accelerated line deltas: count one step per notch
        return delta > 0 ? 1 : -1
    }
}
