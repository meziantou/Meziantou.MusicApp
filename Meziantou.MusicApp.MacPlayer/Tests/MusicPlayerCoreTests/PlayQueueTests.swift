import Testing
@testable import MusicPlayerCore

private func makeTrack(_ id: String) -> TrackInfo {
    TrackInfo(id: id, title: "Track \(id)", path: "/test/\(id)", artists: "Test Artist", album: "Test Album", duration: 180, addedDate: "2024-01-01")
}

private func makeQueue(trackCount: Int = 5, playAt position: Int? = 0) -> PlayQueue {
    var queue = PlayQueue()
    queue.setPlaylist(id: "playlist1", tracks: (1...trackCount).map { makeTrack(String($0)) })
    if let position {
        queue.play(atPosition: position)
    }

    return queue
}

struct PlayQueueTests {
    @Test func initializesEmpty() {
        let queue = PlayQueue()
        #expect(queue.items.isEmpty)
        #expect(queue.currentIndex == -1)
        #expect(queue.currentTrack == nil)
    }

    @Test func playsAtIndex() {
        let queue = makeQueue()
        #expect(queue.currentTrack?.id == "1")
        #expect(queue.currentIndex == 0)
    }

    @Test func rejectsOutOfRangePosition() {
        var queue = makeQueue(playAt: nil)
        let playedAfterEnd = queue.play(atPosition: 5)
        #expect(!playedAfterEnd)
        let playedBeforeStart = queue.play(atPosition: -1)
        #expect(!playedBeforeStart)
    }

    @Test func generatesLookahead() {
        let queue = makeQueue()
        #expect(queue.items.count == 5)
        #expect(queue.lookahead.map(\.track.id) == ["2", "3", "4", "5"])
        #expect(queue.lookahead.allSatisfy { $0.source == .playlist })
    }

    @Test func lookaheadIsLimitedToMinimum() {
        let queue = makeQueue(trackCount: 100)
        #expect(queue.lookahead.count == PlayQueue.minLookahead)
    }

    @Test func navigatesForward() {
        var queue = makeQueue()
        let succeeded = queue.next()
        #expect(succeeded)
        #expect(queue.currentTrack?.id == "2")
    }

    @Test func navigatesBackward() {
        var queue = makeQueue()
        queue.next()
        let succeeded = queue.previous()
        #expect(succeeded)
        #expect(queue.currentTrack?.id == "1")
    }

    @Test func previousAtStartWithRepeatAllWrapsToLastTrack() {
        var queue = makeQueue()
        queue.setRepeatMode(.all)
        let succeeded = queue.previous()
        #expect(succeeded)
        #expect(queue.currentIndex == 0)
        #expect(queue.currentTrack?.id == "5")
    }

    @Test func previousAtStartWithoutRepeatFails() {
        var queue = makeQueue()
        #expect(!queue.hasPrevious)
        let succeeded = queue.previous()
        #expect(!succeeded)
    }

    @Test func previousAtStartOfQueueGoesToPreviousPlaylistTrack() {
        var queue = makeQueue(playAt: 2)
        let succeeded = queue.previous()
        #expect(succeeded)
        #expect(queue.currentTrack?.id == "2")
    }

    @Test func hasNextDependsOnRepeatMode() {
        var queue = makeQueue(playAt: 4)
        #expect(!queue.hasNext)
        queue.setRepeatMode(.all)
        #expect(queue.hasNext)
    }

    @Test func repeatAllLoopsOverThePlaylist() {
        var queue = makeQueue(playAt: 4)
        queue.setRepeatMode(.all)
        let succeeded = queue.next()
        #expect(succeeded)
        #expect(queue.currentTrack?.id == "1")
    }

    @Test func repeatOneStaysOnTrackUnlessForced() {
        var queue = makeQueue()
        queue.setRepeatMode(.one)
        let repeated = queue.next()
        #expect(repeated)
        #expect(queue.currentTrack?.id == "1")
        let forced = queue.next(force: true)
        #expect(forced)
        #expect(queue.currentTrack?.id == "2")
    }

