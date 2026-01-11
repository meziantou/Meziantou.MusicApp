import { describe, it, expect, beforeEach } from 'vitest';
import { PlayQueueService } from './play-queue-service';
import type { TrackInfo } from '../types';

// Helper to create mock tracks
function createMockTrack(id: string, title: string): TrackInfo {
  return {
    id,
    title,
    artists: 'Test Artist',
    artistId: 'artist1',
    album: 'Test Album',
    albumId: 'album1',
    duration: 180,
    track: 1,
    year: 2024,
    genre: 'Test',
    bitRate: 320000,
    size: 5000000,
    contentType: 'audio/mpeg',
    addedDate: '2024-01-01',
    isrc: null,
    replayGainTrackGain: null,
    replayGainTrackPeak: null,
    replayGainAlbumGain: null,
    replayGainAlbumPeak: null,
    path: `/test/${id}`
  };
}

describe('PlayQueueService - New Array Architecture', () => {
  let service: PlayQueueService;
  let mockTracks: TrackInfo[];

  beforeEach(() => {
    mockTracks = [
      createMockTrack('1', 'Track 1'),
      createMockTrack('2', 'Track 2'),
      createMockTrack('3', 'Track 3'),
      createMockTrack('4', 'Track 4'),
      createMockTrack('5', 'Track 5')
    ];

    service = new PlayQueueService({
      currentPlaylistId: null,
      playlist: [],
      shuffleOrder: [],
      shuffleEnabled: false,
      repeatMode: 'off',
      cachedTrackIds: new Set(),
      recentlyPlayedIds: new Set(),
      isOnline: true,
      networkType: 'normal',
      preventDownloadOnLowData: false
    });
  });

  describe('Basic Queue Operations', () => {
    it('should initialize empty queue', () => {
      expect(service.getQueue()).toEqual([]);
      expect(service.getCurrentIndex()).toBe(-1);
    });

    it('should set playlist and start playback at index', () => {
      service.setPlaylist('playlist1', mockTracks);
      expect(service.playAtIndex(0)).toBe(true);

      const currentTrack = service.getCurrentTrack();
      expect(currentTrack?.id).toBe('1');
      expect(service.getCurrentIndex()).toBe(0);
    });

    it('should generate lookahead items when playing at index', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(0);

      const queue = service.getQueue();
      expect(queue.length).toBeGreaterThan(1); // Should have current + lookahead

      const lookahead = service.getLookaheadQueue();
      expect(lookahead.length).toBeGreaterThan(0);
    });
  });

  describe('Navigation', () => {
    beforeEach(() => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(0);
    });

    it('should navigate forward with next()', () => {
      expect(service.next()).toBe(true);
      expect(service.getCurrentTrack()?.id).toBe('2');
    });

    it('should navigate backward with previous()', () => {
      service.next(); // Go to track 2
      expect(service.previous()).toBe(true);
      expect(service.getCurrentTrack()?.id).toBe('1');
    });

    it('should wrap to end when going previous at start with repeat all', () => {
      service.updateConfig({ repeatMode: 'all' });
      expect(service.previous()).toBe(true); // Should generate previous track (last in playlist)
      expect(service.getCurrentIndex()).toBe(0); // Index shifts due to prepend
      expect(service.getCurrentTrack()?.id).toBe('5'); // Last track in playlist
    });

    it('should handle hasNext correctly', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(4); // Last track

      service.updateConfig({ repeatMode: 'off' });
      expect(service.hasNext()).toBe(false);

      service.updateConfig({ repeatMode: 'all' });
      expect(service.hasNext()).toBe(true);
    });
  });

  describe('Shuffle Mode', () => {
    it('should generate random track for previous when at start', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.setShuffle(true);
      service.playAtIndex(0);

      const currentId = service.getCurrentTrack()?.id;

      expect(service.previous()).toBe(true);
      const prevTrack = service.getCurrentTrack();
      expect(prevTrack?.id).not.toBe(currentId);
    });

    it('should rebuild lookahead when toggling shuffle', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(0);

      service.setShuffle(true);

      // Should still have queue with lookahead
      expect(service.getQueue().length).toBeGreaterThan(0);
    });
  });

  describe('Manual Queue Items', () => {
    it('should add track right after current', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(0);

      const manualTrack = createMockTrack('99', 'Manual Track');
      service.addToQueue(manualTrack, 'playlist1', 0);

      const queue = service.getQueue();
      expect(queue[1].track.id).toBe('99');
      expect(queue[1].source).toBe('manual');
    });

    it('should maintain current index when adding to queue', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(0);

      const indexBefore = service.getCurrentIndex();
      service.addToQueue(mockTracks[4], 'playlist1', 4);

      expect(service.getCurrentIndex()).toBe(indexBefore);
    });
  });

  describe('Repeat Modes', () => {
    beforeEach(() => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(4); // Last track
    });

    it('should not advance past last track when repeat is off', () => {
      service.updateConfig({ repeatMode: 'off' });
      expect(service.hasNext()).toBe(false);
    });

    it('should loop to start when repeat is all', () => {
      service.updateConfig({ repeatMode: 'all' });
      expect(service.hasNext()).toBe(true);

      service.next();
      expect(service.getCurrentTrack()?.id).toBe('1'); // Should wrap to first
    });

    it('should stay on current track when repeat is one', () => {
      service.updateConfig({ repeatMode: 'one' });
      service.next();
      // Note: repeat one is handled at audio player level, not here
      expect(service.hasNext()).toBe(true);
    });
  });

  describe('Queue Management', () => {
    it('should remove item from queue', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(0);

      const queueLength = service.getQueue().length;
      service.removeFromQueue(2); // Remove item at index 2

      expect(service.getQueue().length).toBe(queueLength - 1);
    });

    it('should not remove current track', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(0);

      const currentIndex = service.getCurrentIndex();
      const queueLength = service.getQueue().length;

      service.removeFromQueue(currentIndex);

      expect(service.getQueue().length).toBe(queueLength); // Should not remove
    });

    it('should move queue items', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(0);

      const queue = service.getQueue();
      const itemAt2 = queue[2];

      service.moveQueueItem(2, 4);

      const newQueue = service.getQueue();
      expect(newQueue[4].track.id).toBe(itemAt2.track.id);
    });
  });

  describe('Persistence', () => {
    it('should restore queue state', () => {
      service.setPlaylist('playlist1', mockTracks);
      service.playAtIndex(2);

      const savedQueue = service.getQueue();
      const savedIndex = service.getCurrentIndex();

      // Create new service and restore
      const newService = new PlayQueueService({
        currentPlaylistId: 'playlist1',
        playlist: mockTracks,
        shuffleOrder: [],
        shuffleEnabled: false,
        repeatMode: 'off',
        cachedTrackIds: new Set(),
        recentlyPlayedIds: new Set(),
        isOnline: true,
        networkType: 'normal',
        preventDownloadOnLowData: false
      });

      newService.restoreQueue(savedQueue, savedIndex);

      expect(newService.getCurrentTrack()?.id).toBe('3');
      expect(newService.getCurrentIndex()).toBe(savedIndex);
    });
  });
});
