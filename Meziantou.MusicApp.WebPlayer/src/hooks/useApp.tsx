import { createContext, useContext, useState, useEffect, useCallback, useMemo, type ReactNode } from 'react';
import type {
  AppSettings,
  PlaylistSummary,
  TrackInfo,
  InvalidPlaylistInfo,
} from '../types';
import { DEFAULT_SETTINGS, DEFAULT_PLAYBACK_STATE } from '../constants';
import {
  initApiService,
  getApiService,
  storageService,
  audioPlayer,
  downloadService,
} from '../services';
import { getNetworkType } from '../utils';
import { useAudioPlayer, type AudioPlayerState, type AudioPlayerActions } from './useAudioPlayer';

interface AppContextValue {
  // Settings
  settings: AppSettings;
  updateSettings: (settings: AppSettings) => Promise<void>;

  // Playlists
  playlists: PlaylistSummary[];
  currentPlaylistId: string | null;
  currentPlaylistTracks: TrackInfo[];
  selectPlaylist: (playlist: PlaylistSummary) => Promise<void>;
  syncPlaylists: () => Promise<void>;
  createPlaylist: (name: string) => Promise<PlaylistSummary | null>;
  deletePlaylist: (playlistId: string) => Promise<boolean>;
  invalidPlaylists: InvalidPlaylistInfo[];

  // Network status
  isOnline: boolean;
  networkType: 'normal' | 'low-data' | 'unknown';

  // Cached tracks
  cachedTrackIds: Set<string>;
  downloadTrack: (track: TrackInfo) => Promise<void>;
  deleteDownloadedTrack: (track: TrackInfo) => Promise<void>;
  clearAllCachedTracks: () => Promise<void>;

  // Offline playlists
  offlinePlaylistIds: Set<string>;
  playlistDownloadProgress: Map<string, { cached: number; total: number }>;
  startPlaylistCaching: (playlistId: string) => Promise<void>;
  stopPlaylistCaching: (playlistId: string) => Promise<void>;

  // Audio player
  playerState: AudioPlayerState;
  playerActions: AudioPlayerActions;

  // UI state
  isLoading: boolean;
  isInitialized: boolean;
  showToast: (message: string, type?: 'info' | 'error' | 'success') => void;

  // Playback
  playTrack: (track: TrackInfo, index: number, tracks?: TrackInfo[]) => Promise<void>;
  addTrackToPlaylist: (playlist: PlaylistSummary, trackId: string) => Promise<void>;
  removeTrackFromPlaylist: (playlistId: string, trackIndex: number) => Promise<void>;

  // Test connection
  testConnection: () => Promise<boolean>;

  // Playing state
  playingPlaylistId: string | null;

  // Library scan
  triggerLibraryScan: (force?: boolean) => Promise<void>;
}

const AppContext = createContext<AppContextValue | null>(null);

interface ToastMessage {
  id: number;
  message: string;
  type: 'info' | 'error' | 'success';
}

interface AppProviderProps {
  children: ReactNode;
}