    @Test func shufflePreviousAtStartPicksAnotherTrack() {
        var queue = makeQueue(playAt: nil)
        queue.setShuffle(true)
        queue.play(atPosition: 0)
        let currentId = queue.currentTrack?.id
        let succeeded = queue.previous()
        #expect(succeeded)
        #expect(queue.currentTrack?.id != currentId)
    }

    @Test func shuffleOrderIsAPermutation() {
        var queue = makeQueue(trackCount: 50, playAt: nil)
        queue.setShuffle(true)
        #expect(queue.shuffleOrder.sorted() == Array(0..<50))
    }

    @Test func togglingShuffleRebuildsLookahead() {
        var queue = makeQueue(trackCount: 30)
        queue.setShuffle(true)
        #expect(queue.currentTrack?.id == "1")
        // The lookahead continues from the position of the current track in the new shuffle order
        let remainingInShuffleOrder = 30 - queue.currentPlaylistPosition - 1
        #expect(queue.lookahead.count == min(PlayQueue.minLookahead, remainingInShuffleOrder))
        #expect(queue.lookahead.map(\.indexInPlaylist) == Array(queue.shuffleOrder.dropFirst(queue.currentPlaylistPosition + 1).prefix(PlayQueue.minLookahead)))
    }

    @Test func playPlaylistIndexWithShuffleUsesPlayOrderPosition() {
        var queue = makeQueue(trackCount: 10, playAt: nil)
        queue.setShuffle(true)
        let succeeded = queue.play(playlistIndex: 7)
        #expect(succeeded)
        #expect(queue.currentTrack?.id == "8")
        #expect(queue.currentPlaylistPosition == queue.shuffleOrder.firstIndex(of: 7))
    }

    @Test func setPlaylistKeepsProvidedShuffleOrder() {
        var queue = PlayQueue()
        queue.setShuffle(true)
        queue.setPlaylist(id: "p", tracks: (1...3).map { makeTrack(String($0)) }, shuffleOrder: [2, 0, 1])
        #expect(queue.shuffleOrder == [2, 0, 1])
        queue.play(atPosition: 0)
        #expect(queue.currentTrack?.id == "3")
    }

    @Test func addsManualTrackRightAfterCurrent() {
        var queue = makeQueue()
        queue.addToQueue(makeTrack("99"), playlistId: "playlist1", indexInPlaylist: 0)
        #expect(queue.items[1].track.id == "99")
        #expect(queue.items[1].source == .manual)
        #expect(queue.currentIndex == 0)
    }

    @Test func addToEmptyQueueMakesItCurrent() {
        var queue = PlayQueue()
        queue.addToQueue(makeTrack("99"), playlistId: "playlist1", indexInPlaylist: 0)
        #expect(queue.currentTrack?.id == "99")
    }

    @Test func manualTrackPlaysNext() {
        var queue = makeQueue()
        queue.addToQueue(makeTrack("99"), playlistId: "playlist1", indexInPlaylist: 0)
        queue.next()
        #expect(queue.currentTrack?.id == "99")
        queue.next()
        #expect(queue.currentTrack?.id == "2")
    }

    @Test func jumpsToQueueItem() {
        var queue = makeQueue(trackCount: 30)
        let succeeded = queue.jump(to: 3)
        #expect(succeeded)
        #expect(queue.currentTrack?.id == "4")
        #expect(queue.history.count == 3)
        #expect(queue.lookahead.count == PlayQueue.minLookahead)
        let jumpedOutOfRange = queue.jump(to: 999)
        #expect(!jumpedOutOfRange)
    }

    @Test func removesItem() {
        var queue = makeQueue()
        let count = queue.items.count
        queue.remove(at: 2)
        #expect(queue.items.count == count - 1)
    }

    @Test func doesNotRemoveCurrentItem() {
        var queue = makeQueue()
        let count = queue.items.count
        queue.remove(at: queue.currentIndex)
        #expect(queue.items.count == count)
    }

