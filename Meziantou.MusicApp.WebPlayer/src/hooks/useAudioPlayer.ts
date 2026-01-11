import { useState, useEffect, useMemo, useRef } from 'react';
import type { TrackInfo, RepeatMode, QueueItem, StreamingQuality, ReplayGainMode, PlaybackState } from '../types';
import { audioPlayer, type PlayerEventType, type PlayerEventDetail } from '../services';

export interface AudioPlayerState {
  currentTrack: TrackInfo | null;
  currentQuality: StreamingQuality | null;
  isPlaying: boolean;
  currentTime: number;
  duration: number;
  volume: number;
  isMuted: boolean;
  shuffleEnabled: boolean;
  repeatMode: RepeatMode;
  queue: QueueItem[];
  lookaheadQueue: QueueItem[];
  isLoading: boolean;
}

export interface AudioPlayerActions {
  play: () => Promise<void>;
  pause: () => void;
  togglePlayPause: () => void;
  next: () => Promise<void>;
  previous: () => Promise<void>;
  seek: (time: number) => void;
  setVolume: (volume: number) => void;
  toggleMute: () => void;
  setShuffle: (enabled: boolean) => void;
  cycleRepeatMode: () => RepeatMode;
  setPlaylist: (playlistId: string, tracks: TrackInfo[], initialShuffleOrder?: number[]) => void;
  playTrack: (track: TrackInfo) => Promise<void>;
  playAtIndex: (index: number, autoPlay?: boolean, startTime?: number) => Promise<void>;
  addToQueue: (track: TrackInfo, playlistId: string, indexInPlaylist: number) => void;
  removeFromQueue: (index: number) => void;
  setQuality: (quality: StreamingQuality) => void;
  setReplayGainMode: (mode: ReplayGainMode) => void;
  setReplayGainPreamp: (preamp: number) => void;
  setScrobbleEnabled: (enabled: boolean) => void;
  setPreventDownloadOnLowData: (prevent: boolean) => void;
  setNetworkType: (type: 'normal' | 'low-data' | 'unknown') => void;
  setCachedTrackIds: (ids: Set<string>) => void;
  setIsOnline: (isOnline: boolean) => void;
  restoreState: (state: PlaybackState) => Promise<void>;
  getCurrentPlaylistId: () => string | null;
  getCurrentIndex: () => number;
  loadRecentlyPlayed: () => Promise<void>;
}

