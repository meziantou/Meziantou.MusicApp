import type { TrackInfo, StreamingQuality, ReplayGainMode, PlaybackState, RepeatMode, QueueItem } from '../types';
import { getApiService } from './api-service';
import { storageService } from './storage-service';
import { PlayQueueService } from './play-queue-service';

export type PlayerEventType =
  | 'play'
  | 'pause'
  | 'timeupdate'
  | 'ended'
  | 'trackchange'
  | 'error'
  | 'volumechange'
  | 'durationchange'
  | 'loadstart'
  | 'canplay'
  | 'queuechange';

export interface PlayerEventDetail {
  currentTime?: number;
  duration?: number;
  volume?: number;
  error?: string;
  track?: TrackInfo;
  quality?: StreamingQuality;
}

type PlayerEventCallback = (detail: PlayerEventDetail) => void;

interface AudioInstance {
  audio: HTMLAudioElement;
  gainNode: GainNode | null;
  sourceNode: MediaElementAudioSourceNode | null;
  track: TrackInfo | null;
  quality: StreamingQuality | null;
}

export class AudioPlayerService {
  private audioInstance: AudioInstance;

  private audioContext: AudioContext | null = null;
  private masterGainNode: GainNode | null = null;

  private currentTrack: TrackInfo | null = null;
  private currentQuality: StreamingQuality | null = null;
  private queueService: PlayQueueService;

  private quality: StreamingQuality = { format: 'raw' };
  private replayGainMode: ReplayGainMode = 'off';
  private replayGainPreamp: number = 0;
  private scrobbleEnabled: boolean = true;
  private preventDownloadOnLowData: boolean = false;
  private networkType: 'normal' | 'low-data' | 'unknown' = 'unknown';
  private cachedTrackIds: Set<string> = new Set();
  private isOnline: boolean = true;

  // Preloading state
  private preloadedTrack: TrackInfo | null = null;
  private isPreloading: boolean = false;
  private preloadBlobUrl: string | null = null;
  private preloadAbortController: AbortController | null = null;

  // Volume state
  private masterVolume: number = 1;
  private isMuted: boolean = false;

  private hasScrobbled: boolean = false;
  private hasSentNowPlaying: boolean = false;

  private eventListeners: Map<PlayerEventType, Set<PlayerEventCallback>> = new Map();
  private saveStateDebounced: ReturnType<typeof setTimeout> | null = null;
  private lastSaveTime: number = 0;

  // Recently played tracks for filtering out recently heard songs from the queue
  private recentlyPlayedIds: Set<string> = new Set();
  private static readonly RECENTLY_PLAYED_MAX_COUNT = 300;

  constructor() {
    this.audioInstance = this.createAudioInstance();
    this.setupAudioEvents(this.audioInstance);
    this.setupMediaSession();
    this.queueService = new PlayQueueService({
      currentIndex: -1,
      currentPlaylistId: null,
      playlist: [],
      shuffleOrder: [],
      shuffleEnabled: false,
      repeatMode: 'off',
      cachedTrackIds: this.cachedTrackIds,
      recentlyPlayedIds: this.recentlyPlayedIds,
      isOnline: this.isOnline,
      networkType: this.networkType,
      preventDownloadOnLowData: this.preventDownloadOnLowData
    });
  }

  private createAudioInstance(): AudioInstance {
    const audio = new Audio();
    audio.preload = 'auto';
    return {
      audio,
      gainNode: null,
      sourceNode: null,
      track: null,
      quality: null
    };
  }

  private get audio(): HTMLAudioElement {
    return this.audioInstance.audio;
  }

  private get activeInstance(): AudioInstance {
    return this.audioInstance;
  }

  private async initAudioContext(): Promise<void> {
    if (this.audioContext) return;

    this.audioContext = new AudioContext();
    this.masterGainNode = this.audioContext.createGain();
    this.masterGainNode.connect(this.audioContext.destination);
    // Apply volume respecting the muted state
    this.masterGainNode.gain.value = this.isMuted ? 0 : this.linearToLogarithmic(this.masterVolume);

    // Connect audio element to the audio context
    this.connectAudioInstance(this.audioInstance);

    // Emit volumechange to sync React state with actual audio player state
    // This ensures the UI reflects the correct volume after AudioContext initialization
    this.emit('volumechange', { volume: this.masterVolume });
  }

  private connectAudioInstance(instance: AudioInstance): void {
    if (!this.audioContext || !this.masterGainNode) return;

    instance.gainNode = this.audioContext.createGain();
    instance.sourceNode = this.audioContext.createMediaElementSource(instance.audio);
    instance.sourceNode.connect(instance.gainNode);
    instance.gainNode.connect(this.masterGainNode);
  }

