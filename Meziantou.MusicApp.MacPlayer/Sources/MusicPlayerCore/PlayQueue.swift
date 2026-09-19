import Foundation

/// Manages the playback queue using a single array:
/// history (before `currentIndex`), the current track, and lookahead (after `currentIndex`).
/// Navigation moves the index and refills the lookahead from the playlist as needed.
public struct PlayQueue: Sendable {
    public static let maxQueueSize = 200
    public static let minLookahead = 20
    /// When trimming, keep this many items before the current one.
    public static let keepHistoryItems = 20
    /// When trimming, keep this many items after the current one.
    public static let keepLookaheadItems = 30
    public static let maxLoopCount = 10

    public private(set) var items: [QueueItem] = []
    public private(set) var currentIndex = -1

    public private(set) var playlistId: String?
    public private(set) var playlist: [TrackInfo] = []
    public private(set) var shuffleOrder: [Int] = []
    public private(set) var shuffleEnabled = false
    public private(set) var repeatMode = RepeatMode.off

    /// Tracks that are downloaded; when filtering is active only these are queued.
    public var cachedTrackIds: Set<String> = []
    public var isOnline = true
    public var networkType = NetworkType.normal
    public var preventDownloadOnLowData = false

    public init() {
    }

    // MARK: Accessors

    public var currentItem: QueueItem? {
        items.indices.contains(currentIndex) ? items[currentIndex] : nil
    }

    public var currentTrack: TrackInfo? {
        currentItem?.track
    }

    /// Items after the current one.
    public var lookahead: ArraySlice<QueueItem> {
        currentIndex < 0 ? items[...] : items[(currentIndex + 1)...]
    }

    /// Items before the current one.
    public var history: ArraySlice<QueueItem> {
        currentIndex < 0 ? [] : items[..<currentIndex]
    }

    /// Play-order position of the current track in the playlist, or -1 when it is not from the playlist.
    public var currentPlaylistPosition: Int {
        guard let item = currentItem, item.source == .playlist else {
            return -1
        }

        return playOrderPosition(ofPlaylistIndex: item.indexInPlaylist)
    }

    // MARK: Configuration

    /// Sets the playlist and clears the queue.
    public mutating func setPlaylist(id: String, tracks: [TrackInfo], shuffleOrder initialShuffleOrder: [Int]? = nil) {
        playlistId = id
        playlist = tracks
        items = []
        currentIndex = -1

        if shuffleEnabled {
            if let initialShuffleOrder, initialShuffleOrder.count == tracks.count {
                shuffleOrder = initialShuffleOrder
            } else {
                shuffleOrder = Self.makeShuffleOrder(count: tracks.count)
            }
        }
    }

    public mutating func setShuffle(_ enabled: Bool) {
        guard shuffleEnabled != enabled else {
            return
        }

        shuffleEnabled = enabled
        if enabled {
            shuffleOrder = Self.makeShuffleOrder(count: playlist.count)
        }

        // Keep history and current, regenerate the lookahead with the new order
        if currentIndex >= 0 {
            items = Array(items[...currentIndex])
            refillLookahead()
        }
    }

    public mutating func setRepeatMode(_ mode: RepeatMode) {
        repeatMode = mode
    }

    /// Restores a persisted state. A shuffle order that does not match the playlist is regenerated.
    public mutating func restore(playlistId: String?, playlist: [TrackInfo], shuffleEnabled: Bool, shuffleOrder: [Int], repeatMode: RepeatMode, items: [QueueItem], currentIndex: Int) {
        self.playlistId = playlistId
        self.playlist = playlist
        self.shuffleEnabled = shuffleEnabled
        self.shuffleOrder = shuffleEnabled && shuffleOrder.count != playlist.count ? Self.makeShuffleOrder(count: playlist.count) : shuffleOrder
        self.repeatMode = repeatMode
        self.items = items
        self.currentIndex = items.isEmpty ? -1 : min(max(currentIndex, -1), items.count - 1)
        refillLookahead()
    }

    // MARK: Playback

