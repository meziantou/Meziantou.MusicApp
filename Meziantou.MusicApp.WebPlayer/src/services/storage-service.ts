import { openDB, type DBSchema, type IDBPDatabase } from 'idb';
import type {
  AppSettings,
  PlaybackState,
  CachedTrack,
  CachedPlaylist,
  PlaylistSummary,
  TrackInfo
} from '../types';

interface MusicPlayerDB extends DBSchema {
  settings: {
    key: string;
    value: AppSettings;
  };
  playbackState: {
    key: string;
    value: PlaybackState;
  };
  cachedTracks: {
    key: string;
    value: CachedTrack;
    indexes: {
      'by-playlist': string;
      'by-cached-at': number;
    };
  };
  cachedPlaylists: {
    key: string;
    value: CachedPlaylist;
  };
  coverArt: {
    key: string;
    value: {
      trackId: string;
      blob: Blob;
      cachedAt: number;
    };
  };
  offlinePlaylists: {
    key: string;
    value: {
      playlistId: string;
      enabledAt: number;
    };
  };
  pendingScrobbles: {
    key: number;
    value: {
      id?: number;
      trackId: string;
      submission: boolean;
      timestamp: number;
    };
  };
  missingCovers: {
    key: string;
    value: {
      trackId: string;
      timestamp: number;
    };
  };
}

const DB_NAME = 'meziantou-music-player';
const DB_VERSION = 4;

class StorageService {
  private db: IDBPDatabase<MusicPlayerDB> | null = null;
  private initPromise: Promise<IDBPDatabase<MusicPlayerDB>> | null = null;

  async init(): Promise<IDBPDatabase<MusicPlayerDB>> {
    console.log('[StorageService] init called, db:', !!this.db, 'initPromise:', !!this.initPromise);
    if (this.db) return this.db;
    if (this.initPromise) return this.initPromise;

    console.log('[StorageService] Creating new DB connection');
    this.initPromise = openDB<MusicPlayerDB>(DB_NAME, DB_VERSION, {
      upgrade(db) {
        console.log('[StorageService] Running upgrade, version:', db.version);
        // Settings store
        if (!db.objectStoreNames.contains('settings')) {
          db.createObjectStore('settings');
        }

        // Playback state store
        if (!db.objectStoreNames.contains('playbackState')) {
          db.createObjectStore('playbackState');
        }

        // Cached tracks store
        if (!db.objectStoreNames.contains('cachedTracks')) {
          const tracksStore = db.createObjectStore('cachedTracks', { keyPath: 'trackId' });
          tracksStore.createIndex('by-playlist', 'playlistId');
          tracksStore.createIndex('by-cached-at', 'cachedAt');
        }

        // Cached playlists store
        if (!db.objectStoreNames.contains('cachedPlaylists')) {
          db.createObjectStore('cachedPlaylists', { keyPath: 'playlist.id' });
        }

        // Cover art store
        if (!db.objectStoreNames.contains('coverArt')) {
          db.createObjectStore('coverArt', { keyPath: 'trackId' });
        }

        // Missing covers store
        if (!db.objectStoreNames.contains('missingCovers')) {
          db.createObjectStore('missingCovers', { keyPath: 'trackId' });
        }

        // Offline playlists store (tracks which playlists are marked for offline)
        if (!db.objectStoreNames.contains('offlinePlaylists')) {
          db.createObjectStore('offlinePlaylists', { keyPath: 'playlistId' });
        }

        // Pending scrobbles store
        if (!db.objectStoreNames.contains('pendingScrobbles')) {
          db.createObjectStore('pendingScrobbles', { keyPath: 'id', autoIncrement: true });
        }
      }
    });

    this.db = await this.initPromise;
    return this.db;
  }

  // Settings
  async getSettings(defaults: AppSettings): Promise<AppSettings> {
    const db = await this.init();
    const settings = await db.get('settings', 'main');
    
    if (!settings) return defaults;

    // Migration for wifiQuality -> normalQuality and cellularQuality -> lowDataQuality
    const migratedSettings = { ...settings } as any;
    if (migratedSettings.wifiQuality && !migratedSettings.normalQuality) {
      migratedSettings.normalQuality = migratedSettings.wifiQuality;
      delete migratedSettings.wifiQuality;
    }
    if (migratedSettings.cellularQuality && !migratedSettings.lowDataQuality) {
      migratedSettings.lowDataQuality = migratedSettings.cellularQuality;
      delete migratedSettings.cellularQuality;
    }

    return { ...defaults, ...migratedSettings };
  }

  async saveSettings(settings: AppSettings): Promise<void> {
    console.log('[StorageService] saveSettings called with:', settings);
    const db = await this.init();
    console.log('[StorageService] DB initialized:', db.name, 'version:', db.version);
    await db.put('settings', settings, 'main');
    console.log('[StorageService] Settings saved successfully');
  }

  // Playback State
  async getPlaybackState(defaults: PlaybackState): Promise<PlaybackState> {
    const db = await this.init();
    const state = await db.get('playbackState', 'main');
    return state ?? defaults;
  }

  async savePlaybackState(state: PlaybackState): Promise<void> {
    const db = await this.init();
    await db.put('playbackState', state, 'main');
  }