  private setupAudioEvents(instance: AudioInstance): void {
    const audio = instance.audio;

    audio.addEventListener('play', () => {
      this.emit('play', {});
      if (!this.hasSentNowPlaying && this.currentTrack) {
        this.hasSentNowPlaying = true;
        this.handleScrobble(this.currentTrack.id, false).catch(console.error);
      }
    });

    audio.addEventListener('pause', () => {
      this.emit('pause', {});
    });

    audio.addEventListener('timeupdate', () => {
      this.emit('timeupdate', {
        currentTime: audio.currentTime,
        duration: audio.duration
      });
      this.saveStateThrottled();

      if (!this.hasScrobbled && this.currentTrack && audio.duration > 0) {
        const progress = audio.currentTime / audio.duration;
        if (progress > 0.5 || audio.currentTime > 240) {
          this.hasScrobbled = true;
          this.handleScrobble(this.currentTrack.id, true).catch(console.error);
        }
      }

      // Check if we should start preload
      this.checkForPreload();
    });

    audio.addEventListener('ended', () => {
      this.handleTrackEnded();
    });

    audio.addEventListener('error', () => {
      const error = audio.error?.message ?? 'Unknown playback error';
      this.emit('error', { error });
    });

    audio.addEventListener('volumechange', () => {
      this.emit('volumechange', { volume: this.masterVolume });
    });

    audio.addEventListener('durationchange', () => {
      this.emit('durationchange', { duration: audio.duration });
    });

    audio.addEventListener('loadstart', () => {
      this.emit('loadstart', {});
    });

    audio.addEventListener('canplay', () => {
      this.emit('canplay', {});
    });
  }

  private setupMediaSession(): void {
    if (!('mediaSession' in navigator)) return;

    navigator.mediaSession.setActionHandler('play', () => this.play());
    navigator.mediaSession.setActionHandler('pause', () => this.pause());
    navigator.mediaSession.setActionHandler('previoustrack', () => this.previous());
    navigator.mediaSession.setActionHandler('nexttrack', () => this.next());
    navigator.mediaSession.setActionHandler('seekto', (details) => {
      if (details.seekTime !== undefined) {
        this.seek(details.seekTime);
      }
    });
    navigator.mediaSession.setActionHandler('seekbackward', (details) => {
      const skipTime = details.seekOffset ?? 10;
      this.seek(Math.max(0, this.audio.currentTime - skipTime));
    });
    navigator.mediaSession.setActionHandler('seekforward', (details) => {
      const skipTime = details.seekOffset ?? 10;
      this.seek(Math.min(this.audio.duration, this.audio.currentTime + skipTime));
    });
  }

  private updateMediaSession(): void {
    if (!('mediaSession' in navigator) || !this.currentTrack) return;

    const api = getApiService();

    navigator.mediaSession.metadata = new MediaMetadata({
      title: this.currentTrack.title,
      artist: this.currentTrack.artists ?? 'Unknown Artist',
      album: this.currentTrack.album ?? 'Unknown Album',
      artwork: [
        { src: api.getSongCoverUrl(this.currentTrack.id, 96), sizes: '96x96', type: 'image/jpeg' },
        { src: api.getSongCoverUrl(this.currentTrack.id, 128), sizes: '128x128', type: 'image/jpeg' },
        { src: api.getSongCoverUrl(this.currentTrack.id, 192), sizes: '192x192', type: 'image/jpeg' },
        { src: api.getSongCoverUrl(this.currentTrack.id, 256), sizes: '256x256', type: 'image/jpeg' },
        { src: api.getSongCoverUrl(this.currentTrack.id, 384), sizes: '384x384', type: 'image/jpeg' },
        { src: api.getSongCoverUrl(this.currentTrack.id, 512), sizes: '512x512', type: 'image/jpeg' }
      ]
    });

    this.updatePositionState();
  }

  private updatePositionState(): void {
    if (!('mediaSession' in navigator) || !this.audio.duration) return;

    try {
      navigator.mediaSession.setPositionState({
        duration: this.audio.duration,
        playbackRate: this.audio.playbackRate,
        position: this.audio.currentTime
      });
    } catch {
      // Ignore errors from invalid position state
    }
  }

