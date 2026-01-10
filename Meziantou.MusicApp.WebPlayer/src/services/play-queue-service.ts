import type { TrackInfo, QueueItem, RepeatMode } from '../types';

export interface NextTrackResult {
  track: TrackInfo;
  playlistId: string;
  indexInPlaylist: number;
}

export interface QueueConfig {
  currentIndex: number;
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
 * Service responsible for managing the playback queue, including:
 * - Queue item management (add, remove, reorder)
 * - Next/previous track calculation
 * - Queue replenishment based on playlist, shuffle, and repeat modes
 * - Smart filtering (recently played, cached tracks, duplicates)
 */
export class PlayQueueService {
  private queue: QueueItem[] = [];
  private config: QueueConfig;

  // Constants
  private static readonly REPLENISH_THRESHOLD = 100;
  private static readonly REPLENISH_AMOUNT = 100;
  private static readonly MAX_LOOP_OFFSET = 5;
  private static readonly DUPLICATE_SKIP_THRESHOLD = 50;

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
   * Gets the next track from the queue or playlist
   */
  getNextTrack(): NextTrackResult | null {
    // Check queue first
    if (this.queue.length > 0) {
      const queueItem = this.queue[0];
      return {
        track: queueItem.track,
        playlistId: queueItem.playlistId,
        indexInPlaylist: queueItem.indexInPlaylist
      };
    }

    // Check playlist
    if (this.config.repeatMode === 'one') {
      return null; // Don't preload for repeat one mode
    }

    let nextIndex = this.config.currentIndex + 1;
    if (nextIndex >= this.config.playlist.length) {
      if (this.config.repeatMode === 'all') {
        nextIndex = 0;
      } else {
        return null;
      }
    }

    const actualIndex = this.config.shuffleEnabled && this.config.shuffleOrder.length > 0
      ? this.config.shuffleOrder[nextIndex]
      : nextIndex;

    if (!this.config.currentPlaylistId || !this.config.playlist[actualIndex]) {
      return null;
    }

    return {
      track: this.config.playlist[actualIndex],
      playlistId: this.config.currentPlaylistId,
      indexInPlaylist: actualIndex
    };
  }

  /**
   * Checks if there's a next track available
   */
  hasNext(): boolean {
    if (this.queue.length > 0) return true;
    if (this.config.repeatMode !== 'off') return this.config.playlist.length > 0;
    return this.config.currentIndex < this.config.playlist.length - 1;
  }

  /**
   * Checks if there's a previous track available
   */
  hasPrevious(): boolean {
    if (this.config.repeatMode !== 'off') return this.config.playlist.length > 0;
    return this.config.currentIndex > 0;
  }

  /**
   * Gets the queue as an array
   */
  getQueue(): QueueItem[] {
    return [...this.queue];
  }

  /**
   * Gets the queue length
   */
  getQueueLength(): number {
    return this.queue.length;
  }

  /**
   * Sets the entire queue
   */
  setQueue(queue: QueueItem[]): void {
    this.queue = [...queue];
  }

  /**
   * Removes and returns the first item from the queue
   */
  shiftQueue(): QueueItem | undefined {
    return this.queue.shift();
  }

  /**
   * Adds a track to the queue (manual addition)
   * Inserts before playlist items to give priority to manual additions
   */
  addToQueue(track: TrackInfo, playlistId: string, indexInPlaylist: number): void {
    const firstPlaylistIndex = this.queue.findIndex(item => item.source === 'playlist');
    const insertIndex = firstPlaylistIndex === -1 ? this.queue.length : firstPlaylistIndex;

    this.queue.splice(insertIndex, 0, {
      track,
      playlistId,
      indexInPlaylist,
      source: 'manual'
    });
  }

  /**
   * Adds multiple tracks to the queue
   */
  addTracksToQueue(items: QueueItem[]): void {
    this.queue.push(...items);
  }

  /**
   * Removes an item from the queue by index
   */
  removeFromQueue(index: number): void {
    if (index < 0 || index >= this.queue.length) return;
    this.queue.splice(index, 1);
  }

