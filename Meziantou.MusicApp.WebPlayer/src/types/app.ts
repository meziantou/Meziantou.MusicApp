import type { PlaylistSummary, TrackInfo } from './api';

// Application state types

export interface AppSettings {
  serverUrl: string;
  authToken: string;
  normalQuality: StreamingQuality;
  lowDataQuality: StreamingQuality;
  downloadQuality: StreamingQuality;
  preventDownloadOnLowData: boolean;
  scrobbleEnabled: boolean;
  hideCoverArt: boolean;
  replayGainMode: ReplayGainMode;
  replayGainPreamp: number; // in dB
  showReplayGainWarning: boolean;
}

export interface StreamingQuality {
  format: 'raw' | 'mp3' | 'opus' | 'ogg' | 'm4a' | 'flac';
  maxBitRate?: number;
}

export type ReplayGainMode = 'off' | 'track' | 'album';

export interface PlaybackState {
  currentPlaylistId: string | null;
  currentTrackIndex: number;
  currentTrackId: string | null;
  currentTime: number;
  isPlaying: boolean;
  volume: number;
  isMuted: boolean;
  shuffleEnabled: boolean;
  repeatMode: RepeatMode;
  shuffleOrder: number[]; // Track indices in shuffle order
  queue: QueueItem[]; // Playing queue
}

export type RepeatMode = 'off' | 'all' | 'one';

export interface CachedTrack {
  trackId: string;
  playlistIds: string[];
  blob: Blob;
  quality: StreamingQuality;
  cachedAt: number;
}

export interface CachedPlaylist {
  playlist: PlaylistSummary;
  tracks: TrackInfo[];
  lastUpdated: number;
}

export interface QueueItem {
  track: TrackInfo;
  playlistId: string;
  indexInPlaylist: number;
  source: 'manual' | 'playlist'; // Whether manually added or auto-populated from playlist
}

export interface AppState {
  settings: AppSettings;
  playback: PlaybackState;
  playlists: PlaylistSummary[];
  currentPlaylistTracks: TrackInfo[];
  isOnline: boolean;
  isConfigured: boolean;
}

// UI State
export interface UIState {
  selectedPlaylistId: string | null;
  searchQuery: string;
  isSettingsOpen: boolean;
  isLoading: boolean;
  error: string | null;
}

// Default values removed - see src/constants.ts
