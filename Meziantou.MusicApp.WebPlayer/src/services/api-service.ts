import type {
  PlaylistsResponse,
  PlaylistTracksResponse,
  ScanStatusResponse,
  CacheCleanupResponse,
  StreamingQuality,
  LyricsResponse
} from '../types';

export class ApiService {
  private static readonly coverAcceptHeader = 'image/avif,image/webp,image/png,image/jpeg;q=0.8,*/*;q=0.5';

  private baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
  }

  updateConfig(baseUrl: string): void {
    this.baseUrl = baseUrl.replace(/\/$/, '');
  }

  private async fetch<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
    const url = `${this.baseUrl}${endpoint}`;
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 30000); // 30s timeout

    try {
      const response = await fetch(url, {
        ...options,
        signal: controller.signal,
        headers: options.headers
      });

      if (!response.ok) {
        const error = await response.json().catch(() => ({ error: response.statusText }));
        throw new Error(error.error || `HTTP ${response.status}`);
      }

      return await response.json();
    } finally {
      clearTimeout(timeoutId);
    }
  }

  async getPlaylists(): Promise<PlaylistsResponse> {
    return this.fetch<PlaylistsResponse>('/api/playlists.json');
  }

  async getPlaylistTracks(playlistId: string): Promise<PlaylistTracksResponse> {
    return this.fetch<PlaylistTracksResponse>(`/api/playlists/${encodeURIComponent(playlistId)}.json`);
  }

  async getScanStatus(): Promise<ScanStatusResponse> {
    return this.fetch<ScanStatusResponse>('/api/scan/status.json');
  }

  async triggerScan(force = false): Promise<ScanStatusResponse> {
    const query = force ? '?force=true' : '';
    return this.fetch<ScanStatusResponse>(`/api/scan.json${query}`, { method: 'POST' });
  }

  async cleanupTranscodingCache(): Promise<CacheCleanupResponse> {
    return this.fetch<CacheCleanupResponse>('/api/cache/transcoding/cleanup.json', { method: 'POST' });
  }

  getSongStreamUrl(songId: string, quality: StreamingQuality): string {
    const params = new URLSearchParams();

    if (quality.format !== 'raw') {
      params.set('format', quality.format);
      if (quality.maxBitRate) {
        params.set('maxBitRate', quality.maxBitRate.toString());
      }
    }

    const queryString = params.toString();
    const url = `${this.baseUrl}/api/songs/${encodeURIComponent(songId)}/data${queryString ? `?${queryString}` : ''}`;
    return url;
  }

  getSongCoverUrl(songId: string, size?: number): string {
    const params = size ? `?size=${size}` : '';
    return `${this.baseUrl}/api/songs/${encodeURIComponent(songId)}/cover${params}`;
  }

  getAuthHeaders(): HeadersInit {
    return {};
  }

  getCoverHeaders(): HeadersInit {
    return {
      'Accept': ApiService.coverAcceptHeader
    };
  }

  async fetchSongBlob(songId: string, quality: StreamingQuality): Promise<Blob> {
    const url = this.getSongStreamUrl(songId, quality);
    const response = await fetch(url);

    if (!response.ok) {
      throw new Error(`Failed to fetch song: HTTP ${response.status}`);
    }

    return response.blob();
  }

  async testConnection(): Promise<boolean> {
    try {
      await this.getPlaylists();
      return true;
    } catch {
      return false;
    }
  }

  async getSongLyrics(songId: string): Promise<LyricsResponse> {
    return this.fetch<LyricsResponse>(`/api/songs/${encodeURIComponent(songId)}/lyrics.json`);
  }
}

// Singleton instance
let apiServiceInstance: ApiService | null = null;

export function getApiService(): ApiService {
  if (!apiServiceInstance) {
    apiServiceInstance = new ApiService('');
  }
  return apiServiceInstance;
}

export function initApiService(baseUrl: string): ApiService {
  if (!apiServiceInstance) {
    apiServiceInstance = new ApiService(baseUrl);
  } else {
    apiServiceInstance.updateConfig(baseUrl);
  }
  return apiServiceInstance;
}