  private applyReplayGain(instance: AudioInstance): void {
    const gainNode = instance.gainNode;
    const track = instance.track;

    if (!gainNode || !track) {
      return;
    }

    let appliedGain = 1;
    let gainDb: number | null = null;

    if (this.replayGainMode !== 'off') {
      if (this.replayGainMode === 'track') {
        gainDb = track.replayGainTrackGain ?? null;
      } else if (this.replayGainMode === 'album') {
        gainDb = track.replayGainAlbumGain ?? track.replayGainTrackGain ?? null;
      }

      if (gainDb !== null && gainDb !== undefined && Number.isFinite(gainDb)) {
        // Convert dB to linear gain: 10^(dB/20)
        const preamp = Number.isFinite(this.replayGainPreamp) ? this.replayGainPreamp : 0;
        const linearGain = Math.pow(10, (gainDb + preamp) / 20);

        if (Number.isFinite(linearGain)) {
          // Prevent clipping by limiting to reasonable values
          appliedGain = Math.min(linearGain, 2);
        }
      }
    }

    gainNode.gain.value = appliedGain;

    console.log(`Playing track: ${track.title} - ${track.artists}`, {
      replayGainMode: this.replayGainMode,
      trackGain: track.replayGainTrackGain,
      albumGain: track.replayGainAlbumGain,
      usedGain: gainDb,
      preamp: this.replayGainPreamp,
      appliedGain: appliedGain
    });
  }

  private async handleScrobble(trackId: string, submission: boolean): Promise<void> {
    if (!this.scrobbleEnabled) return;

    if (this.isOnline) {
      try {
        await getApiService().scrobble(trackId, submission);
      } catch (error) {
        console.error('Scrobble failed, saving for later', error);
        // Only save submissions (actual scrobbles) to pending, not "now playing" notifications
        if (submission) {
          await storageService.addPendingScrobble(trackId, submission);
        }
      }
    } else {
      // Only save submissions (actual scrobbles) to pending when offline, not "now playing" notifications
      if (submission) {
        await storageService.addPendingScrobble(trackId, submission);
      }
    }
  }

  private async processPendingScrobbles(): Promise<void> {
    if (!this.isOnline) return;

    try {
      const pending = await storageService.getPendingScrobbles();
      if (pending.length === 0) return;

      console.log(`Processing ${pending.length} pending scrobbles`);
      const api = getApiService();

      for (const item of pending) {
        try {
          await api.scrobble(item.trackId, item.submission);
          if (item.id !== undefined) {
            await storageService.removePendingScrobble(item.id);
          }
        } catch (error) {
          console.error('Failed to process pending scrobble', error);
          // Stop processing if we hit an error (likely offline again or API issue)
          break;
        }
      }
    } catch (error) {
      console.error('Error processing pending scrobbles', error);
    }
  }

  // Preloading methods

  private checkForPreload(): void {
    if (this.isPreloading || this.preloadedTrack) return;

    const duration = this.audio.duration;

    if (!duration || isNaN(duration)) return;

    this.preloadNextTrack();
  }