    @Test func removingHistoryItemAdjustsCurrentIndex() {
        var queue = makeQueue()
        queue.next()
        queue.next()
        queue.remove(at: 0)
        #expect(queue.currentIndex == 1)
        #expect(queue.currentTrack?.id == "3")
    }

    @Test func movesItems() {
        var queue = makeQueue()
        let itemAt2 = queue.items[2]
        queue.move(from: 2, to: 4)
        #expect(queue.items[4].id == itemAt2.id)
        #expect(queue.currentIndex == 0)
    }

    @Test func movingItemBeforeCurrentAdjustsIndex() {
        var queue = makeQueue()
        queue.next()
        queue.move(from: 3, to: 0)
        #expect(queue.currentIndex == 2)
        #expect(queue.currentTrack?.id == "2")
    }

    @Test func offlineFiltersUncachedTracks() {
        var queue = PlayQueue()
        queue.isOnline = false
        queue.cachedTrackIds = ["1", "3", "5"]
        queue.setPlaylist(id: "playlist1", tracks: (1...5).map { makeTrack(String($0)) })
        queue.play(atPosition: 0)
        #expect(queue.lookahead.map(\.track.id) == ["3", "5"])
    }

    @Test func lowDataWithPreventDownloadFiltersUncachedTracks() {
        var queue = PlayQueue()
        queue.networkType = .lowData
        queue.preventDownloadOnLowData = true
        queue.cachedTrackIds = ["4"]
        queue.setPlaylist(id: "playlist1", tracks: (1...5).map { makeTrack(String($0)) })
        queue.play(atPosition: 0)
        #expect(queue.lookahead.map(\.track.id) == ["4"])
    }

    @Test func trimsQueueWhenTooLarge() {
        var queue = makeQueue(trackCount: 20)
        for index in 0..<PlayQueue.maxQueueSize {
            queue.addToQueue(makeTrack("m\(index)"), playlistId: "playlist1", indexInPlaylist: 0)
        }

        #expect(queue.items.count <= PlayQueue.maxQueueSize)
        #expect(queue.currentTrack?.id == "1")
    }

    @Test func hasNextIsTrueForQueuedItemsWithoutAPlaylist() {
        var queue = PlayQueue()
        queue.addToQueue(makeTrack("1"), playlistId: "playlist1", indexInPlaylist: 0)
        queue.addToQueue(makeTrack("2"), playlistId: "playlist1", indexInPlaylist: 1)
        queue.setRepeatMode(.all)
        #expect(queue.hasNext)
        let advanced = queue.next(force: true)
        #expect(advanced)
        #expect(queue.currentTrack?.id == "2")
    }

    @Test func repeatAllCannotAdvanceWhenNoOtherTrackCanBeQueued() {
        var queue = PlayQueue()
        queue.isOnline = false
        queue.cachedTrackIds = ["1"]
        queue.setPlaylist(id: "playlist1", tracks: (1...5).map { makeTrack(String($0)) })
        queue.setRepeatMode(.all)
        queue.play(atPosition: 0)
        #expect(queue.lookahead.isEmpty)
        // Repeating cannot produce a track to play: callers must not be left waiting for one
        let advanced = queue.next(force: true)
        #expect(!advanced)
    }

    @Test func restoresState() {
        let original = makeQueue(playAt: 2)
        var restored = PlayQueue()
        restored.restore(
            playlistId: "playlist1",
            playlist: original.playlist,
            shuffleEnabled: false,
            shuffleOrder: [],
            repeatMode: .off,
            items: original.items,
            currentIndex: original.currentIndex)
        #expect(restored.currentTrack?.id == "3")
        #expect(restored.currentIndex == original.currentIndex)
    }

    @Test func restoreClampsInvalidIndex() {
        let original = makeQueue()
        var restored = PlayQueue()
        restored.restore(playlistId: "playlist1", playlist: [], shuffleEnabled: false, shuffleOrder: [], repeatMode: .off, items: original.items, currentIndex: 999)
        #expect(restored.currentIndex == original.items.count - 1)
    }
}