    /// Starts playback at a play-order position of the playlist.
    @discardableResult
    public mutating func play(atPosition position: Int) -> Bool {
        guard position >= 0, position < playlist.count, let playlistId else {
            return false
        }

        let actualIndex = playlistIndex(forPosition: position)
        items = [QueueItem(track: playlist[actualIndex], playlistId: playlistId, indexInPlaylist: actualIndex, source: .playlist)]
        currentIndex = 0
        refillLookahead(startPosition: position)
        return true
    }

    /// Starts playback of a track of the playlist, identified by its playlist index.
    @discardableResult
    public mutating func play(playlistIndex: Int) -> Bool {
        guard playlist.indices.contains(playlistIndex) else {
            return false
        }

        return play(atPosition: playOrderPosition(ofPlaylistIndex: playlistIndex))
    }

    /// Inserts a track right after the current one.
    public mutating func addToQueue(_ track: TrackInfo, playlistId: String, indexInPlaylist: Int) {
        let item = QueueItem(track: track, playlistId: playlistId, indexInPlaylist: indexInPlaylist, source: .manual)
        if currentIndex >= 0 {
            items.insert(item, at: currentIndex + 1)
        } else {
            items.insert(item, at: 0)
            currentIndex = 0
        }

        trimIfNeeded()
    }

    /// Removes an item by absolute index. The current item cannot be removed.
    public mutating func remove(at index: Int) {
        guard items.indices.contains(index), index != currentIndex else {
            return
        }

        items.remove(at: index)
        if index < currentIndex {
            currentIndex -= 1
        }
    }

    /// Moves an item (absolute indices). The current item cannot be moved.
    public mutating func move(from fromIndex: Int, to toIndex: Int) {
        guard items.indices.contains(fromIndex), items.indices.contains(toIndex), fromIndex != currentIndex, fromIndex != toIndex else {
            return
        }

        let item = items.remove(at: fromIndex)
        items.insert(item, at: toIndex)

        if fromIndex < currentIndex && toIndex >= currentIndex {
            currentIndex -= 1
        } else if fromIndex > currentIndex && toIndex <= currentIndex {
            currentIndex += 1
        }
    }

    public var hasNext: Bool {
        if repeatMode != .off {
            return !playlist.isEmpty
        }

        if currentIndex < 0 {
            return !items.isEmpty
        }

        return currentIndex < items.count - 1
    }

    public var hasPrevious: Bool {
        guard playlistId != nil, !playlist.isEmpty else {
            return false
        }

        if currentIndex > 0 {
            return true
        }

        if shuffleEnabled {
            return playlist.count > 1
        }

        if repeatMode != .off {
            return true
        }

        guard let item = currentItem, item.source == .playlist else {
            return false
        }

        return playOrderPosition(ofPlaylistIndex: item.indexInPlaylist) > 0
    }

    /// Advances to the next item.
    /// - Parameter force: when false and the repeat mode is `.one`, stays on the current track.
    @discardableResult
    public mutating func next(force: Bool = false) -> Bool {
        guard hasNext else {
            return false
        }

        if !force && repeatMode == .one {
            return true
        }

        if currentIndex >= items.count - 1 {
            refillLookahead()
        }

        guard currentIndex < items.count - 1 else {
            return false
        }

        currentIndex += 1
        refillLookahead()
        trimIfNeeded()
        return true
    }

    /// Makes the item at an absolute index the current one, keeping the skipped items in the history.
    @discardableResult
    public mutating func jump(to index: Int) -> Bool {
        guard items.indices.contains(index) else {
            return false
        }

        currentIndex = index
        refillLookahead()
        trimIfNeeded()
        return true
    }

    /// Goes back to the previous item, generating one at the start of the queue when needed.
    @discardableResult
    public mutating func previous() -> Bool {
        guard hasPrevious else {
            return false
        }

        if currentIndex == 0 {
            guard let item = makePreviousItem() else {
                return false
            }

            // The current index stays 0, which is now the previous track
            items.insert(item, at: 0)
            trimIfNeeded()
            return true
        }

        currentIndex -= 1
        return true
    }

    // MARK: Internals

    private var shouldFilterUncachedTracks: Bool {
        !isOnline || (networkType == .lowData && preventDownloadOnLowData)
    }

