import { useMemo } from 'react';
import type { QueueItem } from '../types';
import { useApp, usePlayer } from '../hooks';
import { PlayingIndicator } from './PlayingIndicator';

interface QueuePanelProps {
  isOpen: boolean;
  onClose: () => void;
}

interface IndexedQueueItem {
  item: QueueItem;
  lookaheadIndex: number;
}

export function QueuePanel({ isOpen, onClose }: QueuePanelProps) {
  const { playlists } = useApp();
  const { playerState, playerActions } = usePlayer();
  const lookaheadQueue = playerState.lookaheadQueue;
  const currentTrack = playerState.currentTrack;
  const { manualItems, playlistItems } = useMemo(() => {
    const manual: IndexedQueueItem[] = [];
    const playlist: IndexedQueueItem[] = [];

    for (let i = 0; i < lookaheadQueue.length; i++) {
      const indexedItem = { item: lookaheadQueue[i], lookaheadIndex: i };
      if (indexedItem.item.source === 'manual') {
        manual.push(indexedItem);
      } else if (indexedItem.item.source === 'playlist') {
        playlist.push(indexedItem);
      }
    }

    return { manualItems: manual, playlistItems: playlist };
  }, [lookaheadQueue]);

  if (!isOpen) return null;

  const playlistName = playlistItems.length > 0
    ? playlists.find(p => p.id === playlistItems[0].item.playlistId)?.name || 'Playlist'
    : 'Playlist';

  const handleRemoveItem = (lookaheadIndex: number) => {
    // Convert lookahead index to full queue index by adding (currentIndex + 1)
    const fullQueue = playerState.queue;
    const currentIndex = fullQueue.length - lookaheadQueue.length - 1;
    const queueIndex = currentIndex + 1 + lookaheadIndex;
    playerActions.removeFromQueue(queueIndex);
  };

  const handlePlayItem = async (lookaheadIndex: number) => {
    if (lookaheadIndex < 0 || lookaheadIndex >= lookaheadQueue.length) return;

    // Skip to this track by calling next() multiple times
    for (let i = 0; i <= lookaheadIndex; i++) {
      await playerActions.next();
    }
  };

  return (
    <div className="queue-panel" style={{ display: 'flex' }}>
      <div className="queue-header">
        <h3>Playing Queue</h3>
        <div className="queue-actions">
          <button className="queue-close-btn" title="Close" onClick={onClose}>
            ×
          </button>
        </div>
      </div>
      <div className="queue-content">
        {lookaheadQueue.length === 0 && !currentTrack ? (
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
                startNumber={1}
                onPlay={handlePlayItem}
                onRemove={handleRemoveItem}
              />
            )}
            {playlistItems.length > 0 && (
              <QueueSection
                title={`Next from: ${playlistName}`}
                items={playlistItems}
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
  items: IndexedQueueItem[];
  startNumber: number;
  onPlay: (lookaheadIndex: number) => void;
  onRemove: (lookaheadIndex: number) => void;
}

function QueueSection({ title, items, startNumber, onPlay, onRemove }: QueueSectionProps) {
  return (
    <div className="queue-section">
      <div className="queue-section-header">{title}</div>
      {items.map((indexedItem, i) => {
        const { item, lookaheadIndex } = indexedItem;
        const displayNumber = startNumber + i;

        return (
          <QueueItemRow
            key={`${item.track.id}-${lookaheadIndex}`}
            item={item}
            lookaheadIndex={lookaheadIndex}
            displayNumber={displayNumber}
            onPlay={() => onPlay(lookaheadIndex)}
            onRemove={() => onRemove(lookaheadIndex)}
          />
        );
      })}
    </div>
  );
}

interface QueueItemRowProps {
  item: QueueItem;
  lookaheadIndex: number;
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
