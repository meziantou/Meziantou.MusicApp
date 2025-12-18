import { useState, useRef, useEffect, useCallback } from 'react';
import type { RepeatMode } from '../types';
import { formatDuration, throttle } from '../utils';
import { useApp } from '../hooks';
import { CoverImage } from './CoverImage';

interface PlayerBarProps {
  onQueueClick: () => void;
}

const getFormatColor = (format: string) => {
  switch (format.toLowerCase()) {
    case 'flac': return '#00bcd4'; // Cyan
    case 'mp3': return '#ff9800'; // Orange
    case 'aac': return '#4caf50'; // Green
    case 'wav': return '#9c27b0'; // Purple
    case 'ogg': return '#ff5722'; // Deep Orange
    case 'opus': return '#e91e63'; // Pink
    case 'm4a': return '#2196f3'; // Blue
    default: return '#9e9e9e'; // Grey
  }
};

export function PlayerBar({ onQueueClick }: PlayerBarProps) {
  const { playerState, playerActions, currentPlaylistId, selectPlaylist, playlists } = useApp();

  const [isDragging, setIsDragging] = useState(false);
  const [showRemainingTime, setShowRemainingTime] = useState(() => {
    const saved = localStorage.getItem('showRemainingTime');
    return saved === 'true';
  });
  const progressBarRef = useRef<HTMLDivElement>(null);

  // Persist time display mode
  useEffect(() => {
    localStorage.setItem('showRemainingTime', String(showRemainingTime));
  }, [showRemainingTime]);

  const handleSeek = useCallback((clientX: number) => {
    const bar = progressBarRef.current;
    if (!bar) return;

    const rect = bar.getBoundingClientRect();
    const percent = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    const time = percent * playerState.duration;
    playerActions.seek(time);
  }, [playerState.duration, playerActions]);

  const handleProgressMouseDown = (e: React.MouseEvent) => {
    setIsDragging(true);
    handleSeek(e.clientX);
  };

  useEffect(() => {
    if (!isDragging) return;

    const handleMouseMove = (e: MouseEvent) => handleSeek(e.clientX);
    const handleMouseUp = () => setIsDragging(false);

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);

    return () => {
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };
  }, [isDragging, handleSeek]);

  const handleVolumeWheel = (e: React.WheelEvent) => {
    const step = 0.05;
    const delta = e.deltaY < 0 ? step : -step;
    const newVolume = Math.max(0, Math.min(2, playerState.volume + delta));

    if (playerState.isMuted) {
      playerActions.toggleMute();
    }
    playerActions.setVolume(newVolume);
  };

  const handleVolumeChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const volume = parseInt(e.target.value, 10) / 100;
    if (playerState.isMuted && volume > 0) {
      playerActions.toggleMute();
    }
    playerActions.setVolume(volume);
  };

  const handleCoverClick = async () => {
    const currentPlaylistIdFromPlayer = playerActions.getCurrentPlaylistId();
    if (!currentPlaylistIdFromPlayer) return;

    if (currentPlaylistId !== currentPlaylistIdFromPlayer) {
      const playlist = playlists.find(p => p.id === currentPlaylistIdFromPlayer);
      if (playlist) {
        await selectPlaylist(playlist);
      }
    }
    
    // Dispatch event to scroll to current track
    window.dispatchEvent(new CustomEvent('scrollToCurrentTrack'));
  };

  const progressPercent = playerState.duration > 0
    ? (playerState.currentTime / playerState.duration) * 100
    : 0;

  const throttledTimeUpdate = throttle(() => {}, 250);
  throttledTimeUpdate();

  return (
    <div className="player-bar">
      <div className="player-track-info">
        <CoverImage
          trackId={playerState.currentTrack?.id ?? ''}
          size={64}
          className="player-cover"
          alt=""
          onClick={handleCoverClick}
        />
        <div className="player-track-details">
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
            <span className="player-track-title" style={{ flex: '0 1 auto' }}>
              {playerState.currentTrack?.title ?? 'No track selected'}
            </span>
            {playerState.currentQuality && (
              <span
                className="player-format-badge"
                style={{
                  backgroundColor: getFormatColor(playerState.currentQuality.format),
                  color: 'white',
                  padding: '2px 6px',
                  borderRadius: '4px',
                  fontSize: '10px',
                  fontWeight: 'bold',
                  lineHeight: '1',
                  flex: '0 0 auto',
                }}
              >
                {playerState.currentQuality.format.toUpperCase()}
                {playerState.currentQuality.maxBitRate ? ` ${playerState.currentQuality.maxBitRate}` : ''}
              </span>
            )}
          </div>
          <span className="player-track-artist">
            {playerState.currentTrack?.artists ?? ''}
          </span>
        </div>
      </div>

      <div className="player-controls">
        <div className="player-buttons">
          <ShuffleButton
            active={playerState.shuffleEnabled}
            onClick={() => playerActions.setShuffle(!playerState.shuffleEnabled)}
          />
          <button
            className="icon-button prev-btn"
            title="Previous"
            aria-label="Previous track"
            onClick={() => playerActions.previous()}
          >
            <svg viewBox="0 0 24 24" fill="currentColor">
              <path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" />
            </svg>
          </button>
          <PlayPauseButton
            isPlaying={playerState.isPlaying}
            onClick={() => playerActions.togglePlayPause()}
          />
          <button
            className="icon-button next-btn"
            title="Next"
            aria-label="Next track"
            onClick={() => playerActions.next()}
          >
            <svg viewBox="0 0 24 24" fill="currentColor">
              <path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z" />
            </svg>
          </button>
          <RepeatButton
            mode={playerState.repeatMode}
            onClick={() => playerActions.cycleRepeatMode()}
          />
        </div>

        <div className="player-progress">
          <span className="progress-time current-time">
            {formatDuration(playerState.currentTime)}
          </span>
          <div
            className="progress-bar-container"
            onMouseDown={handleProgressMouseDown}
            ref={progressBarRef}
          >
            <div className="progress-bar">
              <div
                className="progress-bar-fill"
                style={{ width: `${progressPercent}%` }}
              />
              <div
                className="progress-bar-handle"
                style={{ left: `${progressPercent}%` }}
              />
            </div>
          </div>
          <span
            className="progress-time duration"
            onClick={() => setShowRemainingTime(!showRemainingTime)}
            style={{ cursor: 'pointer' }}
            title={showRemainingTime ? 'Show total time' : 'Show remaining time'}
          >
            {showRemainingTime && playerState.duration > 0
              ? `-${formatDuration(playerState.duration - playerState.currentTime)}`
              : formatDuration(playerState.duration)}
          </span>
        </div>
      </div>

      <div className="player-right">
        <div className="player-secondary-actions">
          <QueueButton
            queueLength={playerState.queue.length}
            onClick={onQueueClick}
          />
        </div>
        <div className="player-volume" onWheel={handleVolumeWheel}>
          <VolumeButton
            volume={playerState.volume}
            isMuted={playerState.isMuted}
            onMuteToggle={() => playerActions.toggleMute()}
          />
          <div className="volume-slider-container">
            <input
              type="range"
              className="volume-slider"
              min="0"
              max="200"
              value={playerState.isMuted ? 0 : playerState.volume * 100}
              title={`Volume: ${Math.round(playerState.volume * 100)}%`}
              aria-label="Volume"
              onChange={handleVolumeChange}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

function ShuffleButton({ active, onClick }: { active: boolean; onClick: () => void }) {
  return (
    <button
      className={`icon-button shuffle-btn ${active ? 'active' : ''}`}
      title="Shuffle"
      aria-label="Toggle shuffle"
      onClick={onClick}
    >
      <svg viewBox="0 0 24 24" fill="currentColor">
        <path d="M10.59 9.17L5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41l-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z" />
      </svg>
    </button>
  );
}

function PlayPauseButton({ isPlaying, onClick }: { isPlaying: boolean; onClick: () => void }) {
  return (
    <button
      className="play-pause-btn"
      title={isPlaying ? 'Pause' : 'Play'}
      aria-label={isPlaying ? 'Pause' : 'Play'}
      onClick={onClick}
    >
      {isPlaying ? (
        <svg viewBox="0 0 24 24" fill="currentColor">
          <path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z" />
        </svg>
      ) : (
        <svg viewBox="0 0 24 24" fill="currentColor">
          <path d="M8 5v14l11-7z" />
        </svg>
      )}
    </button>
  );
}

function RepeatButton({ mode, onClick }: { mode: RepeatMode; onClick: () => void }) {
  const isActive = mode !== 'off';

  return (
    <button
      className={`icon-button repeat-btn ${isActive ? 'active' : ''}`}
      title="Repeat"
      aria-label="Toggle repeat"
      onClick={onClick}
    >
      {mode === 'one' ? (
        <svg viewBox="0 0 24 24" fill="currentColor">
          <path d="M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4zm-4-2V9h-1l-2 1v1h1.5v4H13z" />
        </svg>
      ) : (
        <svg viewBox="0 0 24 24" fill="currentColor">
          <path d="M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4z" />
        </svg>
      )}
    </button>
  );
}

function VolumeButton({
  volume,
  isMuted,
  onMuteToggle,
}: {
  volume: number;
  isMuted: boolean;
  onMuteToggle: () => void;
}) {
  const getIcon = () => {
    if (isMuted || volume === 0) {
      return (
        <svg viewBox="0 0 24 24" fill="currentColor">
          <path d="M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z" />
        </svg>
      );
    }
    if (volume < 0.5) {
      return (
        <svg viewBox="0 0 24 24" fill="currentColor">
          <path d="M18.5 12c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM5 9v6h4l5 5V4L9 9H5z" />
        </svg>
      );
    }
    return (
      <svg viewBox="0 0 24 24" fill="currentColor">
        <path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z" />
      </svg>
    );
  };

  const volumePercentage = Math.round(volume * 100);

  return (
    <button
      className="icon-button volume-btn"
      title={`Volume: ${volumePercentage}%`}
      aria-label="Toggle mute"
      onClick={onMuteToggle}
    >
      {getIcon()}
    </button>
  );
}

function QueueButton({ queueLength, onClick }: { queueLength: number; onClick: () => void }) {
  const hasItems = queueLength > 0;

  return (
    <button
      className={`icon-button queue-btn ${hasItems ? 'has-items' : ''}`}
      title="Queue"
      aria-label="Toggle queue"
      onClick={onClick}
    >
      <svg viewBox="0 0 24 24" fill="currentColor">
        <path d="M15 6H3v2h12V6zm0 4H3v2h12v-2zM3 16h8v-2H3v2zM17 6v8.18c-.31-.11-.65-.18-1-.18-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3V8h3V6h-5z" />
      </svg>
      {hasItems && (
        <span className="queue-badge">
          {queueLength > 99 ? '99+' : queueLength}
        </span>
      )}
    </button>
  );
}
