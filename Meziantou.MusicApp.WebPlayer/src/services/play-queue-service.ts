import type { TrackInfo, QueueItem, RepeatMode } from '../types';

export interface QueueConfig {
  currentPlaylistId: string | null;
  playlist: TrackInfo[];
  shuffleOrder: number[];
  shuffleEnabled: boolean;
  repeatMode: RepeatMode;
  cachedTrackIds: Set<string>;
  recentlyPlayedIds: Set<string>;
  isOnline: boolean;
  networkType: 'normal' | 'low-data' | 'unknown';
  preventDownloadOnLowData: boolean;
}

/**
 * Service responsible for managing the playback queue using a unified array approach:
 * - Single array containing history (before current) and lookahead (after current)
 * - currentIndex points to the currently playing track
 * - Navigation updates the index and refills lookahead as needed
 * - Automatically trims the array when it exceeds limits
 * - Persists the full state (array + index) to IndexedDB
 */
export class PlayQueueService {
  private queueArray: QueueItem[] = []; // Unified array: history + current + lookahead
  private currentIndex: number = -1; // Index of currently playing track in queueArray
  private config: QueueConfig;

  // Constants
  private static readonly MAX_QUEUE_SIZE = 500;
  private static readonly MIN_LOOKAHEAD = 100;
  private static readonly KEEP_HISTORY_ITEMS = 50; // When trimming, keep this many items before current
  private static readonly KEEP_LOOKAHEAD_ITEMS = 100; // When trimming, keep this many items after current
  private static readonly MAX_LOOP_OFFSET = 10;

  constructor(config: QueueConfig) {
    this.config = { ...config };
  }

  /**
   * Updates the internal configuration
   */
  updateConfig(config: Partial<QueueConfig>): void {
    this.config = { ...this.config, ...config };
  }

  /**
   * Gets the full configuration
   */
  getConfig(): QueueConfig {
    return { ...this.config };
  }

  /**
   * Gets the currently playing track
   */
  getCurrentTrack(): TrackInfo | null {
    if (this.currentIndex < 0 || this.currentIndex >= this.queueArray.length) {
      return null;
    }
    return this.queueArray[this.currentIndex]?.track ?? null;
  }

  /**
   * Gets the current playlist ID
   */
  getPlaylistId(): string | null {
    return this.config.currentPlaylistId;
  }

  /**
   * Gets the current index in the queue array
   */
  getCurrentIndex(): number {
    return this.currentIndex;
  }

  /**
   * Gets the current track's index in the playlist (for display purposes)
   * Returns -1 if no current track or current track is not from playlist
   */
  getCurrentPlaylistIndex(): number {
    if (this.currentIndex < 0 || this.currentIndex >= this.queueArray.length) {
      return -1;
    }

    const currentItem = this.queueArray[this.currentIndex];
    if (!currentItem || currentItem.source !== 'playlist') {
      return -1;
    }

    // Return the play order index (not the actual track index)
    return this.findPlaylistIndex(currentItem.indexInPlaylist);
  }

  /**
   * Gets the current playlist
   */
  getPlaylist(): TrackInfo[] {
    return [...this.config.playlist];
  }

  /**
   * Gets the shuffle order
   */
  getShuffleOrder(): number[] {
    return [...this.config.shuffleOrder];
  }

  /**
   * Gets whether shuffle is enabled
   */
  isShuffleEnabled(): boolean {
    return this.config.shuffleEnabled;
  }

  /**
   * Gets the repeat mode
   */
  getRepeatMode(): RepeatMode {
    return this.config.repeatMode;
  }

  /**
   * Gets the full queue array (history + current + lookahead)
   */
  getQueue(): QueueItem[] {
    return [...this.queueArray];
  }

  /**
   * Gets queue items after the current index (lookahead)
   */
  getLookaheadQueue(): QueueItem[] {
    if (this.currentIndex < 0) return [...this.queueArray];
    return this.queueArray.slice(this.currentIndex + 1);
  }

  /**
   * Gets queue items before the current index (history)
   */
  getHistory(): QueueItem[] {
    if (this.currentIndex < 0) return [];
    return this.queueArray.slice(0, this.currentIndex);
  }