  private async preloadNextTrack(): Promise<void> {
    const nextTrackInfo = this.queueService.getNextTrack();
    if (!nextTrackInfo) return;

    const { track } = nextTrackInfo;

    // Don't preload if already preloaded
    if (this.preloadedTrack?.id === track.id) return;

    // Cancel any existing preload request
    if (this.preloadAbortController) {
      this.preloadAbortController.abort();
    }
    this.preloadAbortController = new AbortController();

    this.isPreloading = true;

    try {
      const api = getApiService();
      const cached = await storageService.getCachedTrack(track.id);

      // Clean up old preload URL
      if (this.preloadBlobUrl) {
        URL.revokeObjectURL(this.preloadBlobUrl);
        this.preloadBlobUrl = null;
      }

      if (cached && this.shouldUseCache(cached.quality, this.quality)) {
        this.preloadBlobUrl = URL.createObjectURL(cached.blob);
      } else {
        if (this.networkType === 'low-data' && this.preventDownloadOnLowData) {
          // Skip preloading
          this.isPreloading = false;
          return;
        }

        const url = api.getSongStreamUrl(track.id, this.quality);
        const response = await fetch(url, {
          headers: api.getAuthHeaders(),
          signal: this.preloadAbortController.signal
        });

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }

        const blob = await response.blob();
        this.preloadBlobUrl = URL.createObjectURL(blob);
      }

      this.preloadedTrack = track;
    } catch (error: any) {
      if (error.name !== 'AbortError') {
        console.error('Failed to preload next track:', error);
      }
    } finally {
      this.isPreloading = false;
      this.preloadAbortController = null;
    }
  }

  // Crossfade methods removed

  private shouldUseCache(cachedQuality: StreamingQuality, desiredQuality: StreamingQuality): boolean {
    if (!this.isOnline) return true;
    if (cachedQuality.format === 'raw') return true;
    if (desiredQuality.format === 'raw') return false;
    if (cachedQuality.format !== desiredQuality.format) return false;
    if (desiredQuality.maxBitRate) {
      if (!cachedQuality.maxBitRate) return false;
      if (cachedQuality.maxBitRate < desiredQuality.maxBitRate) return false;
    }
    return true;
  }

  private async loadTrack(track: TrackInfo, autoPlay: boolean = false, startTime: number = 0): Promise<void> {
    // Cancel any ongoing preload
    if (this.preloadAbortController) {
      this.preloadAbortController.abort();
      this.preloadAbortController = null;
    }

    // Clear preloaded state
    this.preloadedTrack = null;
    if (this.preloadBlobUrl) {
      URL.revokeObjectURL(this.preloadBlobUrl);
      this.preloadBlobUrl = null;
    }

    this.currentTrack = track;
    const active = this.activeInstance;
    active.track = track;

    this.hasScrobbled = false;
    this.hasSentNowPlaying = false;

    const api = getApiService();

    // Try to use cached version first
    const cached = await storageService.getCachedTrack(track.id);

    // Clean up old src
    if (active.audio.src) {
      URL.revokeObjectURL(active.audio.src);
    }

    if (cached && this.shouldUseCache(cached.quality, this.quality)) {
      active.audio.src = URL.createObjectURL(cached.blob);
      active.quality = cached.quality;
    } else {
      if (this.networkType === 'low-data' && this.preventDownloadOnLowData) {
        this.emit('error', { error: 'Skipping track: Low data mode prevents download' });
        // Try to play next track if possible, or just stop
        // For now we just return, the UI should handle the error
        return;
      }

      // Stream from server with auth header
      const url = api.getSongStreamUrl(track.id, this.quality);

      // Fetch with auth and create blob URL
      try {
        const response = await fetch(url, {
          headers: api.getAuthHeaders()
        });

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }

        const blob = await response.blob();
        active.audio.src = URL.createObjectURL(blob);
        active.quality = this.quality;
      } catch (error) {
        this.emit('error', { error: `Failed to load track: ${error}` });
        return;
      }
    }

    this.currentQuality = active.quality;

    this.applyReplayGain(active);

    // Reset gain to full for active track
    if (active.gainNode) {
      const targetGain = active.gainNode.gain.value;
      active.gainNode.gain.value = targetGain > 0 ? targetGain : 1;
    }

    // Set start time
    if (startTime > 0) {
      const setTime = () => {
        try {
          // Ensure we don't seek past duration
          const duration = active.audio.duration;
          if (duration && startTime < duration) {
            active.audio.currentTime = startTime;
          } else if (!duration) {
            // If duration is not available yet, try setting it anyway
            active.audio.currentTime = startTime;
          }
        } catch (e) {
          console.warn('Failed to seek to start time', e);
        }
      };

      if (active.audio.readyState >= 1) {
        setTime();
      } else {
        await new Promise<void>((resolve) => {
          const onMetadata = () => {
            setTime();
            resolve();
          };
          active.audio.addEventListener('loadedmetadata', onMetadata, { once: true });
        });
      }
    }

    this.updateMediaSession();
    this.emit('trackchange', { track, quality: this.currentQuality ?? undefined });
    this.saveState();

    // Record the track as recently played
    this.recordRecentlyPlayed(track.id);

    if (autoPlay) {
      try {
        await this.play();
      } catch (error) {
        console.warn('Autoplay failed:', error);
        this.emit('pause', {});
      }
    }
  }

  private async handleTrackEnded(): Promise<void> {
    this.emit('ended', {});

    if (this.queueService.getRepeatMode() === 'one') {
      this.audio.currentTime = 0;
      this.play();
      return;
    }

    // Always play from queue - it should contain upcoming tracks
    if (this.queueService.getQueueLength() > 0) {
      await this.playFromQueue();
      return;
    }

    // Queue is empty and no repeat - stop playback
    // This shouldn't normally happen as queue should be populated
  }

  private async playFromQueue(): Promise<void> {
    if (this.queueService.getQueueLength() === 0) return;

    const queueItem = this.queueService.shiftQueue()!;

    // Update current index if this is a playlist track from the current playlist
    this.queueService.updateIndexFromQueueItem(queueItem);

    this.emit('queuechange', {});

    // Replenish queue if needed (triggers at <100 items, adds 100 items to reach ~200)
    const playlistItemsCount = this.queueService.getQueue().filter(i => i.source === 'playlist').length;
    if (this.queueService.getRepeatMode() !== 'off' || playlistItemsCount < 10) {
      this.queueService.updateConfig({
        cachedTrackIds: this.cachedTrackIds,
        recentlyPlayedIds: this.recentlyPlayedIds,
        isOnline: this.isOnline,
        networkType: this.networkType,
        preventDownloadOnLowData: this.preventDownloadOnLowData
      });
      this.queueService.replenishPlaylistQueue();
    } else {
      this.scheduleStateSave();
    }

    await this.loadTrack(queueItem.track, true);
  }

  private getActualIndex(index: number): number {
    return this.queueService.getActualIndex(index);
  }

  private scheduleStateSave(): void {
    if (this.saveStateDebounced) {
      clearTimeout(this.saveStateDebounced);
    }
    this.saveStateDebounced = setTimeout(() => this.saveState(), 1000);
  }

  private saveStateThrottled(): void {
    const now = Date.now();
    if (now - this.lastSaveTime >= 1000) {
      this.saveState();
    }
  }

  private async saveState(): Promise<void> {
    this.lastSaveTime = Date.now();
    const state: PlaybackState = {
      currentPlaylistId: this.queueService.getPlaylistId(),
      currentTrackIndex: this.queueService.getCurrentIndex(),
      currentTrackId: this.currentTrack?.id ?? null,
      currentTime: this.audio.currentTime,
      isPlaying: !this.audio.paused,
      volume: this.masterVolume,
      isMuted: this.isMuted,
      shuffleEnabled: this.queueService.isShuffleEnabled(),
      repeatMode: this.queueService.getRepeatMode(),
      shuffleOrder: this.queueService.getShuffleOrder(),
      queue: this.queueService.getQueue(),
      playHistory: this.queueService.getPlayHistory()
    };
    await storageService.savePlaybackState(state);
  }

  private recordRecentlyPlayed(trackId: string): void {
    // Add to local cache
    this.recentlyPlayedIds.add(trackId);

    // Persist to storage and cleanup old entries in the background
    storageService.addRecentlyPlayed(trackId)
      .then(() => storageService.cleanupOldRecentlyPlayed(AudioPlayerService.RECENTLY_PLAYED_MAX_COUNT))
      .catch((err) => console.error('Failed to save recently played track:', err));
  }

  async loadRecentlyPlayed(): Promise<void> {
    try {
      this.recentlyPlayedIds = await storageService.getRecentlyPlayedIds(AudioPlayerService.RECENTLY_PLAYED_MAX_COUNT);
    } catch (err) {
      console.error('Failed to load recently played tracks:', err);
      this.recentlyPlayedIds = new Set();
    }
  }

  // Public methods

  on(event: PlayerEventType, callback: PlayerEventCallback): void {
    if (!this.eventListeners.has(event)) {
      this.eventListeners.set(event, new Set());
    }
    this.eventListeners.get(event)!.add(callback);
  }

  off(event: PlayerEventType, callback: PlayerEventCallback): void {
    this.eventListeners.get(event)?.delete(callback);
  }

  private emit(event: PlayerEventType, detail: PlayerEventDetail): void {
    this.eventListeners.get(event)?.forEach(callback => callback(detail));
  }

  setPlaylist(playlistId: string, tracks: TrackInfo[], initialShuffleOrder?: number[]): void {
    this.queueService.setPlaylist(playlistId, tracks, initialShuffleOrder);

    // Clear playlist items from queue when changing playlist
    this.queueService.clearPlaylistQueue();
    // Clear play history when changing playlist
    this.queueService.clearHistory();
    this.emit('queuechange', {});
  }

  async playAtIndex(index: number, autoPlay: boolean = true, startTime: number = 0): Promise<void> {
    if (!this.queueService.playAtIndex(index)) return;

    const actualIndex = this.getActualIndex(index);
    const track = this.queueService.getPlaylist()[actualIndex];

    // Clear and rebuild the queue with upcoming playlist tracks
    this.queueService.clearPlaylistQueue();
    this.queueService.updateConfig({
      cachedTrackIds: this.cachedTrackIds,
      recentlyPlayedIds: this.recentlyPlayedIds,
      isOnline: this.isOnline,
      networkType: this.networkType,
      preventDownloadOnLowData: this.preventDownloadOnLowData
    });
    this.queueService.replenishPlaylistQueue();

    await this.loadTrack(track, autoPlay, startTime);
  }

  async playTrack(track: TrackInfo): Promise<void> {
    const playlist = this.queueService.getPlaylist();
    const index = playlist.findIndex((t: TrackInfo) => t.id === track.id);
    if (index >= 0) {
      // Find the shuffle index if shuffling
      const shuffleIndex = this.queueService.findShuffleIndex(index);
      this.queueService.setCurrentIndex(shuffleIndex);
    }

    // Clear and rebuild the queue with upcoming playlist tracks
    this.queueService.clearPlaylistQueue();
    this.queueService.updateConfig({
      cachedTrackIds: this.cachedTrackIds,
      recentlyPlayedIds: this.recentlyPlayedIds,
      isOnline: this.isOnline,
      networkType: this.networkType,
      preventDownloadOnLowData: this.preventDownloadOnLowData
    });
    this.queueService.replenishPlaylistQueue();

    await this.loadTrack(track, true);
  }

  async play(): Promise<void> {
    if (!this.audioContext) {
      await this.initAudioContext();
    }
    if (this.audioContext?.state === 'suspended') {
      try {
        // Resume can hang if waiting for user gesture, so we timeout
        // The AudioContext was not allowed to start. It must be resumed (or created) after a user gesture on the page.
        await Promise.race([
          this.audioContext.resume(),
          new Promise((_, reject) => setTimeout(() => reject(new Error('Timeout')), 50))
        ]);
      } catch (e) {
        console.warn('AudioContext resume failed/timed out:', e);
      }
    }
    await this.audio.play();
    navigator.mediaSession.playbackState = 'playing';
  }

  pause(): void {
    this.audio.pause();
    navigator.mediaSession.playbackState = 'paused';
  }

  togglePlayPause(): void {
    if (this.audio.paused) {
      this.play();
    } else {
      this.pause();
    }
  }

  async next(): Promise<void> {
    // Add current track to history before moving forward (for shuffle mode)
    if (this.currentTrack && this.queueService.getPlaylistId() !== null && this.queueService.isShuffleEnabled()) {
      const actualIndex = this.getActualIndex(this.queueService.getCurrentIndex());
      this.queueService.addToHistory({
        track: this.currentTrack,
        playlistId: this.queueService.getPlaylistId()!,
        indexInPlaylist: actualIndex,
        source: 'playlist'
      });
    }

    // Check queue first
    if (this.queueService.getQueueLength() > 0) {
      const item = this.queueService.shiftQueue();
      this.emit('queuechange', {});

      // If it's a playlist item, update the current index
      if (item?.source === 'playlist' && item.playlistId === this.queueService.getPlaylistId()) {
        this.queueService.setCurrentIndex(this.queueService.calculateNextIndex());
      }

      await this.loadTrack(item!.track, true);
      this.queueService.updateConfig({
        cachedTrackIds: this.cachedTrackIds,
        recentlyPlayedIds: this.recentlyPlayedIds,
        isOnline: this.isOnline,
        networkType: this.networkType,
        preventDownloadOnLowData: this.preventDownloadOnLowData
      });
      this.queueService.replenishPlaylistQueue();
      return;
    }

    if (!this.hasNext()) return;

    const nextIndex = this.queueService.calculateNextIndex();
    await this.playAtIndex(nextIndex);
  }

  async previous(): Promise<void> {
    // If more than 3 seconds into the song, restart it
    if (this.audio.currentTime > 3) {
      this.audio.currentTime = 0;
      return;
    }

    if (!this.hasPrevious()) return;

    // In shuffle mode, handle history-based navigation
    if (this.queueService.isShuffleEnabled()) {
      // Check if we have history to go back to
      const hasHistory = this.queueService.getPlayHistory().length > 0;

      // If no history, add current track to history so we can return to it with "next"
      if (!hasHistory && this.currentTrack && this.queueService.getPlaylistId() !== null) {
        const actualIndex = this.getActualIndex(this.queueService.getCurrentIndex());
        this.queueService.addToHistory({
          track: this.currentTrack,
          playlistId: this.queueService.getPlaylistId()!,
          indexInPlaylist: actualIndex,
          source: 'playlist'
        });
      }

      const prevItem = this.queueService.getPreviousTrack();
      if (prevItem) {
        // Update index to match the historical/random track
        if (prevItem.playlistId === this.queueService.getPlaylistId()) {
          const shuffleIndex = this.queueService.findShuffleIndex(prevItem.indexInPlaylist);
          if (shuffleIndex >= 0) {
            this.queueService.setCurrentIndex(shuffleIndex);
          } else {
            // If shuffle index not found (empty shuffle order), use actual index directly
            this.queueService.setCurrentIndex(prevItem.indexInPlaylist);
          }
        }
        await this.loadTrack(prevItem.track, true);
        return;
      }
    }

    // Sequential mode
    const prevIndex = this.queueService.calculatePreviousIndex();
    await this.playAtIndex(prevIndex);
  }

  hasNext(): boolean {
    return this.queueService.hasNext();
  }

  hasPrevious(): boolean {
    return this.queueService.hasPrevious();
  }

  seek(time: number): void {
    this.audio.currentTime = Math.max(0, Math.min(time, this.audio.duration || 0));
    this.updatePositionState();
  }

  private linearToLogarithmic(linear: number): number {
    // Convert linear slider value (0-2) to logarithmic gain value
    // Using exponential curve: gain = linear^2 for smoother control at lower volumes
    return linear * linear;
  }

  setVolume(volume: number): void {
    this.masterVolume = Math.max(0, Math.min(2, volume));
    const gainValue = this.linearToLogarithmic(this.masterVolume);
    if (this.masterGainNode) {
      this.masterGainNode.gain.value = this.isMuted ? 0 : gainValue;
    } else {
      // Fallback if no audio context
      this.audioInstance.audio.volume = this.isMuted ? 0 : Math.min(1, gainValue);
    }
    this.emit('volumechange', { volume: this.masterVolume });
  }

  getVolume(): number {
    return this.masterVolume;
  }

  setMuted(muted: boolean): void {
    this.isMuted = muted;
    if (this.masterGainNode) {
      this.masterGainNode.gain.value = muted ? 0 : this.linearToLogarithmic(this.masterVolume);
    } else {
      this.audioInstance.audio.muted = muted;
    }
    this.emit('volumechange', { volume: this.masterVolume });
  }

  getIsMuted(): boolean {
    return this.isMuted;
  }

  toggleMute(): void {
    this.setMuted(!this.isMuted);
  }

  setShuffle(enabled: boolean): void {
    this.queueService.setShuffle(enabled);
    if (!enabled) {
      // Clear history when turning off shuffle
      this.queueService.clearHistory();
    }
    // Rebuild queue with new shuffle order
    this.queueService.clearPlaylistQueue();
    this.queueService.updateConfig({
      cachedTrackIds: this.cachedTrackIds,
      recentlyPlayedIds: this.recentlyPlayedIds,
      isOnline: this.isOnline,
      networkType: this.networkType,
      preventDownloadOnLowData: this.preventDownloadOnLowData
    });
    this.queueService.replenishPlaylistQueue();
    this.scheduleStateSave();
  }

  isShuffleEnabled(): boolean {
    return this.queueService.isShuffleEnabled();
  }

  setRepeatMode(mode: RepeatMode): void {
    this.queueService.setRepeatMode(mode);
    // Replenish queue (may need more items in repeat mode)
    this.queueService.updateConfig({
      cachedTrackIds: this.cachedTrackIds,
      recentlyPlayedIds: this.recentlyPlayedIds,
      isOnline: this.isOnline,
      networkType: this.networkType,
      preventDownloadOnLowData: this.preventDownloadOnLowData
    });
    this.queueService.replenishPlaylistQueue();
    this.scheduleStateSave();
  }

  getRepeatMode(): RepeatMode {
    return this.queueService.getRepeatMode();
  }

  cycleRepeatMode(): RepeatMode {
    const modes: RepeatMode[] = ['off', 'all', 'one'];
    const currentIdx = modes.indexOf(this.queueService.getRepeatMode());
    const newMode = modes[(currentIdx + 1) % modes.length];
    this.queueService.setRepeatMode(newMode);
    // Replenish queue (may need more items in repeat mode)
    this.queueService.updateConfig({
      cachedTrackIds: this.cachedTrackIds,
      recentlyPlayedIds: this.recentlyPlayedIds,
      isOnline: this.isOnline,
      networkType: this.networkType,
      preventDownloadOnLowData: this.preventDownloadOnLowData
    });
    this.queueService.replenishPlaylistQueue();
    this.scheduleStateSave();
    return newMode;
  }

  setQuality(quality: StreamingQuality): void {
    this.quality = quality;
  }

  setReplayGainMode(mode: ReplayGainMode): void {
    this.replayGainMode = mode;
    this.applyReplayGain(this.activeInstance);
  }

  setReplayGainPreamp(preamp: number): void {
    this.replayGainPreamp = preamp;
    this.applyReplayGain(this.activeInstance);
  }

  setPreventDownloadOnLowData(prevent: boolean): void {
    this.preventDownloadOnLowData = prevent;
  }

  setScrobbleEnabled(enabled: boolean): void {
    this.scrobbleEnabled = enabled;
  }

  setNetworkType(type: 'normal' | 'low-data' | 'unknown'): void {
    this.networkType = type;
  }

  setCachedTrackIds(ids: Set<string>): void {
    this.cachedTrackIds = ids;
    this.queueService.updateConfig({ cachedTrackIds: ids });
  }

  setIsOnline(isOnline: boolean): void {
    const wasOffline = !this.isOnline;
    this.isOnline = isOnline;

    if (wasOffline && isOnline) {
      this.processPendingScrobbles();
    }
  }

  getCurrentTrack(): TrackInfo | null {
    return this.currentTrack;
  }

  getCurrentQuality(): StreamingQuality | null {
    return this.currentQuality;
  }

  getCurrentPlaylistId(): string | null {
    return this.queueService.getPlaylistId();
  }

  getCurrentIndex(): number {
    return this.queueService.getCurrentIndex();
  }

  getCurrentTime(): number {
    return this.audio.currentTime;
  }

  getDuration(): number {
    return this.audio.duration || 0;
  }

  isPlaying(): boolean {
    return !this.audio.paused;
  }

  getPlaybackState(): PlaybackState {
    return {
      currentPlaylistId: this.queueService.getPlaylistId(),
      currentTrackIndex: this.queueService.getCurrentIndex(),
      currentTrackId: this.currentTrack?.id ?? null,
      currentTime: this.audio.currentTime,
      isPlaying: !this.audio.paused,
      volume: this.masterVolume,
      isMuted: this.isMuted,
      shuffleEnabled: this.queueService.isShuffleEnabled(),
      repeatMode: this.queueService.getRepeatMode(),
      shuffleOrder: this.queueService.getShuffleOrder(),
      queue: this.queueService.getQueue(),
      playHistory: this.queueService.getPlayHistory()
    };
  }

  async restoreState(state: PlaybackState): Promise<void> {
    this.setVolume(state.volume);
    this.setMuted(state.isMuted);
    
    // Restore state to queue service
    this.queueService.updateConfig({
      shuffleEnabled: state.shuffleEnabled,
      repeatMode: state.repeatMode,
      shuffleOrder: state.shuffleOrder,
      currentPlaylistId: state.currentPlaylistId,
      currentIndex: state.currentTrackIndex,
      playlist: [], // Will be set by the app after playlists are loaded
      cachedTrackIds: this.cachedTrackIds,
      recentlyPlayedIds: this.recentlyPlayedIds,
      isOnline: this.isOnline,
      networkType: this.networkType,
      preventDownloadOnLowData: this.preventDownloadOnLowData
    });

    // Restore queue and ensure each item has a source field (for backwards compatibility)
    const queue = (state.queue ?? []).map(item => ({
      ...item,
      source: item.source ?? 'manual' // Default to manual for old queue items without source
    }));
    this.queueService.setQueue(queue);

    // Restore play history if available
    if (state.playHistory) {
      this.queueService.setPlayHistory(state.playHistory);
    }

    // The actual track loading will be handled by the app after playlists are loaded
  }

  // Queue management methods

  addToQueue(track: TrackInfo, playlistId: string, indexInPlaylist: number): void {
    this.queueService.addToQueue(track, playlistId, indexInPlaylist);
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  addTracksToQueue(items: QueueItem[]): void {
    this.queueService.addTracksToQueue(items);
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  removeFromQueue(index: number): void {
    this.queueService.removeFromQueue(index);
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  clearQueue(): void {
    this.queueService.clearQueue();
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  clearAllQueue(): void {
    this.queueService.clearAllQueue();
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  getQueue(): QueueItem[] {
    return this.queueService.getQueue();
  }

  getQueueLength(): number {
    return this.queueService.getQueueLength();
  }

  moveQueueItem(fromIndex: number, toIndex: number): void {
    this.queueService.moveQueueItem(fromIndex, toIndex);
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  playNextFromQueue(): Promise<void> {
    return this.playFromQueue();
  }

  destroy(): void {
    this.audio.pause();
    this.audio.src = '';
    this.eventListeners.clear();
    if (this.saveStateDebounced) {
      clearTimeout(this.saveStateDebounced);
    }
  }
}

// Singleton instance
export const audioPlayer = new AudioPlayerService();
