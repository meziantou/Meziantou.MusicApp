import type { AppSettings, PlaybackState } from './types';

export const DEFAULT_SETTINGS: AppSettings = {
  serverUrl: '',
  authToken: '',
  normalQuality: { format: 'opus', maxBitRate: 160 },
  lowDataQuality: { format: 'opus', maxBitRate: 160 },
  downloadQuality: { format: 'opus', maxBitRate: 160 },
  preventDownloadOnLowData: false,
  scrobbleEnabled: true,
  hideCoverArt: false,
  replayGainMode: 'off',
  replayGainPreamp: 0,
  showReplayGainWarning: true
};

export const DEFAULT_PLAYBACK_STATE: PlaybackState = {
  currentPlaylistId: null,
  currentTrackIndex: -1,
  currentTime: 0,
  isPlaying: false,
  volume: 1,
  isMuted: false,
  shuffleEnabled: false,
  repeatMode: 'off',
  shuffleOrder: [],
  queue: []
};
