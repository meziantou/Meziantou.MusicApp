import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AudioPlayerService } from './audio-player';
import { TrackInfo } from '../types';

// Mock dependencies
vi.mock('./api-service', () => ({
  getApiService: vi.fn(() => ({
    getSongStreamUrl: vi.fn().mockReturnValue('http://mock-url'),
    getAuthHeaders: vi.fn().mockReturnValue({}),
    getSongCoverUrl: vi.fn().mockReturnValue('http://mock-cover-url'),
  })),
}));

vi.mock('./storage-service', () => ({
  storageService: {
    getCachedTrack: vi.fn().mockResolvedValue(null),
    savePlaybackState: vi.fn(),
    saveSettings: vi.fn(),
    init: vi.fn(),
    addRecentlyPlayed: vi.fn().mockResolvedValue(undefined),
    getRecentlyPlayedIds: vi.fn().mockResolvedValue(new Set()),
    cleanupOldRecentlyPlayed: vi.fn().mockResolvedValue(undefined),
  },
}));

describe('AudioPlayerService Queue Logic', () => {
  let player: AudioPlayerService;
  const mockTracks: TrackInfo[] = [
    { id: '1', title: 'Track 1', path: '/music/track1.mp3', artists: 'Artist 1', album: 'Album 1', duration: 100, artistId: null, albumId: null, track: 1, year: 2020, genre: 'Pop', bitRate: 320, size: 1000, contentType: 'audio/mp3', addedDate: null, isrc: null, replayGainTrackGain: null, replayGainTrackPeak: null, replayGainAlbumGain: null, replayGainAlbumPeak: null },
    { id: '2', title: 'Track 2', path: '/music/track2.mp3', artists: 'Artist 2', album: 'Album 1', duration: 100, artistId: null, albumId: null, track: 2, year: 2020, genre: 'Pop', bitRate: 320, size: 1000, contentType: 'audio/mp3', addedDate: null, isrc: null, replayGainTrackGain: null, replayGainTrackPeak: null, replayGainAlbumGain: null, replayGainAlbumPeak: null },
    { id: '3', title: 'Track 3', path: '/music/track3.mp3', artists: 'Artist 3', album: 'Album 1', duration: 100, artistId: null, albumId: null, track: 3, year: 2020, genre: 'Pop', bitRate: 320, size: 1000, contentType: 'audio/mp3', addedDate: null, isrc: null, replayGainTrackGain: null, replayGainTrackPeak: null, replayGainAlbumGain: null, replayGainAlbumPeak: null },
    { id: '4', title: 'Track 4', path: '/music/track4.mp3', artists: 'Artist 4', album: 'Album 1', duration: 100, artistId: null, albumId: null, track: 4, year: 2020, genre: 'Pop', bitRate: 320, size: 1000, contentType: 'audio/mp3', addedDate: null, isrc: null, replayGainTrackGain: null, replayGainTrackPeak: null, replayGainAlbumGain: null, replayGainAlbumPeak: null },
    { id: '5', title: 'Track 5', path: '/music/track5.mp3', artists: 'Artist 5', album: 'Album 1', duration: 100, artistId: null, albumId: null, track: 5, year: 2020, genre: 'Pop', bitRate: 320, size: 1000, contentType: 'audio/mp3', addedDate: null, isrc: null, replayGainTrackGain: null, replayGainTrackPeak: null, replayGainAlbumGain: null, replayGainAlbumPeak: null },
  ];

  beforeEach(() => {
    vi.clearAllMocks();
    player = new AudioPlayerService();
    // Setup initial state
    player.setPlaylist('playlist-1', mockTracks);
  });

  describe('Next Track', () => {
    it('should play next track in sequence when repeat is off', async () => {
      await player.playAtIndex(0, false);
      expect(player.getCurrentIndex()).toBe(0);

      await player.next();
      expect(player.getCurrentIndex()).toBe(1);
      expect(player.getCurrentTrack()?.id).toBe('2');

      await player.next();
      expect(player.getCurrentIndex()).toBe(2);
      expect(player.getCurrentTrack()?.id).toBe('3');
    });

    it('should stop at the end of playlist when repeat is off', async () => {
      await player.playAtIndex(4, false); // Last track
      expect(player.getCurrentIndex()).toBe(4);

      await player.next();
      // Should stay on last track or not play anything new?
      // Implementation of next() returns if !hasNext().
      // hasNext() returns false if index is last and repeat is off.
      expect(player.getCurrentIndex()).toBe(4);
      expect(player.getCurrentTrack()?.id).toBe('5');
    });

    it('should loop to start when repeat is all', async () => {
      player.setRepeatMode('all');
      await player.playAtIndex(4, false); // Last track

      await player.next();
      expect(player.getCurrentIndex()).toBe(0);
      expect(player.getCurrentTrack()?.id).toBe('1');
    });

    it('should go to next track even when repeat is one', async () => {
      // "Next" button usually forces advancement
      player.setRepeatMode('one');
      await player.playAtIndex(0, false);

      await player.next();
      expect(player.getCurrentIndex()).toBe(1);
      expect(player.getCurrentTrack()?.id).toBe('2');
    });
  });

  describe('Previous Track', () => {
    it('should go to previous track', async () => {
      await player.playAtIndex(1, false);

      await player.previous();
      expect(player.getCurrentIndex()).toBe(0);
      expect(player.getCurrentTrack()?.id).toBe('1');
    });

    it('should loop to end when repeat is all and at start', async () => {
      player.setRepeatMode('all');
      await player.playAtIndex(0, false);

      await player.previous();
      expect(player.getCurrentIndex()).toBe(4);
      expect(player.getCurrentTrack()?.id).toBe('5');
    });
  });

  describe('Shuffle Mode', () => {
    it('should play tracks in random order', async () => {
      player.setShuffle(true);
      await player.playAtIndex(0, false);

      // We can't predict the exact order, but we can check that it follows the shuffleOrder
      const state = player.getPlaybackState();
      const shuffleOrder = state.shuffleOrder;

      expect(shuffleOrder).toHaveLength(mockTracks.length);
      expect(state.currentTrackIndex).toBe(0); // In shuffle mode, currentIndex is index in shuffleOrder

      // The actual track played should be playlist[shuffleOrder[0]]
      const expectedTrackIndex = shuffleOrder[0];
      const expectedTrack = mockTracks[expectedTrackIndex];
      expect(player.getCurrentTrack()?.id).toBe(expectedTrack.id);

      // Now next() should play shuffleOrder[currentIndex + 1]
      await player.next();

      const nextIndex = player.getCurrentIndex();
      expect(nextIndex).toBe(1);

      // Verify it's not just linear (though it could be by chance)
      // We can check if the track played matches the shuffle order
      const expectedNextTrackIndex = shuffleOrder[nextIndex];
      const expectedNextTrack = mockTracks[expectedNextTrackIndex];
      expect(player.getCurrentTrack()?.id).toBe(expectedNextTrack.id);
    });
  });

  describe('Queue Removal', () => {
    it('should not restore manually removed items when advancing to next track', async () => {
      // Start playing from first track
      await player.playAtIndex(0, false);
      expect(player.getCurrentIndex()).toBe(0);

      // Get the queue and verify it has tracks
      let queue = player.getQueue();
      const initialQueueLength = queue.length;
      expect(initialQueueLength).toBeGreaterThan(0);

      // Find Track 2 in the queue (index 1)
      const track2Index = queue.findIndex(item => item.track.id === '2');
      expect(track2Index).toBeGreaterThanOrEqual(0);

      // Remove Track 2 from the queue
      player.removeFromQueue(track2Index);

      // Verify Track 2 is removed
      queue = player.getQueue();
      expect(queue.find(item => item.track.id === '2')).toBeUndefined();

      // Advance to next track (should skip removed track 2 and go to track 3)
      await player.next();
      expect(player.getCurrentIndex()).toBe(2); // Track 3 is at playlist index 2
      expect(player.getCurrentTrack()?.id).toBe('3');

      // Verify Track 2 is still not in the queue after advancing
      queue = player.getQueue();
      expect(queue.find(item => item.track.id === '2')).toBeUndefined();
    });

    it('should clear removed items tracking when changing playlist', async () => {
      // Start playing from first track
      await player.playAtIndex(0, false);

      // Get the queue and remove Track 2
      let queue = player.getQueue();
      const track2Index = queue.findIndex(item => item.track.id === '2');
      if (track2Index >= 0) {
        player.removeFromQueue(track2Index);
      }

      // Verify Track 2 is removed
      queue = player.getQueue();
      expect(queue.find(item => item.track.id === '2')).toBeUndefined();

      // Change to a new playlist (same tracks)
      player.setPlaylist('playlist-2', mockTracks);

      // Start playing again
      await player.playAtIndex(0, false);

      // Track 2 should now be in the queue again since we changed playlists
      queue = player.getQueue();
      expect(queue.find(item => item.track.id === '2')).toBeDefined();
    });
  });

  describe('Play Track by ID', () => {
    it('should play the correct track in shuffle mode', async () => {
      player.setShuffle(true);

      // When double-clicking tracks in the UI while in shuffle mode,
      // the player should play the exact track clicked, not a random one
      await player.playTrack(mockTracks[4]); // Request Track 5
      expect(player.getCurrentTrack()?.id).toBe('5'); // Should play Track 5

      await player.playTrack(mockTracks[2]); // Request Track 3
      expect(player.getCurrentTrack()?.id).toBe('3'); // Should play Track 3

      await player.playTrack(mockTracks[0]); // Request Track 1
      expect(player.getCurrentTrack()?.id).toBe('1'); // Should play Track 1
    });

    it('should handle double-click scenario in shuffle mode', async () => {
      // Simulate a typical shuffle scenario
      const shuffleOrder = [3, 1, 4, 0, 2]; // Track 4, Track 2, Track 5, Track 1, Track 3
      player.setPlaylist('playlist-1', mockTracks, shuffleOrder);
      player.setShuffle(true);

      // User double-clicks on Track 2 in the UI
      // Track 2 has actual index 1 in the original playlist
      await player.playTrack(mockTracks[1]);

      // Should be playing Track 2, not a random track
      expect(player.getCurrentTrack()?.id).toBe('2');
    });
  });});