  /**
   * Clears manual items from the queue, keeping playlist items
   */
  clearQueue(): void {
    const hasPlaylistItems = this.queue.some(item => item.source === 'playlist');
    if (hasPlaylistItems) {
      this.queue = this.queue.filter(item => item.source === 'playlist');
    } else {
      this.queue = [];
    }
  }

  /**
   * Clears all items from the queue
   */
  clearAllQueue(): void {
    this.queue = [];
  }

  /**
   * Moves a queue item from one position to another
   */
  moveQueueItem(fromIndex: number, toIndex: number): void {
    if (fromIndex < 0 || fromIndex >= this.queue.length) return;
    if (toIndex < 0 || toIndex >= this.queue.length) return;

    const [item] = this.queue.splice(fromIndex, 1);
    this.queue.splice(toIndex, 0, item);
  }

  /**
   * Clears playlist items from the queue
   */
  clearPlaylistQueue(): void {
    this.queue = this.queue.filter(item => item.source === 'manual');
  }

  /**
   * Ensures the queue has enough playlist items.
   * Only adds new items if needed - doesn't rebuild existing queue.
   * Replenishment triggers when queue drops below threshold, then adds items to reach target size.
   */
  replenishPlaylistQueue(): void {
    if (!this.config.currentPlaylistId || this.config.playlist.length === 0) return;

    // Count existing playlist items in queue
    const existingPlaylistItems = this.queue.filter(item => item.source === 'playlist').length;

    // Only replenish when queue drops below threshold
    if (existingPlaylistItems >= PlayQueueService.REPLENISH_THRESHOLD) return;

    // When replenishing, add items to bring total back up
    const itemsToAdd = this.config.repeatMode !== 'off'
      ? PlayQueueService.REPLENISH_AMOUNT
      : Math.min(
          PlayQueueService.REPLENISH_AMOUNT,
          this.config.playlist.length - this.config.currentIndex - 1 - existingPlaylistItems
        );

    if (itemsToAdd <= 0) return;

    // Build a set of track IDs already in the queue to avoid duplicates
    const queueTrackIds = new Set(this.queue.map(item => item.track.id));

    // Find the last playlist item's index to know where to continue from
    const lastPlaylistItem = [...this.queue].reverse().find(item => item.source === 'playlist');
    let startIndex = this.config.currentIndex + 1;
    let loopOffset = 0;

    if (lastPlaylistItem) {
      const lastEffectiveIndex = lastPlaylistItem.indexInPlaylist;
      loopOffset = Math.floor(lastEffectiveIndex / this.config.playlist.length);
      const lastActualIndex = lastEffectiveIndex % this.config.playlist.length;

      // Find where this is in the current play order (shuffle or normal)
      if (this.config.shuffleEnabled && this.config.shuffleOrder.length > 0) {
        startIndex = this.config.shuffleOrder.indexOf(lastActualIndex) + 1;
      } else {
        startIndex = lastActualIndex + 1;
      }
    }

    // Generate new playlist items
    const newItems: QueueItem[] = [];
    let addedCount = 0;
    const shouldFilter = (!this.config.isOnline) ||
      (this.config.networkType === 'low-data' && this.config.preventDownloadOnLowData);

    // Track items we've considered to detect when all playlist items are already queued
    let skippedDuplicates = 0;
    let skippedRecentlyPlayed = 0;
    let allowRecentlyPlayed = false;

    while (addedCount < itemsToAdd) {
      for (let i = startIndex; i < this.config.playlist.length && addedCount < itemsToAdd; i++) {
        const actualIndex = this.config.shuffleEnabled && this.config.shuffleOrder.length > 0
          ? this.config.shuffleOrder[i]
          : i;

        const track = this.config.playlist[actualIndex];

        if (shouldFilter && !this.config.cachedTrackIds.has(track.id)) {
          continue;
        }

        const effectiveIndex = actualIndex + (loopOffset * this.config.playlist.length);

        // Skip if track is already in queue, unless all tracks are already queued
        if (queueTrackIds.has(track.id)) {
          skippedDuplicates++;
          // If we've considered many tracks and they're all duplicates, allow adding duplicates
          if (skippedDuplicates >= PlayQueueService.DUPLICATE_SKIP_THRESHOLD) {
            // Reset the duplicate detection and allow duplicates from now on
            queueTrackIds.clear();
            skippedDuplicates = 0;
          } else {
            continue;
          }
        }

        // Skip recently played tracks when possible (to reduce repetition)
        if (!allowRecentlyPlayed && this.config.recentlyPlayedIds.has(track.id)) {
          skippedRecentlyPlayed++;
          // If we've skipped many recently played tracks, allow them to avoid empty queue
          if (skippedRecentlyPlayed >= this.config.playlist.length) {
            allowRecentlyPlayed = true;
            // Don't skip this track since we're now allowing recently played
          } else {
            continue;
          }
        }

        newItems.push({
          track: track,
          playlistId: this.config.currentPlaylistId!,
          indexInPlaylist: effectiveIndex,
          source: 'playlist'
        });
        queueTrackIds.add(track.id);
        addedCount++;
      }

      // If repeat mode is on and we need more, loop from the beginning
      if (this.config.repeatMode === 'all' && addedCount < itemsToAdd && this.config.playlist.length > 0) {
        loopOffset++;
        if (loopOffset > PlayQueueService.MAX_LOOP_OFFSET) break; // Safety break
        startIndex = 0;
        // On subsequent loops, allow recently played tracks since we've gone through all
        if (loopOffset > 1) {
          allowRecentlyPlayed = true;
        }
      } else {
        break;
      }
    }

    // Append new items to the end of the queue
    this.queue.push(...newItems);
  }

