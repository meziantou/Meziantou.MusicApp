import { createContext, useContext, useState, useEffect, useCallback, useMemo, type ReactNode } from 'react';
import type {
  AppSettings,
  PlaylistSummary,
  TrackInfo,
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
  triggerLibraryScan: () => Promise<void>;
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
  const [isOnline, setIsOnline] = useState(navigator.onLine);
  const [networkType, setNetworkType] = useState(getNetworkType());
  const [cachedTrackIds, setCachedTrackIds] = useState<Set<string>>(new Set());
  const [offlinePlaylistIds, setOfflinePlaylistIds] = useState<Set<string>>(new Set());
  const [offlinePlaylistTracks, setOfflinePlaylistTracks] = useState<Map<string, string[]>>(new Map());
  const [loadingCount, setLoadingCount] = useState(0);
  const isLoading = loadingCount > 0;
  const [toasts, setToasts] = useState<ToastMessage[]>([]);
  const [isInitialized, setIsInitialized] = useState(false);
  const [playingPlaylistId, setPlayingPlaylistId] = useState<string | null>(null);

  const [playerState, playerActions] = useAudioPlayer();

  const setIsLoading = useCallback((loading: boolean) => {
    setLoadingCount(prev => Math.max(0, prev + (loading ? 1 : -1)));
  }, []);

  // Compute playlist download progress dynamically from track lists and cached IDs
  const playlistDownloadProgress = useMemo(() => {
    const progress = new Map<string, { cached: number; total: number }>();
    for (const [playlistId, trackIds] of offlinePlaylistTracks) {
      const cachedCount = trackIds.filter(id => cachedTrackIds.has(id)).length;
      progress.set(playlistId, { cached: cachedCount, total: trackIds.length });
    }
    return progress;
  }, [offlinePlaylistTracks, cachedTrackIds]);

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
        initApiService(loadedSettings.serverUrl, loadedSettings.authToken);

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

          // Set up track lists for offline playlists
          const offlinePls = await storageService.getOfflinePlaylistIds();
          const trackLists = new Map<string, string[]>();
          for (const cachedPl of cachedPlaylists) {
            if (offlinePls.has(cachedPl.playlist.id)) {
              trackLists.set(cachedPl.playlist.id, cachedPl.tracks.map(t => t.id));
            }
          }
          setOfflinePlaylistTracks(trackLists);
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
                 if (state.currentTrackIndex >= 0) {
                     await playerActions.playAtIndex(state.currentTrackIndex, state.isPlaying, state.currentTime);
                 }
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
        setCachedTrackIds(prev => new Set([...prev, progress.trackId]));
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
  }, [isOnline, settings.serverUrl, cachedTrackIds, currentPlaylistId]);

  // Sync playlists periodically
  useEffect(() => {
    if (!isInitialized || !settings.serverUrl) return;

    const interval = setInterval(() => {
      if (isOnline && !document.hidden) {
        syncPlaylistsInternal();
      }
    }, 5 * 60 * 1000);

    return () => clearInterval(interval);
  }, [isInitialized, settings.serverUrl, isOnline, cachedTrackIds, currentPlaylistId]);

  async function syncPlaylistsInternal() {
    if (!isOnline) return;

    try {
      const api = getApiService();
      const response = await api.getPlaylists();
      const sorted = response.playlists.sort((a, b) => a.sortOrder - b.sortOrder);
      setPlaylists(sorted);

      // Get current offline playlist IDs
      const currentOfflinePlaylists = await storageService.getOfflinePlaylistIds();

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

            // Update track list for progress calculation
            setOfflinePlaylistTracks(prev => {
              const next = new Map(prev);
              next.set(playlist.id, tracksResponse.tracks.map(t => t.id));
              return next;
            });

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
            // Playlist hasn't changed, but ensure track list is set for progress
            setOfflinePlaylistTracks(prev => {
              const next = new Map(prev);
              next.set(playlist.id, cached.tracks.map(t => t.id));
              return next;
            });

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
      showToast('Failed to load tracks', 'error');
      return [];
    }
  }

  const updateSettings = useCallback(async (newSettings: AppSettings) => {
    console.log('[useApp] updateSettings called with:', newSettings);
    const serverChanged = newSettings.serverUrl !== settings.serverUrl ||
      newSettings.authToken !== settings.authToken;

    setSettings(newSettings);
    console.log('[useApp] Calling storageService.saveSettings');
    await storageService.saveSettings(newSettings);
    console.log('[useApp] storageService.saveSettings completed');
    initApiService(newSettings.serverUrl, newSettings.authToken);

    playerActions.setReplayGainMode(newSettings.replayGainMode);
    playerActions.setReplayGainPreamp(newSettings.replayGainPreamp);
    playerActions.setScrobbleEnabled(newSettings.scrobbleEnabled);
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
    await playerActions.playTrack(_track, currentPlaylistId);
  }, [currentPlaylistId, currentPlaylistTracks, settings, playerActions]);

  const downloadTrack = useCallback(async (track: TrackInfo) => {
    if (!currentPlaylistId) return;
    await downloadService.queueDownload(track, currentPlaylistId, settings.downloadQuality);
    showToast(`Downloading "${track.title}"`);
  }, [currentPlaylistId, settings.downloadQuality, showToast]);

  const deleteDownloadedTrack = useCallback(async (track: TrackInfo) => {
    await downloadService.deleteTrack(track.id);
    setCachedTrackIds(prev => {
      const next = new Set(prev);
      next.delete(track.id);
      return next;
    });
    showToast(`Removed "${track.title}" from downloads`);
  }, [showToast]);

  const clearAllCachedTracks = useCallback(async () => {
    await storageService.clearCachedTracks();
    setCachedTrackIds(new Set());
    playerActions.setCachedTrackIds(new Set());
    showToast('All downloaded songs cleared');
  }, [playerActions, showToast]);

  const addTrackToPlaylist = useCallback(async (playlist: PlaylistSummary, trackId: string) => {
    if (!isOnline) {
      showToast('Cannot modify playlists while offline', 'error');
      return;
    }

    if (!settings.serverUrl) {
      showToast('Server is not configured', 'error');
      return;
    }

    if (playlist.id.startsWith('virtual:')) {
      showToast('Cannot add tracks to this playlist', 'error');
      return;
    }

    setIsLoading(true);
    try {
      const api = getApiService();
      const existing = await api.getPlaylistTracks(playlist.id);

      const isAlreadyInPlaylist = existing.tracks.some(t => t.id === trackId);
      if (isAlreadyInPlaylist) {
        const shouldAddAgain = window.confirm('This track is already in this playlist. Add it again?');
        if (!shouldAddAgain) {
          return;
        }
      }

      const updated = await api.updatePlaylist(playlist.id, {
        songIds: [...existing.tracks.map(t => t.id), trackId]
      });

      setPlaylists(prev => prev.map(p =>
        p.id === playlist.id
          ? { ...p, name: updated.name, trackCount: updated.trackCount, duration: updated.duration, changed: updated.changed }
          : p
      ));

      const updatedSummary = playlists.find(p => p.id === playlist.id);
      if (updatedSummary) {
        await storageService.saveCachedPlaylist(updatedSummary, updated.tracks);
      }

      if (currentPlaylistId === playlist.id) {
        setCurrentPlaylistTracks(updated.tracks);
      }

      // If this playlist is marked for offline, update track list and download the new track
      if (offlinePlaylistIds.has(playlist.id)) {
        setOfflinePlaylistTracks(prev => {
          const next = new Map(prev);
          next.set(playlist.id, updated.tracks.map(t => t.id));
          return next;
        });

        // Download the new track if not already cached
        if (!cachedTrackIds.has(trackId)) {
          const newTrack = updated.tracks.find(t => t.id === trackId);
          if (newTrack) {
            await downloadService.queueDownload(newTrack, playlist.id, settings.downloadQuality);
          }
        }
      }

      showToast('Added track to playlist', 'success');
    } catch (error) {
      console.error('Failed to add track to playlist:', error);
      showToast('Failed to add track to playlist', 'error');
    } finally {
      setIsLoading(false);
    }
  }, [isOnline, settings.serverUrl, settings.downloadQuality, playlists, currentPlaylistId, offlinePlaylistIds, cachedTrackIds, showToast]);

  const removeTrackFromPlaylist = useCallback(async (playlistId: string, trackIndex: number) => {
    if (!isOnline) {
      showToast('Cannot modify playlists while offline', 'error');
      return;
    }

    if (!settings.serverUrl) {
      showToast('Server is not configured', 'error');
      return;
    }

    if (playlistId.startsWith('virtual:')) {
      showToast('Cannot remove tracks from this playlist', 'error');
      return;
    }

    setIsLoading(true);
    try {
      const api = getApiService();
      
      let tracks = currentPlaylistTracks;
      if (currentPlaylistId !== playlistId) {
         const response = await api.getPlaylistTracks(playlistId);
         tracks = response.tracks;
      }

      if (trackIndex < 0 || trackIndex >= tracks.length) {
          showToast('Invalid track index', 'error');
          return;
      }

      const newTracks = [...tracks];
      newTracks.splice(trackIndex, 1);
      const songIds = newTracks.map(t => t.id);

      const updated = await api.updatePlaylist(playlistId, {
        songIds: songIds
      });

      setPlaylists(prev => prev.map(p =>
        p.id === playlistId
          ? { ...p, name: updated.name, trackCount: updated.trackCount, duration: updated.duration, changed: updated.changed }
          : p
      ));

      const updatedSummary = playlists.find(p => p.id === playlistId);
      if (updatedSummary) {
        await storageService.saveCachedPlaylist(updatedSummary, updated.tracks);
      }

      if (currentPlaylistId === playlistId) {
        setCurrentPlaylistTracks(updated.tracks);
      }
      
      if (offlinePlaylistIds.has(playlistId)) {
        setOfflinePlaylistTracks(prev => {
          const next = new Map(prev);
          next.set(playlistId, updated.tracks.map(t => t.id));
          return next;
        });
      }

      showToast('Removed track from playlist', 'success');
    } catch (error) {
      console.error('Failed to remove track from playlist:', error);
      showToast('Failed to remove track from playlist', 'error');
    } finally {
      setIsLoading(false);
    }
  }, [isOnline, settings.serverUrl, currentPlaylistId, currentPlaylistTracks, playlists, offlinePlaylistIds, showToast]);

  const testConnection = useCallback(async () => {
    try {
      const api = getApiService();
      api.updateConfig(settings.serverUrl, settings.authToken);
      return await api.testConnection();
    } catch {
      return false;
    }
  }, [settings]);

  const triggerLibraryScan = useCallback(async () => {
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
      await api.triggerScan();
      showToast('Library scan started', 'success');
    } catch (error) {
      console.error('Failed to trigger library scan:', error);
      showToast('Failed to trigger library scan', 'error');
    }
  }, [isOnline, settings.serverUrl, showToast]);

  const syncPlaylists = useCallback(async () => {
    await syncPlaylistsInternal();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOnline]);

  const createPlaylist = useCallback(async (name: string): Promise<PlaylistSummary | null> => {
    if (!isOnline) {
      showToast('Cannot create playlists while offline', 'error');
      return null;
    }

    if (!settings.serverUrl) {
      showToast('Server is not configured', 'error');
      return null;
    }

    const trimmedName = name.trim();
    if (!trimmedName) {
      showToast('Playlist name is required', 'error');
      return null;
    }

    setIsLoading(true);
    try {
      const api = getApiService();
      const response = await api.createPlaylist({ name: trimmedName });

      const newPlaylist: PlaylistSummary = {
        id: response.id,
        name: response.name,
        trackCount: response.trackCount,
        duration: response.duration,
        created: response.created,
        changed: response.changed,
        sortOrder: playlists.length,
      };

      // Refetch playlists to get correct sort order from server
      await syncPlaylistsInternal();

      showToast(`Created playlist "${trimmedName}"`, 'success');
      return newPlaylist;
    } catch (error) {
      console.error('Failed to create playlist:', error);
      showToast('Failed to create playlist', 'error');
      return null;
    } finally {
      setIsLoading(false);
    }
  }, [isOnline, settings.serverUrl, playlists.length, showToast]);

  const deletePlaylist = useCallback(async (playlistId: string): Promise<boolean> => {
    if (!isOnline) {
      showToast('Cannot delete playlists while offline', 'error');
      return false;
    }

    if (!settings.serverUrl) {
      showToast('Server is not configured', 'error');
      return false;
    }

    if (playlistId.startsWith('virtual:')) {
      showToast('Cannot delete this playlist', 'error');
      return false;
    }

    const playlist = playlists.find(p => p.id === playlistId);
    if (!playlist) {
      showToast('Playlist not found', 'error');
      return false;
    }

    const confirmed = window.confirm(`Are you sure you want to delete "${playlist.name}"?`);
    if (!confirmed) {
      return false;
    }

    setIsLoading(true);
    try {
      const api = getApiService();
      await api.deletePlaylist(playlistId);

      // If this playlist was marked for offline, clean up
      if (offlinePlaylistIds.has(playlistId)) {
        downloadService.cancelPlaylistDownloads(playlistId);
        await storageService.setPlaylistOffline(playlistId, false);
        await downloadService.deletePlaylistTracks(playlistId);
        setOfflinePlaylistIds(prev => {
          const next = new Set(prev);
          next.delete(playlistId);
          return next;
        });
        setOfflinePlaylistTracks(prev => {
          const next = new Map(prev);
          next.delete(playlistId);
          return next;
        });
      }

      // Remove from cached playlists
      await storageService.deleteCachedPlaylist(playlistId);
      
      // Cleanup orphaned tracks
      await storageService.cleanupOrphanedTracks();

      // Update local state
      setPlaylists(prev => prev.filter(p => p.id !== playlistId));

      // If this was the current playlist, clear selection
      if (currentPlaylistId === playlistId) {
        setCurrentPlaylistId(null);
        setCurrentPlaylistTracks([]);
      }

      // If this was the playing playlist, stop playback
      if (playingPlaylistId === playlistId) {
        playerActions.pause();
        setPlayingPlaylistId(null);
      }

      showToast(`Deleted playlist "${playlist.name}"`, 'success');
      return true;
    } catch (error) {
      console.error('Failed to delete playlist:', error);
      showToast('Failed to delete playlist', 'error');
      return false;
    } finally {
      setIsLoading(false);
    }
  }, [isOnline, settings.serverUrl, playlists, currentPlaylistId, playingPlaylistId, offlinePlaylistIds, playerActions, showToast]);

  // Helper to update offline playlist track list
  const setPlaylistTrackList = useCallback((playlistId: string, tracks: TrackInfo[]) => {
    setOfflinePlaylistTracks(prev => {
      const next = new Map(prev);
      next.set(playlistId, tracks.map(t => t.id));
      return next;
    });
  }, []);

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

    // Set track list for progress calculation
    setPlaylistTrackList(playlistId, tracks);

    // Queue all tracks for download
    const uncachedTracks = tracks.filter(t => !cachedTrackIds.has(t.id));
    if (uncachedTracks.length === 0) {
      showToast('Playlist already cached', 'success');
      return;
    }

    showToast(`Downloading ${uncachedTracks.length} tracks...`);
    await downloadService.queuePlaylistDownload(uncachedTracks, playlistId, settings.downloadQuality);
  }, [isOnline, playlists, cachedTrackIds, settings.downloadQuality, showToast, setPlaylistTrackList]);

  // Stop caching a playlist and optionally delete cached tracks
  const stopPlaylistCaching = useCallback(async (playlistId: string) => {
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

    // Remove from track list tracking
    setOfflinePlaylistTracks(prev => {
      const next = new Map(prev);
      next.delete(playlistId);
      return next;
    });

    // Cleanup orphaned tracks
    await storageService.cleanupOrphanedTracks();

    showToast('Removed offline playlist');
  }, [showToast]);

  const value: AppContextValue = {
    settings,
    updateSettings,
    playlists,
    currentPlaylistId,
    currentPlaylistTracks,
    selectPlaylist,
    syncPlaylists,
    createPlaylist,
    deletePlaylist,
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
  };

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
