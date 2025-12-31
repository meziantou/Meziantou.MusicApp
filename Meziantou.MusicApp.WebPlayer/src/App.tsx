import { useState, useEffect } from 'react';
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
} from './components';
import './styles/main.css';

function AppContent() {
  const { isLoading, settings, isInitialized, playerActions } = useApp();
  const [queueOpen, setQueueOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [diagnosticsOpen, setDiagnosticsOpen] = useState(false);
  const [songDetailsTrack, setSongDetailsTrack] = useState<TrackInfo | null>(null);

  // Show settings on first load if not configured (only after initialization)
  useEffect(() => {
    if (isInitialized && !settings.serverUrl) {
      setSettingsOpen(true);
    }
  }, [isInitialized, settings.serverUrl]);

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
