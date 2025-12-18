import type { StreamingQuality, TrackInfo } from '../types';
import { getApiService } from './api-service';
import { storageService } from './storage-service';

export interface DownloadProgress {
  trackId: string;
  progress: number; // 0-1
  status: 'pending' | 'downloading' | 'complete' | 'error';
  error?: string;
}

type DownloadCallback = (progress: DownloadProgress) => void;

class DownloadService {
  private downloadQueue: Map<string, { track: TrackInfo; playlistIds: Set<string>; quality: StreamingQuality }> = new Map();
  private activeDownloads: Set<string> = new Set();
  private maxConcurrentDownloads = 8;
  private callbacks: Set<DownloadCallback> = new Set();
  private cachedTrackIds: Set<string> = new Set();

  async init(): Promise<void> {
    this.cachedTrackIds = await storageService.getCachedTrackIds();
  }

  async refreshCacheState(): Promise<void> {
    this.cachedTrackIds = await storageService.getCachedTrackIds();
  }

  onProgress(callback: DownloadCallback): () => void {
    this.callbacks.add(callback);
    return () => this.callbacks.delete(callback);
  }

  private notifyProgress(progress: DownloadProgress): void {
    this.callbacks.forEach(cb => cb(progress));
  }

  isTrackCached(trackId: string): boolean {
    return this.cachedTrackIds.has(trackId);
  }

  isTrackDownloading(trackId: string): boolean {
    return this.activeDownloads.has(trackId) || this.downloadQueue.has(trackId);
  }

  async queueDownload(track: TrackInfo, playlistId: string, quality: StreamingQuality): Promise<void> {
    if (this.cachedTrackIds.has(track.id)) {
      await storageService.addPlaylistToTrack(track.id, playlistId);
      return;
    }

    if (this.downloadQueue.has(track.id)) {
      const item = this.downloadQueue.get(track.id)!;
      item.playlistIds.add(playlistId);
      return;
    }

    this.downloadQueue.set(track.id, { track, playlistIds: new Set([playlistId]), quality });
    this.notifyProgress({ trackId: track.id, progress: 0, status: 'pending' });
    this.processQueue();
  }

  async queuePlaylistDownload(tracks: TrackInfo[], playlistId: string, quality: StreamingQuality): Promise<void> {
    for (const track of tracks) {
      await this.queueDownload(track, playlistId, quality);
    }
  }

  cancelDownload(trackId: string): void {
    this.downloadQueue.delete(trackId);
  }

  cancelPlaylistDownloads(playlistId: string): void {
    for (const [trackId, item] of this.downloadQueue.entries()) {
      item.playlistIds.delete(playlistId);
      if (item.playlistIds.size === 0) {
        this.downloadQueue.delete(trackId);
      }
    }
  }

  clearQueue(): void {
    this.downloadQueue.clear();
  }

  private async processQueue(): Promise<void> {
    if (this.activeDownloads.size >= this.maxConcurrentDownloads) {
      return;
    }

    const next = this.downloadQueue.entries().next();
    if (next.done) return;

    const [trackId, { track, playlistIds, quality }] = next.value;
    this.downloadQueue.delete(trackId);
    this.activeDownloads.add(trackId);

    try {
      await this.downloadTrack(track, Array.from(playlistIds), quality);
    } finally {
      this.activeDownloads.delete(trackId);
      this.processQueue();
    }
  }

  private async downloadTrack(track: TrackInfo, playlistIds: string[], quality: StreamingQuality): Promise<void> {
    const api = getApiService();
    
    this.notifyProgress({ trackId: track.id, progress: 0, status: 'downloading' });

    try {
      const url = api.getSongStreamUrl(track.id, quality);
      const response = await fetch(url, {
        headers: api.getAuthHeaders()
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const contentLength = response.headers.get('content-length');
      const total = contentLength ? parseInt(contentLength, 10) : 0;
      
      const reader = response.body?.getReader();
      if (!reader) {
        throw new Error('No response body');
      }

      const chunks: BlobPart[] = [];
      let received = 0;

      while (true) {
        const { done, value } = await reader.read();
        
        if (done) break;
        
        chunks.push(value);
        received += value.length;

        if (total > 0) {
          this.notifyProgress({ 
            trackId: track.id, 
            progress: received / total, 
            status: 'downloading' 
          });
        }
      }

      const blob = new Blob(chunks);
      
      await storageService.saveCachedTrack({
        trackId: track.id,
        playlistIds,
        blob,
        quality,
        cachedAt: Date.now()
      });

      // Try to download cover art
      try {
        const cachedCover = await storageService.getCachedCover(track.id);
        const isMissing = await storageService.isCoverMissing(track.id);
        
        if (!cachedCover && !isMissing) {
          // Use a reasonable size for caching (256px)
          const coverUrl = api.getSongCoverUrl(track.id, 256);
          const coverResponse = await fetch(coverUrl, {
            headers: api.getAuthHeaders()
          });
          
          if (coverResponse.ok) {
            const coverBlob = await coverResponse.blob();
            await storageService.saveCachedCover(track.id, coverBlob);
          } else if (coverResponse.status === 404) {
            await storageService.addMissingCover(track.id);
          }
        }
      } catch (error) {
        console.error(`Failed to download cover for track ${track.id}`, error);
      }

      this.cachedTrackIds.add(track.id);
      this.notifyProgress({ trackId: track.id, progress: 1, status: 'complete' });
    } catch (error) {
      this.notifyProgress({ 
        trackId: track.id, 
        progress: 0, 
        status: 'error',
        error: error instanceof Error ? error.message : 'Download failed'
      });
    }
  }

  async deleteTrack(trackId: string): Promise<void> {
    await storageService.deleteCachedTrack(trackId);
    this.cachedTrackIds.delete(trackId);
  }

  async deletePlaylistTracks(playlistId: string): Promise<void> {
    const tracks = await storageService.getCachedTracksByPlaylist(playlistId);
    for (const track of tracks) {
      await storageService.removePlaylistFromTrack(track.trackId, playlistId);
      const stillCached = await storageService.getCachedTrack(track.trackId);
      if (!stillCached) {
        this.cachedTrackIds.delete(track.trackId);
      }
    }
  }

  getQueueSize(): number {
    return this.downloadQueue.size + this.activeDownloads.size;
  }
}

export const downloadService = new DownloadService();