export function useAudioPlayer(): [AudioPlayerState, AudioPlayerActions] {
  const [state, setState] = useState<AudioPlayerState>({
    currentTrack: audioPlayer.getCurrentTrack(),
    currentQuality: audioPlayer.getCurrentQuality(),
    isPlaying: audioPlayer.isPlaying(),
    currentTime: audioPlayer.getCurrentTime(),
    duration: audioPlayer.getDuration(),
    volume: audioPlayer.getVolume(),
    isMuted: audioPlayer.getIsMuted(),
    shuffleEnabled: audioPlayer.isShuffleEnabled(),
    repeatMode: audioPlayer.getRepeatMode(),
    queue: audioPlayer.getQueue(),
    lookaheadQueue: audioPlayer.getLookaheadQueue(),
    isLoading: false,
  });

  const timeUpdateThrottleRef = useRef<number>(0);

  useEffect(() => {
    const handlers: { event: PlayerEventType; handler: (detail: PlayerEventDetail) => void }[] = [
      {
        event: 'play',
        handler: () => setState(prev => ({ ...prev, isPlaying: true })),
      },
      {
        event: 'pause',
        handler: () => setState(prev => ({ ...prev, isPlaying: false })),
      },
      {
        event: 'timeupdate',
        handler: (detail) => {
          const now = Date.now();
          if (now - timeUpdateThrottleRef.current > 250) {
            timeUpdateThrottleRef.current = now;
            setState(prev => ({
              ...prev,
              currentTime: detail.currentTime ?? prev.currentTime,
              duration: detail.duration ?? prev.duration,
            }));
          }
        },
      },
      {
        event: 'durationchange',
        handler: (detail) => setState(prev => ({ ...prev, duration: detail.duration ?? 0 })),
      },
      {
        event: 'trackchange',
        handler: (detail) => setState(prev => ({
          ...prev,
          currentTrack: detail.track ?? null,
          currentQuality: detail.quality ?? null,
          currentTime: 0,
          duration: 0,
          isLoading: false
        })),
      },
      {
        event: 'volumechange',
        handler: () => setState(prev => ({
          ...prev,
          volume: audioPlayer.getVolume(),
          isMuted: audioPlayer.getIsMuted(),
        })),
      },
      {
        event: 'queuechange',
        handler: () => setState(prev => ({
          ...prev,
          queue: audioPlayer.getQueue(),
          lookaheadQueue: audioPlayer.getLookaheadQueue()
        })),
      },
      {
        event: 'loadstart',
        handler: () => setState(prev => ({ ...prev, isLoading: true })),
      },
      {
        event: 'canplay',
        handler: () => setState(prev => ({ ...prev, isLoading: false })),
      },
      {
        event: 'error',
        handler: () => setState(prev => ({ ...prev, isLoading: false })),
      },
    ];

    for (const { event, handler } of handlers) {
      audioPlayer.on(event, handler);
    }

    return () => {
      for (const { event, handler } of handlers) {
        audioPlayer.off(event, handler);
      }
    };
  }, []);

  const actions = useMemo<AudioPlayerActions>(() => ({
    play: () => audioPlayer.play(),
    pause: () => audioPlayer.pause(),
    togglePlayPause: () => audioPlayer.togglePlayPause(),
    next: () => audioPlayer.next(),
    previous: () => audioPlayer.previous(),
    seek: (time: number) => audioPlayer.seek(time),
    setVolume: (volume: number) => audioPlayer.setVolume(volume),
    toggleMute: () => audioPlayer.toggleMute(),
    setShuffle: (enabled: boolean) => {
      audioPlayer.setShuffle(enabled);
      setState(prev => ({ ...prev, shuffleEnabled: enabled }));
    },
    cycleRepeatMode: () => {
      const mode = audioPlayer.cycleRepeatMode();
      setState(prev => ({ ...prev, repeatMode: mode }));
      return mode;
    },
    setPlaylist: (playlistId: string, tracks: TrackInfo[], initialShuffleOrder?: number[]) => {
      audioPlayer.setPlaylist(playlistId, tracks, initialShuffleOrder);
    },
    playTrack: async (track: TrackInfo) => {
      setState(prev => ({ ...prev, isLoading: true }));
      await audioPlayer.playTrack(track);
    },
    playAtIndex: async (index: number, autoPlay = true, startTime = 0) => {
      setState(prev => ({ ...prev, isLoading: true }));
      await audioPlayer.playAtIndex(index, autoPlay, startTime);
    },
    addToQueue: (track: TrackInfo, playlistId: string, indexInPlaylist: number) => {
      audioPlayer.addToQueue(track, playlistId, indexInPlaylist);
    },
    removeFromQueue: (index: number) => {
      audioPlayer.removeFromQueue(index);
    },
    setQuality: (quality: StreamingQuality) => {
      audioPlayer.setQuality(quality);
    },
    setReplayGainMode: (mode: ReplayGainMode) => {
      audioPlayer.setReplayGainMode(mode);
    },
    setReplayGainPreamp: (preamp: number) => {
      audioPlayer.setReplayGainPreamp(preamp);
    },
    setScrobbleEnabled: (enabled: boolean) => {
      audioPlayer.setScrobbleEnabled(enabled);
    },
    setPreventDownloadOnLowData: (prevent: boolean) => {
      audioPlayer.setPreventDownloadOnLowData(prevent);
    },
    setNetworkType: (type: 'normal' | 'low-data' | 'unknown') => {
      audioPlayer.setNetworkType(type);
    },
    setCachedTrackIds: (ids: Set<string>) => {
      audioPlayer.setCachedTrackIds(ids);
    },
    setIsOnline: (isOnline: boolean) => {
      audioPlayer.setIsOnline(isOnline);
    },
    restoreState: async (playbackState: PlaybackState) => {
      await audioPlayer.restoreState(playbackState);
      setState(prev => ({
        ...prev,
        volume: playbackState.volume,
        isMuted: playbackState.isMuted,
        shuffleEnabled: playbackState.shuffleEnabled,
        repeatMode: playbackState.repeatMode,
        queue: playbackState.queue,
      }));
    },
    getCurrentPlaylistId: () => audioPlayer.getCurrentPlaylistId(),
    getCurrentIndex: () => audioPlayer.getCurrentIndex(),
    loadRecentlyPlayed: () => audioPlayer.loadRecentlyPlayed(),
  }), []);

  return [state, actions];
}