  // Cached Tracks
  async getCachedTrack(trackId: string): Promise<CachedTrack | undefined> {
    const db = await this.init();
    return db.get('cachedTracks', trackId);
  }

  async saveCachedTrack(track: CachedTrack): Promise<void> {
    const db = await this.init();
    await db.put('cachedTracks', track);
  }

  async deleteCachedTrack(trackId: string): Promise<void> {
    const db = await this.init();
    await db.delete('cachedTracks', trackId);
  }

  async clearCachedTracks(): Promise<void> {
    const db = await this.init();
    await db.clear('cachedTracks');
  }

  async getCachedTrackIds(): Promise<Set<string>> {
    const db = await this.init();
    const keys = await db.getAllKeys('cachedTracks');
    return new Set(keys);
  }

  async getCachedTracksByPlaylist(playlistId: string): Promise<CachedTrack[]> {
    const db = await this.init();
    return db.getAllFromIndex('cachedTracks', 'by-playlist', playlistId);
  }

  async clearOldCachedTracks(maxAge: number): Promise<void> {
    const db = await this.init();
    const cutoff = Date.now() - maxAge;
    const tx = db.transaction('cachedTracks', 'readwrite');
    const index = tx.store.index('by-cached-at');
    
    let cursor = await index.openCursor(IDBKeyRange.upperBound(cutoff));
    while (cursor) {
      await cursor.delete();
      cursor = await cursor.continue();
    }
    
    await tx.done;
  }

  // Cached Playlists
  async getCachedPlaylist(playlistId: string): Promise<CachedPlaylist | undefined> {
    const db = await this.init();
    return db.get('cachedPlaylists', playlistId);
  }

  async saveCachedPlaylist(playlist: PlaylistSummary, tracks: TrackInfo[]): Promise<void> {
    const db = await this.init();
    await db.put('cachedPlaylists', {
      playlist,
      tracks,
      lastUpdated: Date.now()
    });
  }

  async getAllCachedPlaylists(): Promise<CachedPlaylist[]> {
    const db = await this.init();
    return db.getAll('cachedPlaylists');
  }

  async deleteCachedPlaylist(playlistId: string): Promise<void> {
    const db = await this.init();
    await db.delete('cachedPlaylists', playlistId);
  }

  // Cover Art
  async getCachedCover(trackId: string): Promise<Blob | undefined> {
    const db = await this.init();
    const entry = await db.get('coverArt', trackId);
    return entry?.blob;
  }

  async saveCachedCover(trackId: string, blob: Blob): Promise<void> {
    const db = await this.init();
    await db.put('coverArt', {
      trackId,
      blob,
      cachedAt: Date.now()
    });
  }

  // Offline Playlists (marking playlists for offline caching)
  async getOfflinePlaylistIds(): Promise<Set<string>> {
    const db = await this.init();
    const entries = await db.getAll('offlinePlaylists');
    return new Set(entries.map(e => e.playlistId));
  }

  async setPlaylistOffline(playlistId: string, enabled: boolean): Promise<void> {
    const db = await this.init();
    if (enabled) {
      await db.put('offlinePlaylists', { playlistId, enabledAt: Date.now() });
    } else {
      await db.delete('offlinePlaylists', playlistId);
    }
  }

  async isPlaylistOffline(playlistId: string): Promise<boolean> {
    const db = await this.init();
    const entry = await db.get('offlinePlaylists', playlistId);
    return entry !== undefined;
  }

  // Pending scrobbles
  async addPendingScrobble(trackId: string, submission: boolean): Promise<void> {
    const db = await this.init();
    await db.add('pendingScrobbles', {
      trackId,
      submission,
      timestamp: Date.now()
    });
  }

  async getPendingScrobbles(): Promise<{ id: number; trackId: string; submission: boolean; timestamp: number }[]> {
    const db = await this.init();
    // Cast to any because the type definition in IDB library might not infer the auto-increment key correctly in getAll
    return (await db.getAll('pendingScrobbles')) as any[];
  }

  async removePendingScrobble(id: number): Promise<void> {
    const db = await this.init();
    await db.delete('pendingScrobbles', id);
  }

  async addMissingCover(trackId: string): Promise<void> {
    const db = await this.init();
    await db.put('missingCovers', { trackId, timestamp: Date.now() });
  }

  async isCoverMissing(trackId: string): Promise<boolean> {
    const db = await this.init();
    const entry = await db.get('missingCovers', trackId);
    return entry !== undefined;
  }

  async clearCovers(): Promise<void> {
    const db = await this.init();
    await Promise.all([
      db.clear('coverArt'),
      db.clear('missingCovers')
    ]);
  }

  // Clear all data
  async clearAll(): Promise<void> {
    const db = await this.init();
    await Promise.all([
      db.clear('cachedTracks'),
      db.clear('cachedPlaylists'),
      db.clear('coverArt'),
      db.clear('missingCovers')
    ]);
  }

  // Get storage usage estimate
  async getStorageEstimate(): Promise<{ usage: number; quota: number } | null> {
    if ('storage' in navigator && 'estimate' in navigator.storage) {
      const estimate = await navigator.storage.estimate();
      return {
        usage: estimate.usage ?? 0,
        quota: estimate.quota ?? 0
      };
    }
    return null;
  }
}

// Singleton instance
export const storageService = new StorageService();
