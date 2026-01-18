import type {
  PlaylistsResponse,
  PlaylistTracksResponse,
  ScanStatusResponse,
  StreamingQuality,
  CreatePlaylistRequest,
  UpdatePlaylistRequest,
  LyricsResponse
} from '../types';

export class ApiService {
  private baseUrl: string;
  private authToken: string;

  constructor(baseUrl: string, authToken: string) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.authToken = authToken;
  }

  updateConfig(baseUrl: string, authToken: string): void {
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.authToken = authToken;
  }

  private async fetch<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
    const url = `${this.baseUrl}${endpoint}`;
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 30000); // 30s timeout

    try {
      const response = await fetch(url, {
        ...options,
        signal: controller.signal,
        headers: {
          'Authorization': `Bearer ${this.authToken}`,
          ...options.headers
        }
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

  async updatePlaylist(playlistId: string, request: UpdatePlaylistRequest): Promise<PlaylistTracksResponse> {
    return this.fetch<PlaylistTracksResponse>(`/api/playlists/${encodeURIComponent(playlistId)}.json`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(request)
    });
  }

  async createPlaylist(request: CreatePlaylistRequest): Promise<PlaylistTracksResponse> {
    return this.fetch<PlaylistTracksResponse>('/api/playlists.json', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(request)
    });
  }

  async deletePlaylist(playlistId: string): Promise<void> {
    const url = `${this.baseUrl}/api/playlists/${encodeURIComponent(playlistId)}.json`;
    const response = await fetch(url, {
      method: 'DELETE',
      headers: {
        'Authorization': `Bearer ${this.authToken}`
      }
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ error: response.statusText }));
      throw new Error(error.error || `HTTP ${response.status}`);
    }
  }

  async addTrackToPlaylist(playlistId: string, songId: string): Promise<PlaylistTracksResponse> {
    return this.fetch<PlaylistTracksResponse>(`/api/playlists/${encodeURIComponent(playlistId)}/tracks.json`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ songId })
    });
  }

  async removeTrackFromPlaylist(playlistId: string, trackIndex: number): Promise<PlaylistTracksResponse> {
    return this.fetch<PlaylistTracksResponse>(`/api/playlists/${encodeURIComponent(playlistId)}/tracks/${trackIndex}.json`, {
      method: 'DELETE'
    });
  }

  async getScanStatus(): Promise<ScanStatusResponse> {
    return this.fetch<ScanStatusResponse>('/api/scan/status.json');
  }

  async triggerScan(): Promise<ScanStatusResponse> {
    return this.fetch<ScanStatusResponse>('/api/scan.json', { method: 'POST' });
  }

  async scrobble(songId: string, submission: boolean): Promise<void> {
    await this.fetch('/api/scrobble.json', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ id: songId, submission })
    });
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
    return {
      'Authorization': `Bearer ${this.authToken}`
    };
  }

  async fetchSongBlob(songId: string, quality: StreamingQuality): Promise<Blob> {
    const url = this.getSongStreamUrl(songId, quality);
    const response = await fetch(url, {
      headers: this.getAuthHeaders()
    });

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
    apiServiceInstance = new ApiService('', '');
  }
  return apiServiceInstance;
}

export function initApiService(baseUrl: string, authToken: string): ApiService {
  if (!apiServiceInstance) {
    apiServiceInstance = new ApiService(baseUrl, authToken);
  } else {
    apiServiceInstance.updateConfig(baseUrl, authToken);
  }
  return apiServiceInstance;
}
