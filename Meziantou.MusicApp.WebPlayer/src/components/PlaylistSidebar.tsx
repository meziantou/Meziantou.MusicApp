import { useMemo, useState, useCallback } from 'react';
import type { PlaylistSummary } from '../types';
import { formatDuration } from '../utils';
import { useApp } from '../hooks';

interface PlaylistSidebarProps {
  onSettingsClick: () => void;
}

export function PlaylistSidebar({ onSettingsClick }: PlaylistSidebarProps) {
  const {
    playlists,
    currentPlaylistId,
    playingPlaylistId,
    selectPlaylist,
    addTrackToPlaylist,
    offlinePlaylistIds,
    playlistDownloadProgress,
    startPlaylistCaching,
    stopPlaylistCaching,
    isOnline,
    networkType,
    createPlaylist,
    deletePlaylist,
    invalidPlaylists,
  } = useApp();

  const [isCreating, setIsCreating] = useState(false);
  const [newPlaylistName, setNewPlaylistName] = useState('');

  const handleCreateClick = useCallback(() => {
    setIsCreating(true);
    setNewPlaylistName('');
  }, []);

  const handleCancelCreate = useCallback(() => {
    setIsCreating(false);
    setNewPlaylistName('');
  }, []);

  const handleConfirmCreate = useCallback(async () => {
    if (!newPlaylistName.trim()) return;

    const playlist = await createPlaylist(newPlaylistName);
    if (playlist) {
      setIsCreating(false);
      setNewPlaylistName('');
      selectPlaylist(playlist);
    }
  }, [newPlaylistName, createPlaylist, selectPlaylist]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      handleConfirmCreate();
    } else if (e.key === 'Escape') {
      handleCancelCreate();
    }
  }, [handleConfirmCreate, handleCancelCreate]);

  const handleDragOver = (e: React.DragEvent, _playlist: PlaylistSummary) => {
    const hasTrackId = e.dataTransfer.types.includes('application/x-meziantou-song-id') ||
      e.dataTransfer.types.includes('text/plain');
    if (!hasTrackId) return;

    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
  };

  const handleDrop = (e: React.DragEvent, playlist: PlaylistSummary) => {
    e.preventDefault();
    e.stopPropagation();

    const trackId = e.dataTransfer.getData('application/x-meziantou-song-id') ||
      e.dataTransfer.getData('text/plain');
    if (!trackId) return;

    addTrackToPlaylist(playlist, trackId);
  };

  return (
    <aside className="sidebar">

      <div className="sidebar-section">
        <div className="sidebar-section-header">
          <h2 className="sidebar-section-title">Playlists</h2>
          {isOnline && !isCreating && (
            <button
              className="new-playlist-btn"
              onClick={handleCreateClick}
              title="Create new playlist"
            >
              <svg viewBox="0 0 24 24" fill="currentColor" width="18" height="18">
                <path d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z" />
              </svg>
            </button>
          )}
        </div>

        {isCreating && (
          <div className="new-playlist-form">
            <input
              type="text"
              className="new-playlist-input"
              placeholder="Playlist name"
              value={newPlaylistName}
              onChange={(e) => setNewPlaylistName(e.target.value)}
              onKeyDown={handleKeyDown}
              autoFocus
            />
            <div className="new-playlist-actions">
              <button
                className="new-playlist-confirm"
                onClick={handleConfirmCreate}
                disabled={!newPlaylistName.trim()}
                title="Create"
              >
                <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16">
                  <path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z" />
                </svg>
              </button>
              <button
                className="new-playlist-cancel"
                onClick={handleCancelCreate}
                title="Cancel"
              >
                <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16">
                  <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
                </svg>
              </button>
            </div>
          </div>
        )}

        <div className="playlist-list">
          {playlists.length === 0 ? (
            <div className="empty-state">No playlists</div>
          ) : (
            playlists.map(playlist => {
              const isOffline = offlinePlaylistIds.has(playlist.id);
              const progress = playlistDownloadProgress.get(playlist.id);

              return (
                <PlaylistItem
                  key={playlist.id}
                  playlist={playlist}
                  isSelected={playlist.id === currentPlaylistId}
                  isPlaying={playlist.id === playingPlaylistId}
                  isOffline={isOffline}
                  progress={progress}
                  isOnline={isOnline}
                  onSelect={() => selectPlaylist(playlist)}
                  onDragOver={(e) => handleDragOver(e, playlist)}
                  onDrop={(e) => handleDrop(e, playlist)}
                  onStartCaching={() => startPlaylistCaching(playlist.id)}
                  onStopCaching={() => stopPlaylistCaching(playlist.id)}
                  onDelete={() => deletePlaylist(playlist.id)}
                />
              );
            })
          )}
        </div>

        {invalidPlaylists.length > 0 && (
          <div className="invalid-playlists-section">
            <h3 className="invalid-playlists-title">Invalid Playlists</h3>
            {invalidPlaylists.map((invalid, index) => (
              <div key={index} className="invalid-playlist-item" title={invalid.errorMessage}>
                <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16" className="error-icon">
                  <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z" />
                </svg>
                <div className="invalid-playlist-content">
                  <span className="invalid-playlist-name">{invalid.path.split(/[\\/]/).pop() || invalid.path}</span>
                  <span className="invalid-playlist-error">{invalid.errorMessage}</span>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="sidebar-footer">
        {(!isOnline || networkType === 'low-data') && (
          <div className="network-status">
            {!isOnline ? (
              <>
                <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16" className="status-icon offline">
                  <path d="M23.64 7c-.45-.34-4.93-4-11.64-4-1.5 0-2.89.19-4.15.48L18.18 13.8 23.64 7zm-6.6 8.22L3.27 1.44 2 2.72l2.05 2.06C1.91 5.17 1.5 5.68 1 6.07l11 13.73 2.11-2.63 4.17 4.17 1.27-1.27-2.51-2.51z" />
                </svg>
                <span>Offline</span>
              </>
            ) : (
              <>
                <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16" className="status-icon cellular">
                  <path d="M2 22h20V2z" />
                </svg>
                <span>Low Data Mode</span>
              </>
            )}
          </div>
        )}
        <button
          className="sidebar-settings-btn"
          onClick={onSettingsClick}
          title="Settings"
        >
          <svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20">
            <path d="M19.14 12.94c.04-.31.06-.63.06-.94 0-.31-.02-.63-.06-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.04.31-.06.63-.06.94s.02.63.06.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z" />
          </svg>
          Settings
        </button>
      </div>
    </aside>
  );
}

interface PlaylistItemProps {
  playlist: PlaylistSummary;
  isSelected: boolean;
  isPlaying: boolean;
  isOffline: boolean;
  progress?: { cached: number; total: number };
  isOnline: boolean;
  onSelect: () => void;
  onDragOver: (e: React.DragEvent) => void;
  onDrop: (e: React.DragEvent) => void;
  onStartCaching: () => void;
  onStopCaching: () => void;
  onDelete: () => void;
}

function PlaylistItem({
  playlist,
  isSelected,
  isPlaying,
  isOffline,
  progress,
  isOnline,
  onSelect,
  onDragOver,
  onDrop,
  onStartCaching,
  onStopCaching,
  onDelete,
}: PlaylistItemProps) {
  const [isDragOver, setIsDragOver] = useState(false);
  const duration = useMemo(() => formatDuration(playlist.duration), [playlist.duration]);

  const isVirtual = playlist.id.startsWith('virtual:');

  const className = [
    'playlist-item',
    isSelected && 'selected',
    isPlaying && 'playing',
    isOffline && 'offline',
    isDragOver && 'drag-over',
  ].filter(Boolean).join(' ');

  const handleDragOver = (e: React.DragEvent) => {
    onDragOver(e);
    if (e.dataTransfer.dropEffect !== 'none') {
      setIsDragOver(true);
    }
  };

  const handleDragLeave = () => {
    setIsDragOver(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    setIsDragOver(false);
    onDrop(e);
  };

  const handleDownloadClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    if (isOffline) {
      onStopCaching();
    } else {
      onStartCaching();
    }
  };

  const handleDeleteClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    onDelete();
  };

  const isFullyCached = progress && progress.cached >= progress.total;
  const isCaching = isOffline && progress && progress.cached < progress.total;

  return (
    <div
      className={className}
      onClick={onSelect}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          onSelect();
        }
      }}
    >
      <div className="playlist-item-content">
        <span className="playlist-item-name">{playlist.name}</span>
        <span className="playlist-item-info">
          {playlist.trackCount} tracks • {duration}
          {isOffline && progress && progress.cached < progress.total && (
            <span className="playlist-cache-info">
              {' • '}{progress.cached}/{progress.total} cached
            </span>
          )}
        </span>
      </div>
      <div className="playlist-item-actions">
        {isPlaying && (
          <svg className="playing-indicator" viewBox="0 0 24 24" fill="currentColor">
            <path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z" />
          </svg>
        )}
        {(isOnline || isOffline) && (
          <button
            className={`playlist-download-btn ${isOffline ? 'active' : ''} ${isCaching ? 'caching' : ''} ${isFullyCached ? 'complete' : ''}`}
            onClick={handleDownloadClick}
            title={isOffline ? (isFullyCached ? 'Remove from offline' : 'Stop caching') : 'Download for offline'}
          >
            {isCaching ? (
              <svg className="download-icon spinning" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <circle cx="12" cy="12" r="10" strokeOpacity="0.25" />
                <path d="M12 2a10 10 0 0 1 10 10" strokeLinecap="round" />
              </svg>
            ) : isFullyCached ? (
              <svg className="download-icon complete" viewBox="0 0 24 24" fill="currentColor">
                <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z" />
              </svg>
            ) : isOffline ? (
              <svg className="download-icon" viewBox="0 0 24 24" fill="currentColor">
                <path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z" />
              </svg>
            ) : (
              <svg className="download-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3" />
              </svg>
            )}
          </button>
        )}
        {isOnline && !isVirtual && (
          <button
            className="playlist-delete-btn"
            onClick={handleDeleteClick}
            title="Delete playlist"
          >
            <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16">
              <path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z" />
            </svg>
          </button>
        )}
      </div>
    </div>
  );
}