    private func makePreviousItem() -> QueueItem? {
        guard let playlistId, !playlist.isEmpty else {
            return nil
        }

        if shuffleEnabled {
            let currentActualIndex = currentItem?.indexInPlaylist ?? -1
            var randomIndex = 0
            if playlist.count > 1 {
                repeat {
                    randomIndex = Int.random(in: 0..<playlist.count)
                } while randomIndex == currentActualIndex
            }

            return QueueItem(track: playlist[randomIndex], playlistId: playlistId, indexInPlaylist: randomIndex, source: .playlist)
        }

        guard let item = currentItem else {
            return nil
        }

        var previousPosition = playOrderPosition(ofPlaylistIndex: item.indexInPlaylist) - 1
        if previousPosition < 0 {
            if repeatMode == .off {
                return nil
            }

            previousPosition = playlist.count - 1
        }

        let actualIndex = playlistIndex(forPosition: previousPosition)
        return QueueItem(track: playlist[actualIndex], playlistId: playlistId, indexInPlaylist: actualIndex, source: .playlist)
    }

    private mutating func refillLookahead(startPosition: Int? = nil) {
        guard let playlistId, !playlist.isEmpty else {
            return
        }

        let lookaheadCount = items.count - currentIndex - 1
        guard lookaheadCount < Self.minLookahead else {
            return
        }

        let itemsNeeded = Self.minLookahead - lookaheadCount

        var nextPosition: Int
        if let startPosition {
            nextPosition = startPosition + 1
        } else if let last = items.last, last.source == .playlist {
            nextPosition = playOrderPosition(ofPlaylistIndex: last.indexInPlaylist) + 1
        } else if let current = currentItem, current.source == .playlist {
            nextPosition = playOrderPosition(ofPlaylistIndex: current.indexInPlaylist) + 1
        } else {
            nextPosition = 0
        }

        var queuedTrackIds = Set(items.map(\.track.id))
        let filterUncached = shouldFilterUncachedTracks
        var newItems: [QueueItem] = []
        var loopCount = 0

        while newItems.count < itemsNeeded && loopCount < Self.maxLoopCount {
            var position = nextPosition
            while position < playlist.count && newItems.count < itemsNeeded {
                let actualIndex = playlistIndex(forPosition: position)
                let track = playlist[actualIndex]
                position += 1

                if filterUncached && !cachedTrackIds.contains(track.id) {
                    continue
                }

                // Skip duplicates unless every track of the playlist is already queued
                if queuedTrackIds.contains(track.id) && queuedTrackIds.count < playlist.count {
                    continue
                }

                newItems.append(QueueItem(track: track, playlistId: playlistId, indexInPlaylist: actualIndex, source: .playlist))
                queuedTrackIds.insert(track.id)
            }

            if newItems.count < itemsNeeded && repeatMode == .all {
                nextPosition = 0
                loopCount += 1
            } else {
                break
            }
        }

        items.append(contentsOf: newItems)
    }

    private mutating func trimIfNeeded() {
        guard items.count > Self.maxQueueSize, currentIndex >= 0 else {
            return
        }

        let keepBefore = min(Self.keepHistoryItems, currentIndex)
        let start = currentIndex - keepBefore
        let end = min(currentIndex + Self.keepLookaheadItems + 1, items.count)
        items = Array(items[start..<end])
        currentIndex = keepBefore
        refillLookahead()
    }

    /// The playlist index of the track at a play-order position.
    public func playlistIndex(forPosition position: Int) -> Int {
        if shuffleEnabled, shuffleOrder.indices.contains(position) {
            return shuffleOrder[position]
        }

        return position
    }

    /// The play-order position of a playlist index.
    public func playOrderPosition(ofPlaylistIndex index: Int) -> Int {
        guard !playlist.isEmpty else {
            return index
        }

        let actualIndex = index % playlist.count
        if shuffleEnabled, !shuffleOrder.isEmpty {
            return shuffleOrder.firstIndex(of: actualIndex) ?? actualIndex
        }

        return actualIndex
    }

    static func makeShuffleOrder(count: Int) -> [Int] {
        Array(0..<count).shuffled()
    }
}