  /**
   * Sets the playlist and resets the queue
   */
  setPlaylist(playlistId: string, tracks: TrackInfo[], initialShuffleOrder?: number[]): void {
    this.config.currentPlaylistId = playlistId;
    this.config.playlist = [...tracks];

    // Clear the queue and reset index when switching playlists
    this.queueArray = [];
    this.currentIndex = -1;

    if (this.config.shuffleEnabled) {
      if (initialShuffleOrder && initialShuffleOrder.length === tracks.length) {
        this.config.shuffleOrder = [...initialShuffleOrder];
      } else {
        this.config.shuffleOrder = this.generateShuffleOrder();
      }
    }
  }

  /**
   * Starts playback at a specific index in the playlist
   * Creates the queue array with this track as current
   */
  playAtIndex(playlistIndex: number): boolean {
    if (playlistIndex < 0 || playlistIndex >= this.config.playlist.length) {
      return false;
    }

    const actualIndex = this.getActualPlaylistIndex(playlistIndex);
    const track = this.config.playlist[actualIndex];

    if (!track || !this.config.currentPlaylistId) {
      return false;
    }

    // Create queue with selected track as current (index 0)
    this.queueArray = [{
      track,
      playlistId: this.config.currentPlaylistId,
      indexInPlaylist: actualIndex,
      source: 'playlist'
    }];
    this.currentIndex = 0;

    // Fill lookahead
    this.refillLookahead(playlistIndex);

    return true;
  }

  /**
   * Adds a track to play next (right after current track)
   */
  addToQueue(track: TrackInfo, playlistId: string, indexInPlaylist: number): void {
    const item: QueueItem = {
      track,
      playlistId,
      indexInPlaylist,
      source: 'manual'
    };

    // Insert right after current track
    if (this.currentIndex >= 0) {
      this.queueArray.splice(this.currentIndex + 1, 0, item);
    } else {
      // No current track, add to beginning
      this.queueArray.unshift(item);
      if (this.currentIndex < 0) {
        this.currentIndex = 0;
      } else {
        this.currentIndex++;
      }
    }

    this.trimIfNeeded();
  }

  /**
   * Removes an item from the queue by absolute index
   */
  removeFromQueue(index: number): void {
    if (index < 0 || index >= this.queueArray.length) return;
    if (index === this.currentIndex) return; // Can't remove current track

    this.queueArray.splice(index, 1);

    // Adjust current index if needed
    if (index < this.currentIndex) {
      this.currentIndex--;
    }
  }

  /**
   * Moves a queue item from one position to another
   */
  moveQueueItem(fromIndex: number, toIndex: number): void {
    if (fromIndex < 0 || fromIndex >= this.queueArray.length) return;
    if (toIndex < 0 || toIndex >= this.queueArray.length) return;
    if (fromIndex === this.currentIndex) return; // Can't move current track
    if (fromIndex === toIndex) return; // No move needed

    const [item] = this.queueArray.splice(fromIndex, 1);
    this.queueArray.splice(toIndex, 0, item);

    // Adjust current index based on the move
    if (fromIndex < this.currentIndex && toIndex >= this.currentIndex) {
      // Item moved from before current to at/after current
      this.currentIndex--;
    } else if (fromIndex > this.currentIndex && toIndex <= this.currentIndex) {
      // Item moved from after current to at/before current
      this.currentIndex++;
    }
  }

  /**
   * Advances to the next track in the queue
   * @param force If true, advances even when repeat mode is 'one'
   */
  next(force = false): boolean {
    if (!this.hasNext()) {
      return false;
    }

    // Handle repeat one (unless forced)
    if (!force && this.config.repeatMode === 'one') {
      return true; // Stay on current track
    }

    this.currentIndex++;

    // Ensure we have enough lookahead
    this.refillLookahead();
    this.trimIfNeeded();

    return true;
  }

  /**
   * Goes back to the previous track in the queue
   */
  previous(): boolean {
    if (!this.hasPrevious()) {
      return false;
    }

    // If at start of queue (no history), add a random/previous track at beginning
    if (this.currentIndex === 0) {
      const prevItem = this.generatePreviousTrack();
      if (prevItem) {
        this.queueArray.unshift(prevItem);
        // currentIndex stays at 0 (which is now the previous track)
        this.trimIfNeeded();
        return true;
      }
      return false;
    }

    // Navigate backward in existing history
    this.currentIndex--;
    return true;
  }

