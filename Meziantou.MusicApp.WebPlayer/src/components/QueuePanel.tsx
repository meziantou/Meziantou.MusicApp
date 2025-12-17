import type { QueueItem } from '../types';
import { useApp } from '../hooks';
import { PlayingIndicator } from './PlayingIndicator';

interface QueuePanelProps {
  isOpen: boolean;
  onClose: () => void;
}

export function QueuePanel({ isOpen, onClose }: QueuePanelProps) {
  const { playerState, playerActions, playlists } = useApp();

  if (!isOpen) return null;

  const queue = playerState.queue;
  const currentTrack = playerState.currentTrack;
  const manualItems = queue.filter(item => item.source === 'manual');
  const playlistItems = queue.filter(item => item.source === 'playlist');

  const playlistName = playlistItems.length > 0 
    ? playlists.find(p => p.id === playlistItems[0].playlistId)?.name || 'Playlist'
    : 'Playlist';

  const handleClearQueue = () => {
    playerActions.clearQueue();
  };

  const handleRemoveItem = (index: number) => {
    playerActions.removeFromQueue(index);
  };

  const handlePlayItem = async (queueIndex: number) => {
    // Remove all items before this one
    for (let i = 0; i < queueIndex; i++) {
      playerActions.removeFromQueue(0);
    }
    // Skip the current track to play the next one in queue
    await playerActions.next();
  };

  return (
    <div className="queue-panel" style={{ display: 'flex' }}>
      <div className="queue-header">
        <h3>Playing Queue</h3>
        <div className="queue-actions">
          <button className="queue-clear-btn" title="Clear queue" onClick={handleClearQueue}>
            Clear
          </button>
          <button className="queue-close-btn" title="Close" onClick={onClose}>
            ×
          </button>
        </div>
      </div>
      <div className="queue-content">
        {queue.length === 0 && !currentTrack ? (
          <div className="queue-empty-message">
            Queue is empty. Add tracks to play them next.
          </div>
        ) : (
          <div className="queue-list">
            {currentTrack && (
              <div className="queue-section">
                <div className="queue-section-header">Now Playing</div>
                <div className="queue-item playing">
                  <div className="queue-item-number">
                    <PlayingIndicator
                      isPlaying={true}
                      isPaused={!playerState.isPlaying}
                      onTogglePlay={() => playerActions.togglePlayPause()}
                    />
                  </div>
                  <div className="queue-item-info">
                    <div className="queue-item-title" style={{ color: 'var(--accent-primary)' }}>{currentTrack.title}</div>
                    <div className="queue-item-artist">{currentTrack.artists ?? 'Unknown Artist'}</div>
                  </div>
                </div>
              </div>
            )}

            {manualItems.length > 0 && (
              <QueueSection
                title="Next Up"
                items={manualItems}
                queue={queue}
                startNumber={1}
                onPlay={handlePlayItem}
                onRemove={handleRemoveItem}
              />
            )}
            {playlistItems.length > 0 && (
              <QueueSection
                title={`Next from: ${playlistName}`}
                items={playlistItems}
                queue={queue}
                startNumber={manualItems.length + 1}
                onPlay={handlePlayItem}
                onRemove={handleRemoveItem}
              />
            )}
          </div>
        )}
      </div>
    </div>
  );
}

interface QueueSectionProps {
  title: string;
  items: QueueItem[];
  queue: QueueItem[];
  startNumber: number;
  onPlay: (queueIndex: number) => void;
  onRemove: (queueIndex: number) => void;
}

function QueueSection({ title, items, queue, startNumber, onPlay, onRemove }: QueueSectionProps) {
  return (
    <div className="queue-section">
      <div className="queue-section-header">{title}</div>
      {items.map((item, i) => {
        const queueIndex = queue.indexOf(item);
        const displayNumber = startNumber + i;

        return (
          <QueueItemRow
            key={`${item.track.id}-${queueIndex}`}
            item={item}
            queueIndex={queueIndex}
            displayNumber={displayNumber}
            onPlay={() => onPlay(queueIndex)}
            onRemove={() => onRemove(queueIndex)}
          />
        );
      })}
    </div>
  );
}

interface QueueItemRowProps {
  item: QueueItem;
  queueIndex: number;
  displayNumber: number;
  onPlay: () => void;
  onRemove: () => void;
}

function QueueItemRow({ item, displayNumber, onPlay, onRemove }: QueueItemRowProps) {
  const track = item.track;
  const isManual = item.source === 'manual';

  return (
    <div
      className={`queue-item ${isManual ? 'queue-item-manual' : ''}`}
      onDoubleClick={onPlay}
    >
      <div className="queue-item-number">{displayNumber}</div>
      <div className="queue-item-info">
        <div className="queue-item-title">{track.title}</div>
        <div className="queue-item-artist">{track.artists ?? 'Unknown Artist'}</div>
      </div>
      <button
        className="queue-item-remove"
        title="Remove from queue"
        onClick={(e) => {
          e.stopPropagation();
          onRemove();
        }}
      >
        ×
      </button>
    </div>
  );
}
