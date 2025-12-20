import type { TrackInfo, StreamingQuality, ReplayGainMode, PlaybackState, RepeatMode, QueueItem } from '../types';
import { getApiService } from './api-service';
import { storageService } from './storage-service';

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
  private currentPlaylistId: string | null = null;
  private playlist: TrackInfo[] = [];
  private currentIndex: number = -1;
  private shuffleOrder: number[] = [];
  private shuffleEnabled: boolean = false;
  private repeatMode: RepeatMode = 'off';
  private queue: QueueItem[] = [];
  
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

  constructor() {
    this.audioInstance = this.createAudioInstance();
    this.setupAudioEvents(this.audioInstance);
    this.setupMediaSession();
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
    this.masterGainNode.gain.value = this.masterVolume;
    
    // Connect audio element to the audio context
    this.connectAudioInstance(this.audioInstance);
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
        await storageService.addPendingScrobble(trackId, submission);
      }
    } else {
      await storageService.addPendingScrobble(trackId, submission);
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
  
  private getNextTrack(): { track: TrackInfo; playlistId: string; indexInPlaylist: number } | null {
    // Check queue first
    if (this.queue.length > 0) {
      const queueItem = this.queue[0];
      return { track: queueItem.track, playlistId: queueItem.playlistId, indexInPlaylist: queueItem.indexInPlaylist };
    }
    
    // Check playlist
    if (this.repeatMode === 'one') {
      return null; // Don't preload for repeat one mode
    }
    
    let nextIndex = this.currentIndex + 1;
    if (nextIndex >= this.playlist.length) {
      if (this.repeatMode === 'all') {
        nextIndex = 0;
      } else {
        return null;
      }
    }
    
    const actualIndex = this.shuffleEnabled && this.shuffleOrder.length > 0
      ? this.shuffleOrder[nextIndex]
      : nextIndex;
    
    return {
      track: this.playlist[actualIndex],
      playlistId: this.currentPlaylistId!,
      indexInPlaylist: actualIndex
    };
  }
  
  private checkForPreload(): void {
    if (this.isPreloading || this.preloadedTrack) return;
    
    const duration = this.audio.duration;
    
    if (!duration || isNaN(duration)) return;
    
    this.preloadNextTrack();
  }
  
  private async preloadNextTrack(): Promise<void> {
    const nextTrackInfo = this.getNextTrack();
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
    
    if (this.repeatMode === 'one') {
      this.audio.currentTime = 0;
      this.play();
      return;
    }

    // Always play from queue - it should contain upcoming tracks
    if (this.queue.length > 0) {
      await this.playFromQueue();
      return;
    }

    // Queue is empty and no repeat - stop playback
    // This shouldn't normally happen as queue should be populated
  }

  private async playFromQueue(): Promise<void> {
    if (this.queue.length === 0) return;
    
    const queueItem = this.queue.shift()!;
    
    // Update current index if this is a playlist track from the current playlist
    if (queueItem.source === 'playlist' && queueItem.playlistId === this.currentPlaylistId) {
      // The indexInPlaylist might be offset for looping, so use modulo
      const actualIndex = queueItem.indexInPlaylist % this.playlist.length;
      // Find the shuffle index if shuffling
      if (this.shuffleEnabled && this.shuffleOrder.length > 0) {
        const shuffleIndex = this.shuffleOrder.indexOf(actualIndex);
        if (shuffleIndex >= 0) {
          this.currentIndex = shuffleIndex;
        }
      } else {
        this.currentIndex = actualIndex;
      }
    }
    
    this.emit('queuechange', {});
    
    // Replenish queue if needed (keep it at ~200 items for repeat mode)
    if (this.repeatMode !== 'off' || this.queue.filter(i => i.source === 'playlist').length < 10) {
      this.rebuildPlaylistQueue();
    } else {
      this.scheduleStateSave();
    }
    
    await this.loadTrack(queueItem.track, true);
  }

  private getActualIndex(index: number): number {
    if (this.shuffleEnabled && this.shuffleOrder.length > 0) {
      return this.shuffleOrder[index];
    }
    return index;
  }

  private generateShuffleOrder(): void {
    this.shuffleOrder = [...Array(this.playlist.length).keys()];
    
    // Fisher-Yates shuffle
    for (let i = this.shuffleOrder.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [this.shuffleOrder[i], this.shuffleOrder[j]] = [this.shuffleOrder[j], this.shuffleOrder[i]];
    }

    // If currently playing, move current track to front
    if (this.currentIndex >= 0) {
      const currentActualIndex = this.currentIndex;
      const shufflePosition = this.shuffleOrder.indexOf(currentActualIndex);
      if (shufflePosition > 0) {
        [this.shuffleOrder[0], this.shuffleOrder[shufflePosition]] = 
          [this.shuffleOrder[shufflePosition], this.shuffleOrder[0]];
      }
    }
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
      currentPlaylistId: this.currentPlaylistId,
      currentTrackIndex: this.currentIndex,
      currentTime: this.audio.currentTime,
      isPlaying: !this.audio.paused,
      volume: this.masterVolume,
      isMuted: this.isMuted,
      shuffleEnabled: this.shuffleEnabled,
      repeatMode: this.repeatMode,
      shuffleOrder: this.shuffleOrder,
      queue: this.queue
    };
    await storageService.savePlaybackState(state);
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
    this.currentPlaylistId = playlistId;
    this.playlist = tracks;
    this.currentIndex = -1;
    
    if (this.shuffleEnabled) {
      if (initialShuffleOrder && initialShuffleOrder.length === tracks.length) {
        this.shuffleOrder = [...initialShuffleOrder];
      } else {
        this.generateShuffleOrder();
      }
    }
    
    // Clear playlist items from queue when changing playlist
    this.queue = this.queue.filter(item => item.source === 'manual');
    this.emit('queuechange', {});
  }

  async playAtIndex(index: number, autoPlay: boolean = true, startTime: number = 0): Promise<void> {
    if (index < 0 || index >= this.playlist.length) return;
    
    this.currentIndex = index;
    const actualIndex = this.getActualIndex(index);
    const track = this.playlist[actualIndex];
    
    // Rebuild the queue with upcoming playlist tracks
    this.rebuildPlaylistQueue();
    
    await this.loadTrack(track, autoPlay, startTime);
  }

  async playTrack(track: TrackInfo, playlistId: string): Promise<void> {
    const index = this.playlist.findIndex(t => t.id === track.id);
    if (index >= 0) {
      // Find the shuffle index if shuffling
      if (this.shuffleEnabled) {
        // When playing a specific track in shuffle mode, we want to play THAT track.
        // But currentIndex refers to the index in the shuffleOrder array.
        // So we need to find where the track's original index is located in the shuffleOrder.
        const shuffleIndex = this.shuffleOrder.indexOf(index);
        if (shuffleIndex >= 0) {
          this.currentIndex = shuffleIndex;
        } else {
          // Fallback if something is wrong with shuffle order
          this.currentIndex = index;
        }
      } else {
        this.currentIndex = index;
      }
    }
    
    this.currentPlaylistId = playlistId;
    
    // Rebuild the queue with upcoming playlist tracks
    this.rebuildPlaylistQueue();
    
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
    // Check queue first
    if (this.queue.length > 0) {
      const item = this.queue.shift();
      this.emit('queuechange', {});
      
      // If it's a playlist item, update the current index
      if (item?.source === 'playlist' && item.playlistId === this.currentPlaylistId) {
        let nextIndex = this.currentIndex + 1;
        if (nextIndex >= this.playlist.length) {
          if (this.repeatMode === 'all') {
            nextIndex = 0;
          }
        }
        this.currentIndex = nextIndex;
      }
      
      await this.loadTrack(item!.track, true);
      this.rebuildPlaylistQueue();
      return;
    }

    if (!this.hasNext()) return;
    
    let nextIndex = this.currentIndex + 1;
    if (nextIndex >= this.playlist.length) {
      if (this.repeatMode !== 'off') {
        nextIndex = 0;
      }
    }
    await this.playAtIndex(nextIndex);
  }

  async previous(): Promise<void> {
    // If more than 3 seconds into the song, restart it
    if (this.audio.currentTime > 3) {
      this.audio.currentTime = 0;
      return;
    }

    if (!this.hasPrevious()) return;
    
    let prevIndex = this.currentIndex - 1;
    if (prevIndex < 0) {
      if (this.repeatMode !== 'off') {
        prevIndex = this.playlist.length - 1;
      }
    }
    await this.playAtIndex(prevIndex);
  }

  hasNext(): boolean {
    if (this.queue.length > 0) return true;
    if (this.repeatMode !== 'off') return this.playlist.length > 0;
    return this.currentIndex < this.playlist.length - 1;
  }

  hasPrevious(): boolean {
    if (this.repeatMode !== 'off') return this.playlist.length > 0;
    return this.currentIndex > 0;
  }

  seek(time: number): void {
    this.audio.currentTime = Math.max(0, Math.min(time, this.audio.duration || 0));
    this.updatePositionState();
  }

  setVolume(volume: number): void {
    this.masterVolume = Math.max(0, Math.min(2, volume));
    if (this.masterGainNode) {
      this.masterGainNode.gain.value = this.isMuted ? 0 : this.masterVolume;
    } else {
      // Fallback if no audio context
      this.audioInstance.audio.volume = this.isMuted ? 0 : Math.min(1, this.masterVolume);
    }
    this.emit('volumechange', { volume: this.masterVolume });
  }

  getVolume(): number {
    return this.masterVolume;
  }

  setMuted(muted: boolean): void {
    this.isMuted = muted;
    if (this.masterGainNode) {
      this.masterGainNode.gain.value = muted ? 0 : this.masterVolume;
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
    this.shuffleEnabled = enabled;
    if (enabled) {
      this.generateShuffleOrder();
    }
    // Rebuild queue with new shuffle order
    this.rebuildPlaylistQueue();
    this.scheduleStateSave();
  }

  isShuffleEnabled(): boolean {
    return this.shuffleEnabled;
  }

  setRepeatMode(mode: RepeatMode): void {
    this.repeatMode = mode;
    // Rebuild queue (may need more items in repeat mode)
    this.rebuildPlaylistQueue();
    this.scheduleStateSave();
  }

  getRepeatMode(): RepeatMode {
    return this.repeatMode;
  }

  cycleRepeatMode(): RepeatMode {
    const modes: RepeatMode[] = ['off', 'all', 'one'];
    const currentIdx = modes.indexOf(this.repeatMode);
    this.repeatMode = modes[(currentIdx + 1) % modes.length];
    // Rebuild queue (may need more items in repeat mode)
    this.rebuildPlaylistQueue();
    this.scheduleStateSave();
    return this.repeatMode;
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
    return this.currentPlaylistId;
  }

  getCurrentIndex(): number {
    return this.currentIndex;
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
      currentPlaylistId: this.currentPlaylistId,
      currentTrackIndex: this.currentIndex,
      currentTime: this.audio.currentTime,
      isPlaying: !this.audio.paused,
      volume: this.masterVolume,
      isMuted: this.isMuted,
      shuffleEnabled: this.shuffleEnabled,
      repeatMode: this.repeatMode,
      shuffleOrder: this.shuffleOrder,
      queue: this.queue
    };
  }

  async restoreState(state: PlaybackState): Promise<void> {
    this.setVolume(state.volume);
    this.setMuted(state.isMuted);
    this.shuffleEnabled = state.shuffleEnabled;
    this.repeatMode = state.repeatMode;
    this.shuffleOrder = state.shuffleOrder;
    this.currentPlaylistId = state.currentPlaylistId;
    this.currentIndex = state.currentTrackIndex;
    
    // Restore queue and ensure each item has a source field (for backwards compatibility)
    this.queue = (state.queue ?? []).map(item => ({
      ...item,
      source: item.source ?? 'manual' // Default to manual for old queue items without source
    }));

    // The actual track loading will be handled by the app after playlists are loaded
  }

  // Queue management methods

  /**
   * Rebuilds the playlist portion of the queue based on current position.
   * Keeps manual items and adds upcoming playlist tracks.
   * Ensures at least 200 playlist items in queue when repeat mode is on.
   */
  private rebuildPlaylistQueue(): void {
    if (!this.currentPlaylistId || this.playlist.length === 0) return;
    
    // Keep only manually added items
    const manualItems = this.queue.filter(item => item.source === 'manual');
    
    // Calculate how many playlist items we need
    const targetCount = this.repeatMode !== 'off' ? 200 : Math.min(200, this.playlist.length - this.currentIndex - 1);
    
    // Generate playlist items starting from current position + 1
    const playlistItems: QueueItem[] = [];
    let addedCount = 0;
    let loopOffset = 0;
    
    const shouldFilter = (!this.isOnline) || (this.networkType === 'low-data' && this.preventDownloadOnLowData);

    while (addedCount < targetCount) {
      for (let i = this.currentIndex + 1; i < this.playlist.length && addedCount < targetCount; i++) {
        const actualIndex = this.shuffleEnabled && this.shuffleOrder.length > 0
          ? this.shuffleOrder[i]
          : i;
        
        const track = this.playlist[actualIndex];
        if (shouldFilter && !this.cachedTrackIds.has(track.id)) {
          continue;
        }

        // For looping, adjust the index to show it's a repeated track
        const effectiveIndex = actualIndex + (loopOffset * this.playlist.length);
        
        playlistItems.push({
          track: track,
          playlistId: this.currentPlaylistId!,
          indexInPlaylist: effectiveIndex,
          source: 'playlist'
        });
        addedCount++;
      }
      
      // If repeat mode is on and we need more, loop from the beginning
      if (this.repeatMode === 'all' && addedCount < targetCount && this.playlist.length > 0) {
        loopOffset++;
        if (loopOffset > 5) break; // Safety break to prevent infinite loops if all tracks are filtered
        // Start from index 0 for looping
        for (let i = 0; i <= this.currentIndex && addedCount < targetCount; i++) {
          const actualIndex = this.shuffleEnabled && this.shuffleOrder.length > 0
            ? this.shuffleOrder[i]
            : i;
          
          const track = this.playlist[actualIndex];
          if (shouldFilter && !this.cachedTrackIds.has(track.id)) {
            continue;
          }

          const effectiveIndex = actualIndex + (loopOffset * this.playlist.length);
          
          playlistItems.push({
            track: track,
            playlistId: this.currentPlaylistId!,
            indexInPlaylist: effectiveIndex,
            source: 'playlist'
          });
          addedCount++;
        }
      } else {
        break; // No repeat, don't loop
      }
    }
    
    // Combine: manual items first, then playlist items
    this.queue = [...manualItems, ...playlistItems];
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  addToQueue(track: TrackInfo, playlistId: string, indexInPlaylist: number): void {
    // Insert manual items before playlist items
    const firstPlaylistIndex = this.queue.findIndex(item => item.source === 'playlist');
    const insertIndex = firstPlaylistIndex === -1 ? this.queue.length : firstPlaylistIndex;
    
    this.queue.splice(insertIndex, 0, { track, playlistId, indexInPlaylist, source: 'manual' });
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  addTracksToQueue(items: QueueItem[]): void {
    this.queue.push(...items);
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  removeFromQueue(index: number): void {
    if (index < 0 || index >= this.queue.length) return;
    this.queue.splice(index, 1);
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  clearQueue(): void {
    // Clear only manual items, keep playlist items
    // Or clear all if there are no playlist items
    const hasPlaylistItems = this.queue.some(item => item.source === 'playlist');
    if (hasPlaylistItems) {
      this.queue = this.queue.filter(item => item.source === 'playlist');
    } else {
      this.queue = [];
    }
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  clearAllQueue(): void {
    this.queue = [];
    this.emit('queuechange', {});
    this.scheduleStateSave();
  }

  getQueue(): QueueItem[] {
    return [...this.queue];
  }

  getQueueLength(): number {
    return this.queue.length;
  }

  moveQueueItem(fromIndex: number, toIndex: number): void {
    if (fromIndex < 0 || fromIndex >= this.queue.length) return;
    if (toIndex < 0 || toIndex >= this.queue.length) return;
    
    const [item] = this.queue.splice(fromIndex, 1);
    this.queue.splice(toIndex, 0, item);
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