  /**
   * Checks if there's a next track available
   */
  hasNext(): boolean {
    if (this.config.repeatMode !== 'off') return this.config.playlist.length > 0;
    if (this.currentIndex < 0) return this.queueArray.length > 0;
    return this.currentIndex < this.queueArray.length - 1;
  }

  /**
   * Checks if there's a previous track available
   */
  hasPrevious(): boolean {
    if (!this.config.currentPlaylistId || this.config.playlist.length === 0) {
      return false;
    }

    // If we have history in the array, can go back
    if (this.currentIndex > 0) {
      return true;
    }

    // If at start but have playlist, can generate previous (shuffle) or calculate (sequential)
    if (this.config.shuffleEnabled) {
      return this.config.playlist.length > 1;
    }

    // Sequential mode - check if we can go back in playlist
    if (this.config.repeatMode !== 'off') {
      return this.config.playlist.length > 0;
    }

    // Sequential mode without repeat - check if there's a previous track in the playlist
    const currentPlaylistItem = this.queueArray[this.currentIndex];
    if (!currentPlaylistItem || currentPlaylistItem.source !== 'playlist') return false;

    const playlistIndex = this.findPlaylistIndex(currentPlaylistItem.indexInPlaylist);
    return playlistIndex > 0;
  }

  /**
   * Sets shuffle enabled/disabled
   */
  setShuffle(enabled: boolean): void {
    this.config.shuffleEnabled = enabled;
    if (enabled) {
      this.config.shuffleOrder = this.generateShuffleOrder();
    }

    // Regenerate lookahead with new shuffle order
    if (this.currentIndex >= 0) {
      // Keep history, regenerate lookahead
      this.queueArray = this.queueArray.slice(0, this.currentIndex + 1);
      this.refillLookahead();
    }
  }

  /**
   * Sets the repeat mode
   */
  setRepeatMode(mode: RepeatMode): void {
    this.config.repeatMode = mode;
  }

  /**
   * Restores the queue state from persistence
   */
  restoreQueue(queueArray: QueueItem[], currentIndex: number): void {
    this.queueArray = [...queueArray];
    this.currentIndex = currentIndex;

    // Ensure we have enough lookahead
    this.refillLookahead();
  }

  /**
   * Generates a previous track for shuffle mode or calculates for sequential
   */
  private generatePreviousTrack(): QueueItem | null {
    if (!this.config.currentPlaylistId || this.config.playlist.length === 0) {
      return null;
    }

    const currentItem = this.queueArray[this.currentIndex];

    if (this.config.shuffleEnabled) {
      // Generate random track different from current
      let currentActualIndex = currentItem?.indexInPlaylist ?? -1;
      let randomIndex: number;

      if (this.config.playlist.length === 1) {
        randomIndex = 0;
      } else {
        do {
          randomIndex = Math.floor(Math.random() * this.config.playlist.length);
        } while (randomIndex === currentActualIndex);
      }

      const track = this.config.playlist[randomIndex];
      if (!track) return null;

      return {
        track,
        playlistId: this.config.currentPlaylistId,
        indexInPlaylist: randomIndex,
        source: 'playlist'
      };
    } else {
      // Sequential mode - go to previous track in playlist
      if (!currentItem) return null;

      const currentPlaylistIndex = this.findPlaylistIndex(currentItem.indexInPlaylist);
      let prevPlaylistIndex = currentPlaylistIndex - 1;

      if (prevPlaylistIndex < 0) {
        if (this.config.repeatMode === 'off') {
          return null;
        }
        prevPlaylistIndex = this.config.playlist.length - 1;
      }

      const actualIndex = this.getActualPlaylistIndex(prevPlaylistIndex);
      const track = this.config.playlist[actualIndex];

      if (!track) return null;

      return {
        track,
        playlistId: this.config.currentPlaylistId,
        indexInPlaylist: actualIndex,
        source: 'playlist'
      };
    }
  }

