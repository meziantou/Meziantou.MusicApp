import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { audioPlayer } from '../services';
import { useAudioPlayer, type AudioPlayerState, type AudioPlayerActions } from './useAudioPlayer';

interface PlayerContextValue {
  playerState: AudioPlayerState;
  playerActions: AudioPlayerActions;
  playingPlaylistId: string | null;
}

const PlayerContext = createContext<PlayerContextValue | null>(null);

interface PlayerProviderProps {
  children: ReactNode;
}

export function PlayerProvider({ children }: PlayerProviderProps) {
  const [playerState, playerActions] = useAudioPlayer();
  const [playingPlaylistId, setPlayingPlaylistId] = useState<string | null>(null);

  useEffect(() => {
    const handler = () => {
      setPlayingPlaylistId(prev => {
        const next = audioPlayer.getCurrentPlaylistId();
        return prev === next ? prev : next;
      });
    };
    // Sync on both trackchange and queuechange so setPlaylist() (without an
    // immediate playTrack) and explicit play paths both update the indicator.
    audioPlayer.on('trackchange', handler);
    audioPlayer.on('queuechange', handler);
    return () => {
      audioPlayer.off('trackchange', handler);
      audioPlayer.off('queuechange', handler);
    };
  }, []);

  const value = useMemo<PlayerContextValue>(() => ({
    playerState,
    playerActions,
    playingPlaylistId,
  }), [playerState, playerActions, playingPlaylistId]);

  return <PlayerContext.Provider value={value}>{children}</PlayerContext.Provider>;
}

export function usePlayer(): PlayerContextValue {
  const ctx = useContext(PlayerContext);
  if (!ctx) {
    throw new Error('usePlayer must be used within a PlayerProvider');
  }
  return ctx;
}
