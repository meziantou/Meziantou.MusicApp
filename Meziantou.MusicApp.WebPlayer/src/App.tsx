import { useState, useEffect } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { listen, type UnlistenFn } from '@tauri-apps/api/event';
import { AppProvider, useApp } from './hooks';
import { audioPlayer } from './services';
import type { TrackInfo } from './types';
import {
  PlaylistSidebar,
  TrackList,
  PlayerBar,
  QueuePanel,
  SettingsDialog,
  CacheDiagnosticsDialog,
  SongDetailsDialog,
  UpdateNotification,
} from './components';
import { isTauriApp } from './utils';
import './styles/main.css';

const VOLUME_STEP = 0.05;
const MENU_CONTROL_EVENT = 'player-menu-control';

interface PlayerMenuControlPayload {
  action: 'previous' | 'next' | 'volume';
  delta?: number;
}

function AppContent() {
  const { isLoading, settings, isInitialized, playerState, playerActions } = useApp();
  const tauriApp = isTauriApp();
  const [queueOpen, setQueueOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [diagnosticsOpen, setDiagnosticsOpen] = useState(false);
  const [songDetailsTrack, setSongDetailsTrack] = useState<TrackInfo | null>(null);

  useEffect(() => {
    if (!tauriApp) {
      return;
    }

    let isActive = true;
    let unlisten: UnlistenFn | null = null;

    void listen<PlayerMenuControlPayload>(MENU_CONTROL_EVENT, async ({ payload }) => {
      try {
        switch (payload.action) {
          case 'previous':
            await playerActions.previous();
            break;
          case 'next':
            await playerActions.next();
            break;
          case 'volume': {
            const delta = payload.delta ?? 0;
            if (delta === 0) {
              break;
            }

            if (audioPlayer.getIsMuted()) {
              playerActions.toggleMute();
            }

            const updatedVolume = Math.max(0, Math.min(2, audioPlayer.getVolume() + delta));
            playerActions.setVolume(updatedVolume);
            break;
          }
        }
      } catch (error) {
        console.error('Failed to process player menu action', error);
      }
    })
      .then((fn) => {
        if (isActive) {
          unlisten = fn;
        } else {
          void fn();
        }
      })
      .catch((error) => {
        console.error('Failed to register player menu listener', error);
      });

    return () => {
      isActive = false;
      if (unlisten) {
        void unlisten();
      }
    };
  }, [tauriApp, playerActions]);

  useEffect(() => {
    if (!tauriApp) {
      return;
    }

    const currentTrack = playerState.currentTrack
      ? `${playerState.currentTrack.title}${playerState.currentTrack.artists ? ` - ${playerState.currentTrack.artists}` : ''}`
      : null;

    void invoke('update_menu_bar_state', {
      currentTrack,
      volume: playerState.volume,
    }).catch((error) => {
      console.error('Failed to update player menu state', error);
    });
  }, [
    tauriApp,
    playerState.currentTrack?.id,
    playerState.currentTrack?.title,
    playerState.currentTrack?.artists,
    playerState.volume,
  ]);

  // Show settings on first load if not configured (only after initialization)
  useEffect(() => {
    if (isInitialized && !settings.serverUrl) {
      setSettingsOpen(true);
    }
  }, [isInitialized, settings.serverUrl]);

  // Handle PWA shortcut actions from URL parameters
  useEffect(() => {
    const url = new URL(window.location.href);
    const action = url.searchParams.get('action');
    
    if (action) {
      // Remove the action parameter from URL to avoid repeated execution
      url.searchParams.delete('action');
      window.history.replaceState({}, '', url.toString());
      
      // Execute the shortcut action
      switch (action) {
        case 'play':
          playerActions.play();
          break;
        case 'pause':
          playerActions.pause();
          break;
        case 'next':
          playerActions.next();
          break;
        case 'previous':
          playerActions.previous();
          break;
        case 'volumeup':
          playerActions.setVolume(Math.min(2, audioPlayer.getVolume() + VOLUME_STEP));
          break;
        case 'volumedown':
          playerActions.setVolume(Math.max(0, audioPlayer.getVolume() - VOLUME_STEP));
          break;
      }
    }
  }, [playerActions]);

  // Listen for view song details events
  useEffect(() => {
    const handleViewDetails = (e: CustomEvent<TrackInfo>) => {
      setSongDetailsTrack(e.detail);
    };

    window.addEventListener('viewSongDetails', handleViewDetails as EventListener);
    return () => window.removeEventListener('viewSongDetails', handleViewDetails as EventListener);
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ctrl+F to focus search (works even when in inputs)
      if (e.ctrlKey && e.code === 'KeyF') {
        e.preventDefault();
        window.dispatchEvent(new CustomEvent('focusSearchInput'));
        return;
      }

      // Ignore when typing in inputs
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) {
        return;
      }

      switch (e.code) {
        case 'Space':
          e.preventDefault();
          // Toggle play/pause is handled by the hook
          break;
        case 'ArrowLeft':
          if (e.shiftKey) {
            e.preventDefault();
            playerActions.seek(audioPlayer.getCurrentTime() - 10);
          } else if (e.ctrlKey) {
            e.preventDefault();
            playerActions.seek(audioPlayer.getCurrentTime() - 30);
          }
          break;
        case 'ArrowRight':
          if (e.shiftKey) {
            e.preventDefault();
            playerActions.seek(audioPlayer.getCurrentTime() + 10);
          } else if (e.ctrlKey) {
            e.preventDefault();
            playerActions.seek(audioPlayer.getCurrentTime() + 30);
          }
          break;
        case 'ArrowUp':
          if (e.ctrlKey) {
            e.preventDefault();
            playerActions.setVolume(Math.min(2, audioPlayer.getVolume() + 0.05));
          }
          break;
        case 'ArrowDown':
          if (e.ctrlKey) {
            e.preventDefault();
            playerActions.setVolume(Math.max(0, audioPlayer.getVolume() - 0.05));
          }
          break;
        case 'Escape':
          setQueueOpen(false);
          setSettingsOpen(false);
          setSongDetailsTrack(null);
          break;
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [playerActions]);

  // Show loading screen until initialized
  if (!isInitialized) {
    return (
      <div className="loading-screen">
        <div className="loading-spinner"></div>
      </div>
    );
  }

  return (
    <>
      <div className="app-layout">
        <aside className="sidebar-container">
          <PlaylistSidebar onSettingsClick={() => setSettingsOpen(true)} />
        </aside>
        <main className="main-content">
          <div className="track-list-wrapper">
            <TrackList />
          </div>
        </main>
        <footer className="player-bar-container">
          <PlayerBar
            onQueueClick={() => setQueueOpen(!queueOpen)}
          />
        </footer>
      </div>

      <div className="queue-panel-container">
        <QueuePanel isOpen={queueOpen} onClose={() => setQueueOpen(false)} />
      </div>

      <SettingsDialog 
        isOpen={settingsOpen} 
        onClose={() => setSettingsOpen(false)} 
        onOpenDiagnostics={() => {
          setSettingsOpen(false);
          setDiagnosticsOpen(true);
        }}
      />

      <CacheDiagnosticsDialog 
        isOpen={diagnosticsOpen} 
        onClose={() => setDiagnosticsOpen(false)} 
      />

      <SongDetailsDialog
        track={songDetailsTrack}
        onClose={() => setSongDetailsTrack(null)}
      />

      {!tauriApp && <UpdateNotification />}

      {isLoading && (
        <div className="loading-overlay">
          <div className="loading-spinner"></div>
        </div>
      )}
    </>
  );
}

export function App() {
  return (
    <AppProvider>
      <AppContent />
    </AppProvider>
  );
}
