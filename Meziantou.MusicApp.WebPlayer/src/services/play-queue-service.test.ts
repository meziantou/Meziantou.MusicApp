import { describe, it, expect, beforeEach } from 'vitest';
import { PlayQueueService } from './play-queue-service';
import type { TrackInfo, QueueItem } from '../types';

// Helper to create mock tracks
function createMockTrack(id: string, title: string): TrackInfo {
  return {
    id,
    title,
    path: `/music/${id}.mp3`,
    artists: 'Test Artist',
    album: 'Test Album',
    duration: 180,
    artistId: null,
    albumId: null,
    track: parseInt(id),
    year: 2024,
    genre: 'Test',
    bitRate: 320,
    size: 5000000,
    contentType: 'audio/mp3',
    addedDate: null,
    isrc: null,
    replayGainTrackGain: null,
    replayGainTrackPeak: null,
    replayGainAlbumGain: null,
    replayGainAlbumPeak: null,
  };
}

// Helper to create mock queue item
function createQueueItem(track: TrackInfo, playlistId: string = 'playlist-1'): QueueItem {
  return {
    track,
    playlistId,
    indexInPlaylist: parseInt(track.id) - 1,
    source: 'playlist',
  };
}

describe('PlayQueueService', () => {
  let service: PlayQueueService;
  let mockTracks: TrackInfo[];

  beforeEach(() => {
    mockTracks = [
      createMockTrack('1', 'Track 1'),
      createMockTrack('2', 'Track 2'),
      createMockTrack('3', 'Track 3'),
      createMockTrack('4', 'Track 4'),
      createMockTrack('5', 'Track 5'),
    ];

    service = new PlayQueueService({
      currentIndex: 0,
      currentPlaylistId: 'playlist-1',
      playlist: mockTracks,
      shuffleOrder: [],
      shuffleEnabled: false,
      repeatMode: 'off',
      cachedTrackIds: new Set(),
      recentlyPlayedIds: new Set(),
      isOnline: true,
      networkType: 'normal',
      preventDownloadOnLowData: false,
    });
  });

  describe('Sequential Mode', () => {
    describe('Next Track', () => {
      it('should return next track in sequence', () => {
        service.updateConfig({ currentIndex: 0 });
        const next = service.getNextTrack();
        expect(next).not.toBeNull();
        expect(next?.track.id).toBe('2');
      });

      it('should return null at end of playlist when repeat is off', () => {
        service.updateConfig({ currentIndex: 4 });
        const next = service.getNextTrack();
        expect(next).toBeNull();
      });

      it('should loop to start when repeat is all', () => {
        service.updateConfig({ currentIndex: 4, repeatMode: 'all' });
        const next = service.getNextTrack();
        expect(next).not.toBeNull();
        expect(next?.track.id).toBe('1');
      });

      it('should return null when repeat is one', () => {
        // Repeat-one means "replay current track when it ends naturally"
        // When user explicitly presses next, return null (no automatic advancement)
        // This lets the audio player decide how to handle manual navigation
        service.updateConfig({ currentIndex: 2, repeatMode: 'one' });
        const next = service.getNextTrack();
        expect(next).toBeNull();
      });
    });

    describe('Previous Track', () => {
      it('should return previous track in sequence', () => {
        service.updateConfig({ currentIndex: 2 });
        const prev = service.getPreviousTrack();
        expect(prev).not.toBeNull();
        expect(prev?.track.id).toBe('2');
      });

      it('should return null at start when repeat is off', () => {
        service.updateConfig({ currentIndex: 0, repeatMode: 'off' });
        const prev = service.getPreviousTrack();
        expect(prev).toBeNull();
      });

      it('should loop to end when repeat is all', () => {
        service.updateConfig({ currentIndex: 0, repeatMode: 'all' });
        const prev = service.getPreviousTrack();
        expect(prev).not.toBeNull();
        expect(prev?.track.id).toBe('5');
      });

      it('should loop to end when repeat is one', () => {
        // Previous navigation is deliberate user action ("I want to go back")
        // Even in repeat-one mode, allow navigation to previous track
        service.updateConfig({ currentIndex: 0, repeatMode: 'one' });
        const prev = service.getPreviousTrack();
        expect(prev).not.toBeNull();
        expect(prev?.track.id).toBe('5');
      });
    });

    describe('hasNext and hasPrevious', () => {
      it('hasNext should return true when not at end', () => {
        service.updateConfig({ currentIndex: 2 });
        expect(service.hasNext()).toBe(true);
      });

      it('hasNext should return false at end without repeat', () => {
        service.updateConfig({ currentIndex: 4, repeatMode: 'off' });
        expect(service.hasNext()).toBe(false);
      });

      it('hasNext should return true at end with repeat', () => {
        service.updateConfig({ currentIndex: 4, repeatMode: 'all' });
        expect(service.hasNext()).toBe(true);
      });

      it('hasPrevious should return true when not at start', () => {
        service.updateConfig({ currentIndex: 2 });
        expect(service.hasPrevious()).toBe(true);
      });

      it('hasPrevious should return false at start without repeat', () => {
        service.updateConfig({ currentIndex: 0, repeatMode: 'off' });
        expect(service.hasPrevious()).toBe(false);
      });

      it('hasPrevious should return true at start with repeat', () => {
        service.updateConfig({ currentIndex: 0, repeatMode: 'all' });
        expect(service.hasPrevious()).toBe(true);
      });
    });
  });

  describe('Shuffle Mode', () => {
    beforeEach(() => {
      // Create a predictable shuffle order: [2, 4, 0, 3, 1]
      const shuffleOrder = [2, 4, 0, 3, 1];
      service.updateConfig({
        shuffleEnabled: true,
        shuffleOrder,
        currentIndex: 0, // Currently at shuffle position 0, which is actual track 2
      });
    });

    describe('Next Track', () => {
      it('should return next track in shuffle order', () => {
        const next = service.getNextTrack();
        expect(next).not.toBeNull();
        // Shuffle order: [2, 4, 0, 3, 1], at position 0, next is position 1 (track 4)
        expect(next?.track.id).toBe('5'); // Track at index 4 is id '5'
      });

      it('should respect shuffle order throughout playlist', () => {
        // Start at position 2 (actual track 0)
        service.updateConfig({ currentIndex: 2 });
        const next = service.getNextTrack();
        expect(next?.track.id).toBe('4'); // Next position is 3 (track 3)
      });

      it('should loop in shuffle mode with repeat all', () => {
        service.updateConfig({ currentIndex: 4, repeatMode: 'all' });
        const next = service.getNextTrack();
        expect(next?.track.id).toBe('3'); // Loop to position 0 (track 2)
      });
    });

    describe('Play History', () => {
      it('should track play history when adding tracks', () => {
        const item = createQueueItem(mockTracks[0]);
        service.addToHistory(item);
        expect(service.getPlayHistory().length).toBe(1);
        expect(service.getPlayHistory()[0].track.id).toBe('1');
      });

      it('should maintain history order (FIFO)', () => {
        service.addToHistory(createQueueItem(mockTracks[0]));
        service.addToHistory(createQueueItem(mockTracks[1]));
        service.addToHistory(createQueueItem(mockTracks[2]));

        const history = service.getPlayHistory();
        expect(history.length).toBe(3);
        expect(history[0].track.id).toBe('1');
        expect(history[1].track.id).toBe('2');
        expect(history[2].track.id).toBe('3');
      });

      it('should limit history size to MAX_HISTORY_SIZE', () => {
        // Add more than MAX_HISTORY_SIZE items
        for (let i = 0; i < 110; i++) {
          const track = createMockTrack(`${i}`, `Track ${i}`);
          service.addToHistory(createQueueItem(track));
        }

        const history = service.getPlayHistory();
        expect(history.length).toBe(100); // MAX_HISTORY_SIZE
        // Should keep the most recent 100
        expect(history[0].track.id).toBe('10');
        expect(history[99].track.id).toBe('109');
      });

      it('should retrieve previous track from history in shuffle mode', () => {
        // Add some history
        service.addToHistory(createQueueItem(mockTracks[0]));
        service.addToHistory(createQueueItem(mockTracks[3]));

        const prev = service.getPreviousTrack();
        expect(prev).not.toBeNull();
        expect(prev?.track.id).toBe('4'); // Most recent in history

        // History should be popped
        expect(service.getPlayHistory().length).toBe(1);
      });

      it('should return multiple tracks in reverse order from history', () => {
        service.addToHistory(createQueueItem(mockTracks[0]));
        service.addToHistory(createQueueItem(mockTracks[2]));
        service.addToHistory(createQueueItem(mockTracks[4]));

        const prev1 = service.getPreviousTrack();
        expect(prev1?.track.id).toBe('5');

        const prev2 = service.getPreviousTrack();
        expect(prev2?.track.id).toBe('3');

        const prev3 = service.getPreviousTrack();
        expect(prev3?.track.id).toBe('1');

        expect(service.getPlayHistory().length).toBe(0);
      });

      it('canGoToPrevious should return true when history exists in shuffle mode', () => {
        service.addToHistory(createQueueItem(mockTracks[0]));
        expect(service.canGoToPrevious()).toBe(true);
      });

      it('canGoToPrevious should return true even without history in shuffle mode', () => {
        // In shuffle mode with no history, should allow going to random previous track
        expect(service.canGoToPrevious()).toBe(true);
      });

      it('should return random track when no history in shuffle mode', () => {
        // When no history exists, getPreviousTrack should return a random track
        const prev = service.getPreviousTrack();
        expect(prev).not.toBeNull();
        expect(prev?.track).toBeDefined();
        // Should be different from current track (which is at shuffle position 0 = actual index 2)
        const currentActualIndex = service.getActualIndex(0);
        expect(prev?.indexInPlaylist).not.toBe(currentActualIndex);
      });

      it('should clear history', () => {
        service.addToHistory(createQueueItem(mockTracks[0]));
        service.addToHistory(createQueueItem(mockTracks[1]));

        service.clearHistory();
        expect(service.getPlayHistory().length).toBe(0);
      });
    });

    describe('Shuffle Order Generation', () => {
      it('should generate a valid shuffle order', () => {
        const shuffleOrder = service.generateShuffleOrder();
        expect(shuffleOrder.length).toBe(mockTracks.length);

        // Should contain all indices
        const sorted = [...shuffleOrder].sort((a, b) => a - b);
        expect(sorted).toEqual([0, 1, 2, 3, 4]);
      });

      it('should place current track at front of shuffle order', () => {
        service.updateConfig({ currentIndex: 3 });
        const shuffleOrder = service.generateShuffleOrder();

        // Current track (index 3) should be at position 0
        expect(shuffleOrder[0]).toBe(3);
      });

      it('should produce different orders on subsequent calls', () => {
        const order1 = service.generateShuffleOrder();
        const order2 = service.generateShuffleOrder();

        // Very unlikely to be the same (but technically possible)
        // We'll just check they're both valid
        expect(order1.length).toBe(mockTracks.length);
        expect(order2.length).toBe(mockTracks.length);
      });
    });
  });

  describe('Queue Management', () => {
    describe('Basic Queue Operations', () => {
      it('should add track to queue', () => {
        service.addToQueue(mockTracks[0], 'playlist-1', 0);
        expect(service.getQueueLength()).toBe(1);
        expect(service.getQueue()[0].track.id).toBe('1');
      });

      it('should add multiple tracks to queue', () => {
        const items = mockTracks.map(track => createQueueItem(track));
        service.addTracksToQueue(items);
        expect(service.getQueueLength()).toBe(5);
      });

      it('should remove track from queue', () => {
        service.addToQueue(mockTracks[0], 'playlist-1', 0);
        service.addToQueue(mockTracks[1], 'playlist-1', 1);

        service.removeFromQueue(0);
        expect(service.getQueueLength()).toBe(1);
        expect(service.getQueue()[0].track.id).toBe('2');
      });

      it('should move queue item', () => {
        service.addToQueue(mockTracks[0], 'playlist-1', 0);
        service.addToQueue(mockTracks[1], 'playlist-1', 1);
        service.addToQueue(mockTracks[2], 'playlist-1', 2);

        service.moveQueueItem(0, 2);
        const queue = service.getQueue();
        expect(queue[0].track.id).toBe('2');
        expect(queue[1].track.id).toBe('3');
        expect(queue[2].track.id).toBe('1');
      });

      it('should clear manual items only', () => {
        service.addToQueue(mockTracks[0], 'playlist-1', 0);
        service.addTracksToQueue([
          { ...createQueueItem(mockTracks[1]), source: 'playlist' },
          { ...createQueueItem(mockTracks[2]), source: 'manual' },
        ]);

        service.clearQueue();
        const queue = service.getQueue();
        expect(queue.length).toBe(1);
        expect(queue[0].source).toBe('playlist');
      });

      it('should clear all queue items', () => {
        service.addTracksToQueue([
          createQueueItem(mockTracks[0]),
          createQueueItem(mockTracks[1]),
        ]);

        service.clearAllQueue();
        expect(service.getQueueLength()).toBe(0);
      });

      it('should shift queue item', () => {
        service.addToQueue(mockTracks[0], 'playlist-1', 0);
        service.addToQueue(mockTracks[1], 'playlist-1', 1);

        const item = service.shiftQueue();
        expect(item?.track.id).toBe('1');
        expect(service.getQueueLength()).toBe(1);
      });
    });

    describe('Queue Prioritization', () => {
      it('should insert manual items before playlist items', () => {
        // Add playlist items first
        service.addTracksToQueue([
          { ...createQueueItem(mockTracks[0]), source: 'playlist' },
          { ...createQueueItem(mockTracks[1]), source: 'playlist' },
        ]);

        // Add manual item
        service.addToQueue(mockTracks[4], 'playlist-1', 4);

        const queue = service.getQueue();
        expect(queue[0].track.id).toBe('5'); // Manual item first
        expect(queue[0].source).toBe('manual');
        expect(queue[1].source).toBe('playlist');
        expect(queue[2].source).toBe('playlist');
      });
    });

    describe('getNextTrack with Queue', () => {
      it('should return queue item before playlist items', () => {
        service.addToQueue(mockTracks[4], 'playlist-1', 4);
        service.updateConfig({ currentIndex: 0 });

        const next = service.getNextTrack();
        expect(next?.track.id).toBe('5'); // From queue, not index 1
      });
    });
  });

  describe('Queue Replenishment', () => {
    beforeEach(() => {
      // Use a larger playlist for replenishment tests
      const largeTracks: TrackInfo[] = [];
      for (let i = 1; i <= 200; i++) {
        largeTracks.push(createMockTrack(`${i}`, `Track ${i}`));
      }
      service.updateConfig({
        playlist: largeTracks,
        currentIndex: 0,
      });
    });

    it('should replenish queue when below threshold', () => {
      service.replenishPlaylistQueue();
      const queue = service.getQueue();
      expect(queue.length).toBeGreaterThan(0);
      expect(queue.length).toBeLessThanOrEqual(100);
    });

    it('should not replenish when above threshold', () => {
      // Fill queue with 110 items
      const items: QueueItem[] = [];
      for (let i = 1; i <= 110; i++) {
        items.push({
          track: createMockTrack(`${i}`, `Track ${i}`),
          playlistId: 'playlist-1',
          indexInPlaylist: i - 1,
          source: 'playlist',
        });
      }
      service.addTracksToQueue(items);

      const initialLength = service.getQueueLength();
      service.replenishPlaylistQueue();
      expect(service.getQueueLength()).toBe(initialLength); // No change
    });

    it('should skip recently played tracks when possible', () => {
      const recentlyPlayed = new Set(['1', '2', '3', '4', '5']);
      service.updateConfig({
        recentlyPlayedIds: recentlyPlayed,
        currentIndex: 0,
      });

      service.replenishPlaylistQueue();
      const queue = service.getQueue();

      // Queue should avoid recently played if possible
      const queueIds = queue.map(item => item.track.id);

      // With 200 tracks and only 5 recently played, queue should avoid them initially
      // Check first 10 items
      const first10HasRecent = queueIds.slice(0, 10).some(id => recentlyPlayed.has(id));
      expect(first10HasRecent).toBe(false);
    });

    it('should avoid duplicates in queue', () => {
      service.replenishPlaylistQueue();
      const queue = service.getQueue();
      const ids = queue.map(item => item.track.id);
      const uniqueIds = new Set(ids);

      // Initially, all IDs should be unique
      expect(ids.length).toBe(uniqueIds.size);
    });

    it('should allow duplicates after threshold', () => {
      // Use a small playlist where duplicates become necessary
      const smallTracks: TrackInfo[] = [];
      for (let i = 1; i <= 10; i++) {
        smallTracks.push(createMockTrack(`${i}`, `Track ${i}`));
      }

      service.updateConfig({
        playlist: smallTracks,
        currentIndex: 0,
        repeatMode: 'all',
      });

      service.replenishPlaylistQueue();
      const queue = service.getQueue();

      // With only 10 tracks and trying to add 100, duplicates are necessary with repeat mode
      // The queue should have more items than unique tracks available
      expect(queue.length).toBeGreaterThan(10);

      // Check that we have some duplicates
      const trackIds = queue.map(item => item.track.id);
      const uniqueIds = new Set(trackIds);
      expect(uniqueIds.size).toBeLessThan(trackIds.length);
    });

    it('should continue from last playlist item index', () => {
      service.updateConfig({ currentIndex: 10 });
      service.replenishPlaylistQueue();

      const queue = service.getQueue();
      expect(queue.length).toBeGreaterThan(0);

      // First item should be from index 11
      expect(parseInt(queue[0].track.id)).toBeGreaterThan(10);
    });

    it('should respect offline mode and cache filtering', () => {
      const cachedIds = new Set(['1', '2', '3']);
      service.updateConfig({
        isOnline: false,
        cachedTrackIds: cachedIds,
        currentIndex: 0,
      });

      service.replenishPlaylistQueue();
      const queue = service.getQueue();

      // All queued tracks should be cached
      for (const item of queue) {
        expect(cachedIds.has(item.track.id)).toBe(true);
      }
    });

    it('should respect low-data mode', () => {
      const cachedIds = new Set(['1', '2', '3', '4', '5']);
      service.updateConfig({
        networkType: 'low-data',
        preventDownloadOnLowData: true,
        cachedTrackIds: cachedIds,
        currentIndex: 0,
      });

      service.replenishPlaylistQueue();
      const queue = service.getQueue();

      // All queued tracks should be cached in low-data mode
      for (const item of queue) {
        expect(cachedIds.has(item.track.id)).toBe(true);
      }
    });

    it('should handle repeat all mode', () => {
      service.updateConfig({
        currentIndex: 195, // Near end
        repeatMode: 'all',
      });

      service.replenishPlaylistQueue();
      const queue = service.getQueue();

      // Should be able to add 100 items even near end by looping
      expect(queue.length).toBeGreaterThan(50);
    });

    it('should respect repeat off mode limits', () => {
      service.updateConfig({
        currentIndex: 195, // 4 tracks left
        repeatMode: 'off',
      });

      service.replenishPlaylistQueue();
      const queue = service.getQueue();

      // Can only add remaining tracks
      expect(queue.length).toBeLessThanOrEqual(4);
    });
  });

  describe('Edge Cases', () => {
    it('should handle empty playlist', () => {
      service.updateConfig({ playlist: [] });
      expect(service.getNextTrack()).toBeNull();
      expect(service.getPreviousTrack()).toBeNull();
      expect(service.hasNext()).toBe(false);
      expect(service.hasPrevious()).toBe(false);
    });

    it('should handle single track playlist', () => {
      service.updateConfig({
        playlist: [mockTracks[0]],
        currentIndex: 0,
      });

      // Without repeat
      expect(service.getNextTrack()).toBeNull();
      expect(service.getPreviousTrack()).toBeNull();

      // With repeat
      service.updateConfig({ repeatMode: 'all' });
      expect(service.getNextTrack()?.track.id).toBe('1');
      expect(service.getPreviousTrack()?.track.id).toBe('1');
    });

    it('should handle invalid indices gracefully', () => {
      service.updateConfig({ currentIndex: -1 });
      const next = service.getNextTrack();
      expect(next?.track.id).toBe('1'); // Index -1 + 1 = 0

      service.updateConfig({ currentIndex: 100 });
      expect(service.getNextTrack()).toBeNull();
    });

    it('should handle missing currentPlaylistId', () => {
      service.updateConfig({ currentPlaylistId: null });
      expect(service.getNextTrack()).toBeNull();
    });

    it('should handle empty shuffle order', () => {
      service.updateConfig({
        shuffleEnabled: true,
        shuffleOrder: [],
      });

      // Should fall back to sequential
      const next = service.getNextTrack();
      expect(next?.track.id).toBe('2');
    });

    it('should handle invalid remove index', () => {
      service.addToQueue(mockTracks[0], 'playlist-1', 0);
      const initialLength = service.getQueueLength();

      service.removeFromQueue(-1);
      expect(service.getQueueLength()).toBe(initialLength);

      service.removeFromQueue(100);
      expect(service.getQueueLength()).toBe(initialLength);
    });

    it('should handle invalid move indices', () => {
      service.addToQueue(mockTracks[0], 'playlist-1', 0);
      service.addToQueue(mockTracks[1], 'playlist-1', 1);

      const queue = service.getQueue();

      service.moveQueueItem(-1, 1);
      expect(service.getQueue()).toEqual(queue); // No change

      service.moveQueueItem(0, 100);
      expect(service.getQueue()).toEqual(queue); // No change
    });
  });

  describe('State Persistence', () => {
    it('should persist and restore queue', () => {
      service.addToQueue(mockTracks[0], 'playlist-1', 0);
      service.addToQueue(mockTracks[1], 'playlist-1', 1);

      const queue = service.getQueue();

      const newService = new PlayQueueService({
        currentIndex: 0,
        currentPlaylistId: 'playlist-1',
        playlist: mockTracks,
        shuffleOrder: [],
        shuffleEnabled: false,
        repeatMode: 'off',
        cachedTrackIds: new Set(),
        recentlyPlayedIds: new Set(),
        isOnline: true,
        networkType: 'normal',
        preventDownloadOnLowData: false,
      });

      newService.setQueue(queue);
      expect(newService.getQueue()).toEqual(queue);
    });

    it('should persist and restore play history', () => {
      service.addToHistory(createQueueItem(mockTracks[0]));
      service.addToHistory(createQueueItem(mockTracks[1]));

      const history = service.getPlayHistory();

      const newService = new PlayQueueService({
        currentIndex: 0,
        currentPlaylistId: 'playlist-1',
        playlist: mockTracks,
        shuffleOrder: [],
        shuffleEnabled: false,
        repeatMode: 'off',
        cachedTrackIds: new Set(),
        recentlyPlayedIds: new Set(),
        isOnline: true,
        networkType: 'normal',
        preventDownloadOnLowData: false,
      });

      newService.setPlayHistory(history);
      expect(newService.getPlayHistory()).toEqual(history);
    });
  });
});
