import type { AppSettings, PlaybackState } from './types';

export const EQUALIZER_FREQUENCIES = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000] as const;
export const EQUALIZER_MIN_GAIN_DB = -12;
export const EQUALIZER_MAX_GAIN_DB = 12;
export const DEFAULT_EQUALIZER_GAINS = EQUALIZER_FREQUENCIES.map(() => 0);

function clampEqualizerGain(gain: number): number {
  return Math.min(EQUALIZER_MAX_GAIN_DB, Math.max(EQUALIZER_MIN_GAIN_DB, gain));
}

export function normalizeEqualizerGains(gains: readonly number[] | null | undefined): number[] {
  return EQUALIZER_FREQUENCIES.map((_, index) => {
    const value = gains?.[index];
    if (typeof value !== 'number' || !Number.isFinite(value)) {
      return 0;
    }

    return clampEqualizerGain(value);
  });
}

export const DEFAULT_SETTINGS: AppSettings = {
  serverUrl: '',
  normalQuality: { format: 'opus', maxBitRate: 160 },
  lowDataQuality: { format: 'opus', maxBitRate: 160 },
  downloadQuality: { format: 'opus', maxBitRate: 160 },
  preventDownloadOnLowData: false,
  hideCoverArt: false,
  hideTrackIndex: false,
  hideTrackDuration: false,
  hideTrackCacheStatus: false,
  disablePlayingAnimation: false,
  replayGainMode: 'off',
  replayGainPreamp: 0,
  showReplayGainWarning: true,
  equalizerGains: [...DEFAULT_EQUALIZER_GAINS],
};

export const DEFAULT_PLAYBACK_STATE: PlaybackState = {
  currentPlaylistId: null,
  currentTrackIndex: -1,
  currentTrackId: null,
  currentTime: 0,
  isPlaying: false,
  volume: 1,
  isMuted: false,
  shuffleEnabled: false,
  repeatMode: 'off',
  shuffleOrder: [],
  queue: []
};