export function AppProvider({ children }: AppProviderProps) {
  const [settings, setSettings] = useState<AppSettings>(DEFAULT_SETTINGS);
  const [playlists, setPlaylists] = useState<PlaylistSummary[]>([]);
  const [currentPlaylistId, setCurrentPlaylistId] = useState<string | null>(null);
  const [currentPlaylistTracks, setCurrentPlaylistTracks] = useState<TrackInfo[]>([]);
  const [invalidPlaylists, setInvalidPlaylists] = useState<InvalidPlaylistInfo[]>([]);
  const [isOnline, setIsOnline] = useState(navigator.onLine);
  const [networkType, setNetworkType] = useState(getNetworkType());
  const [cachedTrackIds, setCachedTrackIds] = useState<Set<string>>(new Set());
  const [offlinePlaylistIds, setOfflinePlaylistIds] = useState<Set<string>>(new Set());
  // Progress is stored directly as {cached,total} so we never have to keep the
  // full track-id list of every offline playlist in React state. Initial values
  // are computed once when the offline playlist is loaded; subsequent download
  // completions bump `cached` incrementally via the downloadService.onProgress
  // handler below.
  const [playlistDownloadProgress, setPlaylistDownloadProgress] =
    useState<Map<string, { cached: number; total: number }>>(new Map());
  const [loadingCount, setLoadingCount] = useState(0);
  const isLoading = loadingCount > 0;
  const [toasts, setToasts] = useState<ToastMessage[]>([]);
  const [isInitialized, setIsInitialized] = useState(false);
  const [playingPlaylistId, setPlayingPlaylistId] = useState<string | null>(null);

  const [playerState, playerActions] = useAudioPlayer();

  const setIsLoading = useCallback((loading: boolean) => {
    setLoadingCount(prev => Math.max(0, prev + (loading ? 1 : -1)));
  }, []);

  const showToast = useCallback((message: string, type: 'info' | 'error' | 'success' = 'info') => {
    const id = Date.now();
    setToasts(prev => [...prev, { id, message, type }]);
    setTimeout(() => {
      setToasts(prev => prev.filter(t => t.id !== id));
    }, 3000);
  }, []);

  // Sync cached tracks to player
  useEffect(() => {
    playerActions.setCachedTrackIds(cachedTrackIds);
  }, [cachedTrackIds, playerActions]);

  // Sync online status to player
  useEffect(() => {
    playerActions.setIsOnline(isOnline);
  }, [isOnline, playerActions]);

  // Initialize app
  useEffect(() => {
    async function init() {
      try {
        console.log('[useApp] Initializing...');
        await storageService.init();
        console.log('[useApp] Storage initialized');

        const loadedSettings = await storageService.getSettings(DEFAULT_SETTINGS);
        console.log('[useApp] Settings loaded:', loadedSettings);
        setSettings(loadedSettings);
        initApiService(loadedSettings.serverUrl);

        // Apply loaded settings to audio player
        playerActions.setReplayGainMode(loadedSettings.replayGainMode);
        playerActions.setReplayGainPreamp(loadedSettings.replayGainPreamp);
        playerActions.setPreventDownloadOnLowData(loadedSettings.preventDownloadOnLowData);

        const networkType = getNetworkType();
        playerActions.setNetworkType(networkType);
        const quality = networkType === 'low-data'
          ? loadedSettings.lowDataQuality
          : loadedSettings.normalQuality;
        playerActions.setQuality(quality);

        await downloadService.init();
        console.log('[useApp] Download service initialized');

        const cached = await storageService.getCachedTrackIds();
        setCachedTrackIds(cached);

        const offlinePlaylists = await storageService.getOfflinePlaylistIds();
        setOfflinePlaylistIds(offlinePlaylists);

        // Verify offline playlists integrity and clean up orphans
        const integrity = await storageService.verifyOfflinePlaylistsIntegrity();
        if (integrity.removed.length > 0) {
          console.log(`[useApp] Cleaned up ${integrity.removed.length} orphaned offline playlist entries`);
          const updatedOfflinePlaylists = await storageService.getOfflinePlaylistIds();
          setOfflinePlaylistIds(updatedOfflinePlaylists);
        }

        // Load recently played tracks for queue filtering
        await playerActions.loadRecentlyPlayed();
        console.log('[useApp] Recently played tracks loaded');

        setIsInitialized(true);
        console.log('[useApp] Initialization complete');
      } catch (error) {
        console.error('[useApp] Initialization failed:', error);
        // Even if initialization fails, we should probably mark as initialized so the UI can render (e.g. settings dialog)
        // But maybe with default settings?
        setIsInitialized(true);
      }
    }
    init();
  }, [playerActions]);

  // Load initial data after initialization
  useEffect(() => {
    if (!isInitialized || !settings.serverUrl) return;

    async function loadData() {
      setIsLoading(true);
      try {
        let currentPlaylists: PlaylistSummary[] = [];
        if (isOnline) {
          currentPlaylists = (await syncPlaylistsInternal()) || [];
        } else {
          const cachedPlaylists = await storageService.getAllCachedPlaylists();
          currentPlaylists = cachedPlaylists.map(cp => cp.playlist).sort((a, b) => a.sortOrder - b.sortOrder);
          setPlaylists(currentPlaylists);

          // Set up download progress for offline playlists.
          // Computing once here avoids keeping the full track-id list in React state.
          const offlinePls = await storageService.getOfflinePlaylistIds();
          const cached = await storageService.getCachedTrackIds();
          const progressMap = new Map<string, { cached: number; total: number }>();
          for (const cachedPl of cachedPlaylists) {
            if (offlinePls.has(cachedPl.playlist.id)) {
              let cachedCount = 0;
              for (const t of cachedPl.tracks) {
                if (cached.has(t.id)) cachedCount++;
              }
              progressMap.set(cachedPl.playlist.id, { cached: cachedCount, total: cachedPl.tracks.length });
            }
          }
          setPlaylistDownloadProgress(progressMap);
        }

        // Restore playback state
        const state = await storageService.getPlaybackState(DEFAULT_PLAYBACK_STATE);
        await playerActions.restoreState(state);

        // Restore last viewed playlist
        const lastViewedId = localStorage.getItem('lastViewedPlaylistId');
        let viewedId = lastViewedId;

        // If no last viewed, fallback to playing playlist
        if (!viewedId && state.currentPlaylistId) {
            viewedId = state.currentPlaylistId;
        }

        // If still nothing, maybe first playlist?
        if (!viewedId && currentPlaylists.length > 0) {
            viewedId = currentPlaylists[0].id;
        }

        let viewedTracks: TrackInfo[] = [];
        if (viewedId) {
             const playlist = currentPlaylists.find(p => p.id === viewedId);
             if (playlist) {
                 setCurrentPlaylistId(viewedId);
                 viewedTracks = await loadPlaylistTracksInternal(viewedId, currentPlaylists);
             }
        }

        // Restore playing playlist tracks
        if (state.currentPlaylistId) {
             let tracks: TrackInfo[] = [];

             if (state.currentPlaylistId === viewedId) {
                 tracks = viewedTracks;
             } else {
                 // We need to fetch tracks for playing playlist separately
                 const api = getApiService();
                 try {
                     if (isOnline) {
                         const response = await api.getPlaylistTracks(state.currentPlaylistId);
                         tracks = response.tracks;
                     } else {
                         const cached = await storageService.getCachedPlaylist(state.currentPlaylistId);
                         if (cached) tracks = cached.tracks;
                     }
                 } catch (e) {
                     console.error("Failed to load playing playlist tracks", e);
                 }
             }

             if (tracks.length > 0) {
                 setPlayingPlaylistId(state.currentPlaylistId);
                 playerActions.setPlaylist(state.currentPlaylistId, tracks, state.shuffleOrder);

                 // Find the correct track index using the track ID for more reliable restoration
                 let trackIndex = state.currentTrackIndex;
                 if (state.currentTrackId) {
                   const foundIndex = tracks.findIndex(t => t.id === state.currentTrackId);
                   if (foundIndex >= 0) {
                     // If we're in shuffle mode, find where this track is in the shuffle order
                     if (state.shuffleEnabled && state.shuffleOrder && state.shuffleOrder.length === tracks.length) {
                       const shuffleIndex = state.shuffleOrder.indexOf(foundIndex);
                       if (shuffleIndex >= 0) {
                         trackIndex = shuffleIndex;
                       } else {
                         // Track not in shuffle order, use found index
                         trackIndex = foundIndex;
                       }
                     } else {
                       trackIndex = foundIndex;
                     }
                   }
                 }

                 if (trackIndex >= 0 && trackIndex < tracks.length) {
                     await playerActions.playAtIndex(trackIndex, state.isPlaying, state.currentTime);
                 }
             }
        }

        // Check for invalid playlists after initial load
        if (isOnline) {
          try {
            const api = getApiService();
            const status = await api.getScanStatus();
            if (status.invalidPlaylists && status.invalidPlaylists.length > 0) {
              setInvalidPlaylists(status.invalidPlaylists);
            }
          } catch (error) {
            console.error('Failed to check for invalid playlists:', error);
          }
        }
      } catch (error) {
        console.error('Failed to load initial data:', error);
        showToast('Failed to load playlists', 'error');
      } finally {
        setIsLoading(false);
      }
    }
    loadData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isInitialized, settings.serverUrl]);

  // Online/offline handlers
  useEffect(() => {
    const handleOnline = () => {
      setIsOnline(true);
      showToast('Back online');
      syncPlaylistsInternal();
    };

    const handleOffline = () => {
      setIsOnline(false);
      showToast('You are offline');
    };

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);

    // Network type listener
    const connection = (navigator as any).connection;
    const handleConnectionChange = () => {
      const type = getNetworkType();
      setNetworkType(type);
      playerActions.setNetworkType(type);
    };

    if (connection) {
      connection.addEventListener('change', handleConnectionChange);
    }

    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
      if (connection) {
        connection.removeEventListener('change', handleConnectionChange);
      }
    };
  }, [showToast]);

  // Download progress handler
  useEffect(() => {
    const unsubscribe = downloadService.onProgress((progress) => {
      if (progress.status === 'complete') {
        setCachedTrackIds(prev => {
          if (prev.has(progress.trackId)) return prev;
          const next = new Set(prev);
          next.add(progress.trackId);
          return next;
        });
        if (progress.playlistIds && progress.playlistIds.length > 0) {
          setPlaylistDownloadProgress(prev => {
            let mutated = false;
            const next = new Map(prev);
            for (const pid of progress.playlistIds!) {
              const entry = next.get(pid);
              if (entry && entry.cached < entry.total) {
                next.set(pid, { cached: entry.cached + 1, total: entry.total });
                mutated = true;
              }
            }
            return mutated ? next : prev;
          });
        }
      } else if (progress.status === 'error') {
        showToast(`Download failed: ${progress.error}`, 'error');
      }
    });
    return unsubscribe;
  }, [showToast]);

  // Track change handler - update playing playlist
  useEffect(() => {
    const handler = () => {
      const playlistId = playerActions.getCurrentPlaylistId();
      setPlayingPlaylistId(playlistId);
    };
    audioPlayer.on('trackchange', handler);
    return () => audioPlayer.off('trackchange', handler);
  }, [playerActions]);

  // Refresh playlists when app becomes visible
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (!document.hidden && isOnline && settings.serverUrl) {
        syncPlaylistsInternal();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange);
  }, [isOnline, settings.serverUrl]);

  // Sync playlists periodically
  useEffect(() => {
    if (!isInitialized || !settings.serverUrl) return;

    const interval = setInterval(() => {
      if (isOnline && !document.hidden) {
        syncPlaylistsInternal();
      }
    }, 5 * 60 * 1000);

    return () => clearInterval(interval);
  }, [isInitialized, settings.serverUrl, isOnline]);

  async function syncPlaylistsInternal() {
    if (!isOnline) return;

    try {
      const api = getApiService();
      const response = await api.getPlaylists();
      const sorted = response.playlists.sort((a, b) => a.sortOrder - b.sortOrder);

      // Check if response is empty
      if (sorted.length === 0) {
        // Check if indexing is still in progress
        const scanStatus = await api.getScanStatus();
        if (!scanStatus.isInitialScanCompleted) {
          // Indexing not complete, keep cached data and retry later
          console.log('Playlists response is empty but indexing not complete, keeping cache');
          const cachedPlaylists = await storageService.getAllCachedPlaylists();
          return cachedPlaylists.map(cp => cp.playlist).sort((a, b) => a.sortOrder - b.sortOrder);
        }
      }

      setPlaylists(sorted);

      // Get current offline playlist IDs
      const currentOfflinePlaylists = await storageService.getOfflinePlaylistIds();

      // Remove playlists that no longer exist
      const cachedPlaylists = await storageService.getAllCachedPlaylists();
      const currentPlaylistIds = new Set(sorted.map(p => p.id));
      for (const cachedPlaylist of cachedPlaylists) {
        if (!currentPlaylistIds.has(cachedPlaylist.playlist.id)) {
          console.log(`Removing deleted playlist from cache: ${cachedPlaylist.playlist.id}`);
          await storageService.deleteCachedPlaylist(cachedPlaylist.playlist.id);

          // Remove from offline playlists if it was marked as offline
          if (currentOfflinePlaylists.has(cachedPlaylist.playlist.id)) {
            await storageService.setPlaylistOffline(cachedPlaylist.playlist.id, false);
            setOfflinePlaylistIds(prev => {
              const next = new Set(prev);
              next.delete(cachedPlaylist.playlist.id);
              return next;
            });
          }

          // Remove from offline playlist progress tracking
          setPlaylistDownloadProgress(prev => {
            if (!prev.has(cachedPlaylist.playlist.id)) return prev;
            const next = new Map(prev);
            next.delete(cachedPlaylist.playlist.id);
            return next;
          });
        }
      }

      // Cache playlists metadata and check for offline playlist updates
      for (const playlist of sorted) {
        const cached = await storageService.getCachedPlaylist(playlist.id);
        const needsUpdate = !cached || new Date(cached.playlist.changed) < new Date(playlist.changed);

        // If this playlist is marked for offline
        if (currentOfflinePlaylists.has(playlist.id)) {
          if (needsUpdate) {
            // Download any new tracks
            const tracksResponse = await api.getPlaylistTracks(playlist.id);
            await storageService.saveCachedPlaylist(playlist, tracksResponse.tracks);

            // Recompute download progress for this playlist
            {
              let cachedCount = 0;
              for (const t of tracksResponse.tracks) {
                if (cachedTrackIds.has(t.id)) cachedCount++;
              }
              const total = tracksResponse.tracks.length;
              setPlaylistDownloadProgress(prev => {
                const next = new Map(prev);
                next.set(playlist.id, { cached: cachedCount, total });
                return next;
              });
            }

            // Queue downloads for uncached tracks
            const uncachedTracks = tracksResponse.tracks.filter(t => !cachedTrackIds.has(t.id));
            if (uncachedTracks.length > 0) {
              await downloadService.queuePlaylistDownload(uncachedTracks, playlist.id, settings.downloadQuality);
            }

            // Remove tracks that are no longer in the playlist
            if (cached) {
              const newTrackIds = new Set(tracksResponse.tracks.map(t => t.id));
              const removedTracks = cached.tracks.filter(t => !newTrackIds.has(t.id));
              for (const track of removedTracks) {
                await storageService.removePlaylistFromTrack(track.id, playlist.id);
              }
            }

            if (currentPlaylistId === playlist.id) {
              setCurrentPlaylistTracks(tracksResponse.tracks);
            }
          } else if (cached) {
            // Playlist hasn't changed, but ensure progress is initialized.
            {
              let cachedCount = 0;
              for (const t of cached.tracks) {
                if (cachedTrackIds.has(t.id)) cachedCount++;
              }
              const total = cached.tracks.length;
              setPlaylistDownloadProgress(prev => {
                const existing = prev.get(playlist.id);
                if (existing && existing.cached === cachedCount && existing.total === total) return prev;
                const next = new Map(prev);
                next.set(playlist.id, { cached: cachedCount, total });
                return next;
              });
            }

            // Also check for uncached tracks (in case previous downloads were interrupted)
            const uncachedTracks = cached.tracks.filter(t => !cachedTrackIds.has(t.id));
            if (uncachedTracks.length > 0) {
              await downloadService.queuePlaylistDownload(uncachedTracks, playlist.id, settings.downloadQuality);
            }
          }
        } else if (needsUpdate && currentPlaylistId === playlist.id) {
          await loadPlaylistTracksInternal(playlist.id, undefined, true);
        }
      }
      return sorted;
    } catch (error) {
      console.error('Failed to sync playlists:', error);

      // On error, check if indexing is still in progress
      try {
        const api = getApiService();
        const scanStatus = await api.getScanStatus();
        if (!scanStatus.isInitialScanCompleted) {
          // Indexing not complete, keep cached data and retry later
          console.log('Failed to sync playlists but indexing not complete, keeping cache');
          const cachedPlaylists = await storageService.getAllCachedPlaylists();
          return cachedPlaylists.map(cp => cp.playlist).sort((a, b) => a.sortOrder - b.sortOrder);
        }
      } catch (scanError) {
        console.error('Failed to check scan status:', scanError);
        // If we can't reach the server at all, return cached playlists
        console.log('Server unreachable, returning cached playlists');
        const cachedPlaylists = await storageService.getAllCachedPlaylists();
        if (cachedPlaylists.length > 0) {
          showToast('Server unavailable, showing cached data', 'info');
          const sorted = cachedPlaylists.map(cp => cp.playlist).sort((a, b) => a.sortOrder - b.sortOrder);
          setPlaylists(sorted);
          return sorted;
        }
      }

      return [];
    }
  }

  async function loadPlaylistTracksInternal(playlistId: string, knownPlaylists?: PlaylistSummary[], forceRefresh = false) {
    try {
      let tracks: TrackInfo[] = [];

      // Try to load from cache first to show data immediately
      const cached = await storageService.getCachedPlaylist(playlistId);
      if (cached) {
        tracks = cached.tracks;
        setCurrentPlaylistTracks(tracks);

        if (!forceRefresh) {
          return tracks;
        }
      }

      if (isOnline) {
        const api = getApiService();
        const response = await api.getPlaylistTracks(playlistId);
        tracks = response.tracks;

        // Check if response is empty
        if (tracks.length === 0) {
          // Check if indexing is still in progress
          const scanStatus = await api.getScanStatus();
          if (!scanStatus.isInitialScanCompleted) {
            // Indexing not complete, keep cached data and retry later
            console.log('Playlist tracks response is empty but indexing not complete, keeping cache');
            if (cached) {
              return cached.tracks;
            }
            return [];
          }
        }

        setCurrentPlaylistTracks(tracks);

        const playlist = (knownPlaylists || playlists).find(p => p.id === playlistId);
        if (playlist) {
          await storageService.saveCachedPlaylist(playlist, response.tracks);
        }
      } else if (!cached) {
        setCurrentPlaylistTracks([]);
        showToast('Playlist not available offline', 'error');
      }
      return tracks;
    } catch (error) {
      console.error('Failed to load playlist tracks:', error);

      // On error, check if indexing is still in progress
      try {
        const api = getApiService();
        const scanStatus = await api.getScanStatus();
        if (!scanStatus.isInitialScanCompleted) {
          // Indexing not complete, keep cached data and retry later
          console.log('Failed to load playlist tracks but indexing not complete, keeping cache');
          const cached = await storageService.getCachedPlaylist(playlistId);
          if (cached) {
            setCurrentPlaylistTracks(cached.tracks);
            return cached.tracks;
          }
          return [];
        }
      } catch (scanError) {
        console.error('Failed to check scan status:', scanError);
        // If we can't reach the server at all, return cached tracks
        console.log('Server unreachable, returning cached tracks');
        const cached = await storageService.getCachedPlaylist(playlistId);
        if (cached) {
          showToast('Server unavailable, showing cached playlist', 'info');
          setCurrentPlaylistTracks(cached.tracks);
          return cached.tracks;
        }
      }

      showToast('Failed to load tracks', 'error');
      return [];
    }
  }

  const updateSettings = useCallback(async (newSettings: AppSettings) => {
    console.log('[useApp] updateSettings called with:', newSettings);
    const serverChanged = newSettings.serverUrl !== settings.serverUrl;

    setSettings(newSettings);
    console.log('[useApp] Calling storageService.saveSettings');
    await storageService.saveSettings(newSettings);
    console.log('[useApp] storageService.saveSettings completed');
    initApiService(newSettings.serverUrl);

    playerActions.setReplayGainMode(newSettings.replayGainMode);
    playerActions.setReplayGainPreamp(newSettings.replayGainPreamp);
    playerActions.setPreventDownloadOnLowData(newSettings.preventDownloadOnLowData);

    const networkType = getNetworkType();
    playerActions.setNetworkType(networkType);
    const quality = networkType === 'low-data'
      ? newSettings.lowDataQuality
      : newSettings.normalQuality;
    playerActions.setQuality(quality);

    showToast('Settings saved');

    const downloadQualityChanged =
      newSettings.downloadQuality.format !== settings.downloadQuality.format ||
      newSettings.downloadQuality.maxBitRate !== settings.downloadQuality.maxBitRate;

    if (downloadQualityChanged) {
      await storageService.clearCachedTracks();
      await downloadService.refreshCacheState();
      setCachedTrackIds(new Set());

      const offlineIds = await storageService.getOfflinePlaylistIds();
      let redownloadCount = 0;

      for (const playlistId of offlineIds) {
        const cachedPlaylist = await storageService.getCachedPlaylist(playlistId);
        if (cachedPlaylist && cachedPlaylist.tracks.length > 0) {
          await downloadService.queuePlaylistDownload(cachedPlaylist.tracks, playlistId, newSettings.downloadQuality);
          redownloadCount++;
        }
      }

      if (redownloadCount > 0) {
        showToast(`Redownloading ${redownloadCount} offline playlists`);
      }
    }

    if (serverChanged && newSettings.serverUrl) {
      setIsLoading(true);
      try {
        await syncPlaylistsInternal();
      } finally {
        setIsLoading(false);
      }
    }
  }, [settings, showToast, playerActions]);

  const selectPlaylist = useCallback(async (playlist: PlaylistSummary) => {
    setCurrentPlaylistId(playlist.id);
    localStorage.setItem('lastViewedPlaylistId', playlist.id);
    setIsLoading(true);
    try {
      await loadPlaylistTracksInternal(playlist.id);
    } finally {
      setIsLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOnline, playlists, showToast]);

  const playTrack = useCallback(async (_track: TrackInfo, _index: number, tracks?: TrackInfo[]) => {
    if (!currentPlaylistId) return;

    const networkType = getNetworkType();
    playerActions.setNetworkType(networkType);
    playerActions.setPreventDownloadOnLowData(settings.preventDownloadOnLowData);

    const quality = networkType === 'low-data'
      ? settings.lowDataQuality
      : settings.normalQuality;
    playerActions.setQuality(quality);

    playerActions.setPlaylist(currentPlaylistId, tracks || currentPlaylistTracks);
    setPlayingPlaylistId(currentPlaylistId);
    await playerActions.playTrack(_track);
  }, [currentPlaylistId, currentPlaylistTracks, settings, playerActions]);

  const downloadTrack = useCallback(async (track: TrackInfo) => {
    if (!currentPlaylistId) return;
    await downloadService.queueDownload(track, currentPlaylistId, settings.downloadQuality);
    showToast(`Downloading "${track.title}"`);
  }, [currentPlaylistId, settings.downloadQuality, showToast]);

  const deleteDownloadedTrack = useCallback(async (track: TrackInfo) => {
    // Capture playlistIds before deletion so we can decrement the per-playlist
    // progress counters for each affected offline playlist.
    const before = await storageService.getCachedTrack(track.id);
    const affectedPlaylistIds = before?.playlistIds ?? [];

    await downloadService.deleteTrack(track.id);
    setCachedTrackIds(prev => {
      if (!prev.has(track.id)) return prev;
      const next = new Set(prev);
      next.delete(track.id);
      return next;
    });
    if (affectedPlaylistIds.length > 0) {
      setPlaylistDownloadProgress(prev => {
        let mutated = false;
        const next = new Map(prev);
        for (const pid of affectedPlaylistIds) {
          const entry = next.get(pid);
          if (entry && entry.cached > 0) {
            next.set(pid, { cached: entry.cached - 1, total: entry.total });
            mutated = true;
          }
        }
        return mutated ? next : prev;
      });
    }
    showToast(`Removed "${track.title}" from downloads`);
  }, [showToast]);

  const clearAllCachedTracks = useCallback(async () => {
    await storageService.clearAll();
    setCachedTrackIds(new Set());
    playerActions.setCachedTrackIds(new Set());
    setOfflinePlaylistIds(new Set());
    setPlaylistDownloadProgress(new Map());
    downloadService.clearQueue();
    showToast('All cached data cleared');
  }, [playerActions, showToast]);

  const addTrackToPlaylist = useCallback(async (_playlist: PlaylistSummary, _trackId: string) => {
    showToast('Server is read-only');
  }, [showToast]);

  const removeTrackFromPlaylist = useCallback(async (_playlistId: string, _trackIndex: number) => {
    showToast('Server is read-only');
  }, [showToast]);

  const testConnection = useCallback(async () => {
    try {
      const api = getApiService();
      api.updateConfig(settings.serverUrl);
      return await api.testConnection();
    } catch {
      return false;
    }
  }, [settings]);

  const triggerLibraryScan = useCallback(async (force = false) => {
    if (!isOnline) {
      showToast('Cannot trigger scan while offline', 'error');
      return;
    }

    if (!settings.serverUrl) {
      showToast('Server is not configured', 'error');
      return;
    }

    try {
      const api = getApiService();
      await api.triggerScan(force);
      showToast(force ? 'Force library scan started' : 'Library scan started', 'success');

      // Check for invalid playlists after a short delay to allow scan to complete
      setTimeout(async () => {
        try {
          const status = await api.getScanStatus();
          if (status.invalidPlaylists) {
            setInvalidPlaylists(status.invalidPlaylists);
          }
        } catch (error) {
          console.error('Failed to check for invalid playlists:', error);
        }
      }, 3000);
    } catch (error) {
      console.error('Failed to trigger library scan:', error);
      showToast(force ? 'Failed to trigger force library scan' : 'Failed to trigger library scan', 'error');
    }
  }, [isOnline, settings.serverUrl, showToast]);

  const syncPlaylists = useCallback(async () => {
    await syncPlaylistsInternal();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOnline]);

  const createPlaylist = useCallback(async (_name: string): Promise<PlaylistSummary | null> => {
    showToast('Server is read-only');
    return null;
  }, [showToast]);

  const deletePlaylist = useCallback(async (_playlistId: string): Promise<boolean> => {
    showToast('Server is read-only');
    return false;
  }, [showToast]);

  // Start caching a playlist for offline use
  const startPlaylistCaching = useCallback(async (playlistId: string) => {
    if (!isOnline) {
      showToast('Cannot download while offline', 'error');
      return;
    }

    await storageService.setPlaylistOffline(playlistId, true);
    setOfflinePlaylistIds(prev => new Set([...prev, playlistId]));

    // Get playlist tracks
    const api = getApiService();
    let tracks: TrackInfo[];
    try {
      const response = await api.getPlaylistTracks(playlistId);
      tracks = response.tracks;

      // Save playlist metadata
      const playlist = playlists.find(p => p.id === playlistId);
      if (playlist) {
        await storageService.saveCachedPlaylist(playlist, tracks);
      }
    } catch (error) {
      console.error('Failed to get playlist tracks:', error);
      showToast('Failed to start download', 'error');
      return;
    }

    // Initialize progress for this playlist
    let cachedCount = 0;
    for (const t of tracks) {
      if (cachedTrackIds.has(t.id)) cachedCount++;
    }
    const total = tracks.length;
    setPlaylistDownloadProgress(prev => {
      const next = new Map(prev);
      next.set(playlistId, { cached: cachedCount, total });
      return next;
    });

    // Queue all tracks for download
    const uncachedTracks = tracks.filter(t => !cachedTrackIds.has(t.id));
    if (uncachedTracks.length === 0) {
      showToast('Playlist already cached', 'success');
      return;
    }

    showToast(`Downloading ${uncachedTracks.length} tracks...`);
    await downloadService.queuePlaylistDownload(uncachedTracks, playlistId, settings.downloadQuality);
  }, [isOnline, playlists, cachedTrackIds, settings.downloadQuality, showToast]);

  // Stop caching a playlist and optionally delete cached tracks
  const stopPlaylistCaching = useCallback(async (playlistId: string) => {
    const playlist = playlists.find(p => p.id === playlistId);
    const playlistName = playlist?.name ?? 'this playlist';
    const confirmed = window.confirm(`Are you sure you want to remove "${playlistName}" from offline cache?`);
    if (!confirmed) {
      return;
    }

    // Cancel pending downloads for this playlist
    downloadService.cancelPlaylistDownloads(playlistId);

    // Remove from offline playlists
    await storageService.setPlaylistOffline(playlistId, false);
    setOfflinePlaylistIds(prev => {
      const next = new Set(prev);
      next.delete(playlistId);
      return next;
    });

    // Delete cached tracks for this playlist
    await downloadService.deletePlaylistTracks(playlistId);

    // Update cachedTrackIds
    const cached = await storageService.getCachedTrackIds();
    setCachedTrackIds(cached);

    // Remove from progress tracking
    setPlaylistDownloadProgress(prev => {
      if (!prev.has(playlistId)) return prev;
      const next = new Map(prev);
      next.delete(playlistId);
      return next;
    });

    // Cleanup orphaned tracks
    await storageService.cleanupOrphanedTracks();

    showToast('Removed offline playlist');
  }, [playlists, showToast]);

  const value = useMemo<AppContextValue>(() => ({
    settings,
    updateSettings,
    playlists,
    currentPlaylistId,
    currentPlaylistTracks,
    selectPlaylist,
    syncPlaylists,
    createPlaylist,
    deletePlaylist,
    invalidPlaylists,
    isOnline,
    networkType,
    cachedTrackIds,
    downloadTrack,
    deleteDownloadedTrack,
    clearAllCachedTracks,
    offlinePlaylistIds,
    playlistDownloadProgress,
    startPlaylistCaching,
    stopPlaylistCaching,
    playerState,
    playerActions,
    isLoading,
    isInitialized,
    showToast,
    playTrack,
    addTrackToPlaylist,
    removeTrackFromPlaylist,
    testConnection,
    playingPlaylistId,
    triggerLibraryScan,
  }), [
    settings,
    updateSettings,
    playlists,
    currentPlaylistId,
    currentPlaylistTracks,
    selectPlaylist,
    syncPlaylists,
    createPlaylist,
    deletePlaylist,
    invalidPlaylists,
    isOnline,
    networkType,
    cachedTrackIds,
    downloadTrack,
    deleteDownloadedTrack,
    clearAllCachedTracks,
    offlinePlaylistIds,
    playlistDownloadProgress,
    startPlaylistCaching,
    stopPlaylistCaching,
    playerState,
    playerActions,
    isLoading,
    isInitialized,
    showToast,
    playTrack,
    addTrackToPlaylist,
    removeTrackFromPlaylist,
    testConnection,
    playingPlaylistId,
    triggerLibraryScan,
  ]);

  return (
    <AppContext.Provider value={value}>
      {children}
      {/* Toast container */}
      <div className="toast-container">
        {toasts.map(toast => (
          <div key={toast.id} className={`toast toast-${toast.type} show`}>
            {toast.message}
          </div>
        ))}
      </div>
    </AppContext.Provider>
  );
}

export function useApp() {
  const context = useContext(AppContext);
  if (!context) {
    throw new Error('useApp must be used within an AppProvider');
  }
  return context;
}