  /**
   * Refills the lookahead portion of the queue
   */
  private refillLookahead(startPlaylistIndex?: number): void {
    if (!this.config.currentPlaylistId || this.config.playlist.length === 0) {
      return;
    }

    const lookaheadCount = this.queueArray.length - this.currentIndex - 1;

    if (lookaheadCount >= PlayQueueService.MIN_LOOKAHEAD) {
      return; // Already have enough lookahead
    }

    const itemsNeeded = PlayQueueService.MIN_LOOKAHEAD - lookaheadCount;

    // Determine where to start generating tracks
    let nextPlaylistIndex: number;
    if (startPlaylistIndex !== undefined) {
      nextPlaylistIndex = startPlaylistIndex + 1;
    } else {
      const lastItem = this.queueArray[this.queueArray.length - 1];
      if (lastItem && lastItem.source === 'playlist') {
        nextPlaylistIndex = this.findPlaylistIndex(lastItem.indexInPlaylist) + 1;
      } else if (this.currentIndex >= 0) {
        const currentItem = this.queueArray[this.currentIndex];
        if (currentItem && currentItem.source === 'playlist') {
          nextPlaylistIndex = this.findPlaylistIndex(currentItem.indexInPlaylist) + 1;
        } else {
          nextPlaylistIndex = 0;
        }
      } else {
        nextPlaylistIndex = 0;
      }
    }

    const newItems: QueueItem[] = [];
    let addedCount = 0;
    let loopCount = 0;

    const queueTrackIds = new Set(this.queueArray.map(item => item.track.id));
    const shouldFilter = (!this.config.isOnline) ||
      (this.config.networkType === 'low-data' && this.config.preventDownloadOnLowData);

    while (addedCount < itemsNeeded && loopCount < PlayQueueService.MAX_LOOP_OFFSET) {
      for (let i = nextPlaylistIndex; i < this.config.playlist.length && addedCount < itemsNeeded; i++) {
        const actualIndex = this.getActualPlaylistIndex(i);
        const track = this.config.playlist[actualIndex];

        if (!track) continue;

        if (shouldFilter && !this.config.cachedTrackIds.has(track.id)) {
          continue;
        }

        // Skip duplicates unless we've tried many tracks
        if (queueTrackIds.has(track.id)) {
          if (queueTrackIds.size < this.config.playlist.length) {
            continue;
          }
        }

        newItems.push({
          track,
          playlistId: this.config.currentPlaylistId,
          indexInPlaylist: actualIndex,
          source: 'playlist'
        });

        queueTrackIds.add(track.id);
        addedCount++;
      }

      if (addedCount < itemsNeeded && this.config.repeatMode === 'all') {
        nextPlaylistIndex = 0;
        loopCount++;
      } else {
        break;
      }
    }

    this.queueArray.push(...newItems);
  }

  /**
   * Trims the queue if it exceeds the maximum size
   */
  private trimIfNeeded(): void {
    if (this.queueArray.length <= PlayQueueService.MAX_QUEUE_SIZE) {
      return;
    }

    if (this.currentIndex < 0) return;

    // Calculate how many items to keep before and after current
    const keepBefore = Math.min(PlayQueueService.KEEP_HISTORY_ITEMS, this.currentIndex);
    const keepAfter = PlayQueueService.KEEP_LOOKAHEAD_ITEMS;

    const newStartIndex = this.currentIndex - keepBefore;
    const newEndIndex = Math.min(this.currentIndex + keepAfter + 1, this.queueArray.length);

    this.queueArray = this.queueArray.slice(newStartIndex, newEndIndex);
    this.currentIndex = keepBefore;

    // Refill if we trimmed too much lookahead
    this.refillLookahead();
  }

  /**
   * Generates a new shuffle order using Fisher-Yates algorithm
   */
  private generateShuffleOrder(): number[] {
    const shuffleOrder = [...Array(this.config.playlist.length).keys()];

    for (let i = shuffleOrder.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [shuffleOrder[i], shuffleOrder[j]] = [shuffleOrder[j], shuffleOrder[i]];
    }

    return shuffleOrder;
  }

  /**
   * Gets the actual track index in playlist from a play order index
   */
  private getActualPlaylistIndex(playlistIndex: number): number {
    if (this.config.shuffleEnabled && this.config.shuffleOrder.length > 0) {
      return this.config.shuffleOrder[playlistIndex] ?? playlistIndex;
    }
    return playlistIndex;
  }

  /**
   * Finds the playlist index (play order) for an actual track index
   */
  private findPlaylistIndex(actualIndex: number): number {
    actualIndex = actualIndex % this.config.playlist.length;

    if (this.config.shuffleEnabled && this.config.shuffleOrder.length > 0) {
      const index = this.config.shuffleOrder.indexOf(actualIndex);
      return index >= 0 ? index : actualIndex;
    }
    return actualIndex;
  }
}