  /**
   * Generates a new shuffle order using Fisher-Yates algorithm
   */
  generateShuffleOrder(): number[] {
    const shuffleOrder = [...Array(this.config.playlist.length).keys()];

    // Fisher-Yates shuffle
    for (let i = shuffleOrder.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [shuffleOrder[i], shuffleOrder[j]] = [shuffleOrder[j], shuffleOrder[i]];
    }

    // If currently playing, move current track to front
    if (this.config.currentIndex >= 0) {
      const currentActualIndex = this.config.currentIndex;
      const shufflePosition = shuffleOrder.indexOf(currentActualIndex);
      if (shufflePosition > 0) {
        [shuffleOrder[0], shuffleOrder[shufflePosition]] =
          [shuffleOrder[shufflePosition], shuffleOrder[0]];
      }
    }

    return shuffleOrder;
  }

  /**
   * Calculates the next index after the current track plays
   */
  calculateNextIndex(): number {
    let nextIndex = this.config.currentIndex + 1;
    if (nextIndex >= this.config.playlist.length) {
      if (this.config.repeatMode === 'all') {
        nextIndex = 0;
      }
    }
    return nextIndex;
  }

  /**
   * Calculates the previous index
   */
  calculatePreviousIndex(): number {
    let prevIndex = this.config.currentIndex - 1;
    if (prevIndex < 0) {
      if (this.config.repeatMode !== 'off') {
        prevIndex = this.config.playlist.length - 1;
      }
    }
    return prevIndex;
  }

  /**
   * Gets the actual track index from a play order index
   */
  getActualIndex(index: number): number {
    if (this.config.shuffleEnabled && this.config.shuffleOrder.length > 0) {
      return this.config.shuffleOrder[index];
    }
    return index;
  }

  /**
   * Updates the current index when a queue item from the playlist is played
   */
  updateIndexFromQueueItem(queueItem: QueueItem): void {
    if (queueItem.source === 'playlist' && queueItem.playlistId === this.config.currentPlaylistId) {
      // The indexInPlaylist might be offset for looping, so use modulo
      const actualIndex = queueItem.indexInPlaylist % this.config.playlist.length;
      // Find the shuffle index if shuffling
      if (this.config.shuffleEnabled && this.config.shuffleOrder.length > 0) {
        const shuffleIndex = this.config.shuffleOrder.indexOf(actualIndex);
        if (shuffleIndex >= 0) {
          this.config.currentIndex = shuffleIndex;
        }
      } else {
        this.config.currentIndex = actualIndex;
      }
    }
  }

  /**
   * Finds the shuffle index for a given actual track index
   */
  findShuffleIndex(actualIndex: number): number {
    if (this.config.shuffleEnabled && this.config.shuffleOrder.length > 0) {
      return this.config.shuffleOrder.indexOf(actualIndex);
    }
    return actualIndex;
  }
}
