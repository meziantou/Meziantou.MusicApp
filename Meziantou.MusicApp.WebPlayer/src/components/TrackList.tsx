import { useState, useRef, useCallback, useEffect, useLayoutEffect, useMemo } from 'react';
import type { TrackInfo, AppSettings } from '../types';
import { formatDuration, matchesSearch, debounce } from '../utils';
import { useApp } from '../hooks';
import { PlayingIndicator } from './PlayingIndicator';
import { CoverImage } from './CoverImage';

type SortOption = 'added' | 'title' | 'artist' | 'album';
type SortDirection = 'asc' | 'desc';

const ITEM_HEIGHT = 56;
const BUFFER_SIZE = 5;

export function TrackList() {
  const {
    settings,
    currentPlaylistTracks,
    currentPlaylistId,
    playingPlaylistId,
    playerState,
    cachedTrackIds,
    isOnline,
    playTrack,
    downloadTrack,
    deleteDownloadedTrack,
    removeTrackFromPlaylist,
    playerActions,
    showToast,
  } = useApp();

  const [searchQuery, setSearchQuery] = useState('');
  const [visibleRange, setVisibleRange] = useState({ start: 0, end: 20 });
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; track: TrackInfo; index: number } | null>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const lastRestoredPlaylistId = useRef<string | null>(null);

  const [sortOption, setSortOption] = useState<SortOption>('added');
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc');
  const sortButtonRef = useRef<HTMLButtonElement>(null);

  const filteredTracks = useMemo(() => {
    let tracks = currentPlaylistTracks;
    if (searchQuery) {
      tracks = tracks.filter(track =>
        matchesSearch(track.title, searchQuery) ||
        matchesSearch(track.artists, searchQuery) ||
        matchesSearch(track.album, searchQuery)
      );
    }

    return [...tracks].sort((a, b) => {
      let res = 0;
      switch (sortOption) {
        case 'title':
          res = a.title.localeCompare(b.title);
          break;
        case 'artist':
          res = (a.artists || '').localeCompare(b.artists || '');
          break;
        case 'album':
          res = (a.album || '').localeCompare(b.album || '');
          break;
        case 'added':
        default:
           const dateA = a.addedDate ? new Date(a.addedDate).getTime() : 0;
           const dateB = b.addedDate ? new Date(b.addedDate).getTime() : 0;
           res = dateA - dateB;
           break;
      }
      return sortDirection === 'asc' ? res : -res;
    });
  }, [currentPlaylistTracks, searchQuery, sortOption, sortDirection]);

  const handleScroll = useCallback(() => {
    const container = scrollContainerRef.current;
    if (!container) return;

    const scrollTop = container.scrollTop;
    const containerHeight = container.clientHeight;

    const visibleStart = Math.max(0, Math.floor(scrollTop / ITEM_HEIGHT) - BUFFER_SIZE);
    const visibleEnd = Math.min(
      filteredTracks.length,
      Math.ceil((scrollTop + containerHeight) / ITEM_HEIGHT) + BUFFER_SIZE
    );

    setVisibleRange({ start: visibleStart, end: visibleEnd });

    // Save scroll position
    if (currentPlaylistId) {
      const positions = JSON.parse(localStorage.getItem('playlistScrollPositions') || '{}');
      positions[currentPlaylistId] = scrollTop;
      localStorage.setItem('playlistScrollPositions', JSON.stringify(positions));
    }
  }, [filteredTracks.length, currentPlaylistId]);

  useEffect(() => {
    handleScroll();
  }, [filteredTracks, handleScroll]);

  // Restore scroll position
  useEffect(() => {
    if (currentPlaylistId && scrollContainerRef.current && filteredTracks.length > 0) {
      if (lastRestoredPlaylistId.current !== currentPlaylistId) {
        const positions = JSON.parse(localStorage.getItem('playlistScrollPositions') || '{}');
        const saved = positions[currentPlaylistId];
        if (saved) {
          scrollContainerRef.current.scrollTop = saved;
        } else {
          scrollContainerRef.current.scrollTop = 0;
        }
        lastRestoredPlaylistId.current = currentPlaylistId;
      }
    }
  }, [currentPlaylistId, filteredTracks]);

  // Listen for scroll requests from PlayerBar
  useEffect(() => {
    const handleScrollRequest = () => {
      if (!playerState.currentTrack || !scrollContainerRef.current) return;

      const index = filteredTracks.findIndex(t => t.id === playerState.currentTrack?.id);
      if (index !== -1) {
        const scrollTop = index * ITEM_HEIGHT;
        // Center the track if possible
        const containerHeight = scrollContainerRef.current.clientHeight;
        const centeredScrollTop = Math.max(0, scrollTop - containerHeight / 2 + ITEM_HEIGHT / 2);
        
        scrollContainerRef.current.scrollTo({
          top: centeredScrollTop,
          behavior: 'smooth'
        });
      }
    };

    window.addEventListener('scrollToCurrentTrack', handleScrollRequest);
    return () => window.removeEventListener('scrollToCurrentTrack', handleScrollRequest);
  }, [filteredTracks, playerState.currentTrack]);

  const debouncedSearch = useMemo(
    () => debounce((query: string) => setSearchQuery(query), 150),
    []
  );

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    debouncedSearch(e.target.value);
  };

  const handleClearSearch = () => {
    setSearchQuery('');
    const input = document.querySelector('.search-input') as HTMLInputElement;
    if (input) input.value = '';
  };

  const handleTrackDoubleClick = (track: TrackInfo, index: number) => {
    const isCached = cachedTrackIds.has(track.id);
    const isAvailable = isOnline || isCached;
    if (isAvailable) {
      playTrack(track, index, filteredTracks);
    }
  };

  const handleContextMenu = (e: React.MouseEvent, track: TrackInfo, index: number) => {
    e.preventDefault();
    setContextMenu({ x: e.clientX, y: e.clientY, track, index });
  };

  const handleDragStart = (e: React.DragEvent, track: TrackInfo) => {
    e.dataTransfer.effectAllowed = 'copy';
    e.dataTransfer.setData('application/x-meziantou-song-id', track.id);
    e.dataTransfer.setData('text/plain', track.id);
  };

  useEffect(() => {
    const handleClick = () => setContextMenu(null);
    document.addEventListener('click', handleClick);
    return () => document.removeEventListener('click', handleClick);
  }, []);

  const totalHeight = filteredTracks.length * ITEM_HEIGHT;
  const visibleTracks = filteredTracks.slice(visibleRange.start, visibleRange.end);

  const trackCountText = useMemo(() => {
    const total = currentPlaylistTracks.length;
    const filtered = filteredTracks.length;
    if (searchQuery && filtered !== total) {
      return `${filtered} of ${total} tracks`;
    }
    return `${total} tracks`;
  }, [currentPlaylistTracks.length, filteredTracks.length, searchQuery]);

  return (
    <div className="track-list-container">
      <div className="track-list-header">
        <div className="search-container">
          <svg className="search-icon" viewBox="0 0 24 24" fill="currentColor">
            <path d="M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z" />
          </svg>
          <input
            type="search"
            className="search-input"
            placeholder="Search tracks..."
            aria-label="Search tracks"
            onChange={handleSearchChange}
          />
          <button
            className={`search-clear ${!searchQuery ? 'hidden' : ''}`}
            aria-label="Clear search"
            onClick={handleClearSearch}
          >
            <svg viewBox="0 0 24 24" fill="currentColor">
              <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
            </svg>
          </button>
        </div>
        
        <div className="sort-container" style={{ position: 'relative', marginLeft: '10px' }}>
          <button 
            ref={sortButtonRef}
            className="sort-button" 
            popoverTarget="sort-menu-popover"
            aria-label="Sort tracks"
            style={{ 
              background: 'transparent', 
              border: 'none', 
              color: 'var(--text-secondary)', 
              cursor: 'pointer',
              display: 'flex',
              alignItems: 'center',
              padding: '8px'
            }}
          >
            <span style={{ marginRight: '4px' }}>Sort</span>
            <svg viewBox="0 0 24 24" fill="currentColor" width="18" height="18">
              <path d="M3 18h6v-2H3v2zM3 6v2h18V6H3zm0 7h12v-2H3v2z" />
            </svg>
          </button>
          <SortMenu 
            id="sort-menu-popover"
            anchorRef={sortButtonRef}
            currentOption={sortOption} 
            currentDirection={sortDirection} 
            onSelect={(opt, dir) => {
              setSortOption(opt);
              setSortDirection(dir);
            }} 
          />
        </div>

        <div className="track-count">{trackCountText}</div>
      </div>

      <div className="track-list-scroll" ref={scrollContainerRef} onScroll={handleScroll}>
        <div className="virtual-content" style={{ height: totalHeight }}>
          <div className="track-list" style={{ transform: `translateY(${visibleRange.start * ITEM_HEIGHT}px)` }}>
            {visibleTracks.map((track, i) => {
              const originalIndex = currentPlaylistTracks.indexOf(track);
              const isCached = cachedTrackIds.has(track.id);
              const isPlaying = track.id === playerState.currentTrack?.id &&
                currentPlaylistId === playingPlaylistId;
              const isAvailable = isOnline || isCached;

              return (
                <TrackItem
                  key={`${track.id}-${visibleRange.start + i}`}
                  track={track}
                  index={originalIndex}
                  isCached={isCached}
                  isPlaying={isPlaying}
                  isPlayerPlaying={playerState.isPlaying}
                  isAvailable={isAvailable}
                  settings={settings}
                  onDoubleClick={() => handleTrackDoubleClick(track, originalIndex)}
                  onContextMenu={(e) => handleContextMenu(e, track, originalIndex)}
                  onDragStart={(e) => handleDragStart(e, track)}
                  onTogglePlay={() => {
                    if (isPlaying) {
                      playerActions.togglePlayPause();
                    } else {
                      playTrack(track, originalIndex, filteredTracks);
                    }
                  }}
                />
              );
            })}
          </div>
        </div>
      </div>

      {contextMenu && (
        <ContextMenu
          x={contextMenu.x}
          y={contextMenu.y}
          track={contextMenu.track}
          index={contextMenu.index}
          isCached={cachedTrackIds.has(contextMenu.track.id)}
          onPlay={() => {
            playTrack(contextMenu.track, contextMenu.index, filteredTracks);
            setContextMenu(null);
          }}
          onAddToQueue={() => {
            playerActions.addToQueue(contextMenu.track, currentPlaylistId!, contextMenu.index);
            showToast(`Added "${contextMenu.track.title}" to queue`);
            setContextMenu(null);
          }}
          onDownload={() => {
            downloadTrack(contextMenu.track);
            setContextMenu(null);
          }}
          onDelete={() => {
            deleteDownloadedTrack(contextMenu.track);
            setContextMenu(null);
          }}
          onRemoveFromPlaylist={
            currentPlaylistId && !currentPlaylistId.startsWith('virtual:')
              ? () => {
                  removeTrackFromPlaylist(currentPlaylistId, contextMenu.index);
                  setContextMenu(null);
                }
              : undefined
          }
        />
      )}
    </div>
  );
}

interface TrackItemProps {
  track: TrackInfo;
  index: number;
  isCached: boolean;
  isPlaying: boolean;
  isPlayerPlaying: boolean;
  isAvailable: boolean;
  settings: AppSettings;
  onDoubleClick: () => void;
  onContextMenu: (e: React.MouseEvent) => void;
  onDragStart: (e: React.DragEvent) => void;
  onTogglePlay: () => void;
}

function TrackItem({
  track,
  index,
  isCached,
  isPlaying,
  isPlayerPlaying,
  isAvailable,
  settings,
  onDoubleClick,
  onContextMenu,
  onDragStart,
  onTogglePlay,
}: TrackItemProps) {
  const className = [
    'track-item',
    isPlaying && 'playing',
    !isAvailable && 'unavailable',
    isCached && 'cached',
  ].filter(Boolean).join(' ');

  const showReplayGainWarning = settings.showReplayGainWarning && settings.replayGainMode !== 'off';
  const hasTrackGain = track.replayGainTrackGain !== null && track.replayGainTrackGain !== undefined;
  const hasAlbumGain = track.replayGainAlbumGain !== null && track.replayGainAlbumGain !== undefined;

  let isMissingReplayGain = false;
  let replayGainTooltip = '';

  if (showReplayGainWarning) {
    if (settings.replayGainMode === 'track') {
      if (!hasTrackGain) {
        isMissingReplayGain = true;
        replayGainTooltip = hasAlbumGain 
          ? "Missing Track ReplayGain (Album ReplayGain available)" 
          : "Missing Track and Album ReplayGain";
      }
    } else if (settings.replayGainMode === 'album') {
      if (!hasAlbumGain && !hasTrackGain) {
        isMissingReplayGain = true;
        replayGainTooltip = "Missing Track and Album ReplayGain";
      }
    }
  }

  return (
    <div
      className={className}
      draggable={isAvailable}
      onDoubleClick={onDoubleClick}
      onContextMenu={onContextMenu}
      onDragStart={onDragStart}
    >
      <div className="track-index-container">
        <span className="track-index">{index + 1}</span>
        <PlayingIndicator
          isPlaying={isPlaying}
          isPaused={!isPlayerPlaying}
          onTogglePlay={onTogglePlay}
        />
      </div>

      <CoverImage
        trackId={track.id}
        size={48}
        className="track-cover"
        alt=""
      />
      <div className="track-info">
        <span className="track-title">
          {track.title}
          {isMissingReplayGain && (
            <span 
              className="replay-gain-warning" 
              title={replayGainTooltip}
              style={{ marginLeft: '8px', fontSize: '0.8em', cursor: 'help' }}
            >
              ⚠️
            </span>
          )}
        </span>
        <span className="track-artist">
          {track.artists || 'Unknown Artist'} • {track.album || 'Unknown Album'}
        </span>
      </div>
      {isCached && (
        <svg className="cached-icon" viewBox="0 0 24 24" fill="currentColor" aria-label="Available offline">
          <title>Available offline</title>
          <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z" />
        </svg>
      )}
      <span className="track-duration">{formatDuration(track.duration)}</span>
      <button
        className="track-options-btn"
        onClick={(e) => {
          e.stopPropagation();
          onContextMenu(e);
        }}
        aria-label="More options"
      >
        <svg viewBox="0 0 24 24" fill="currentColor">
          <path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z" />
        </svg>
      </button>
    </div>
  );
}

interface ContextMenuProps {
  x: number;
  y: number;
  track: TrackInfo;
  index: number;
  isCached: boolean;
  onPlay: () => void;
  onAddToQueue: () => void;
  onDownload: () => void;
  onDelete: () => void;
  onRemoveFromPlaylist?: () => void;
}

function ContextMenu({ x, y, isCached, onPlay, onAddToQueue, onDownload, onDelete, onRemoveFromPlaylist }: ContextMenuProps) {
  const menuRef = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ x, y });

  useLayoutEffect(() => {
    if (menuRef.current) {
      const rect = menuRef.current.getBoundingClientRect();
      let newX = x;
      let newY = y;

      // Check right edge
      if (newX + rect.width > window.innerWidth) {
        newX = window.innerWidth - rect.width - 10;
      }

      // Check bottom edge
      if (newY + rect.height > window.innerHeight) {
        newY = window.innerHeight - rect.height - 10;
      }

      // Check left edge
      if (newX < 10) {
        newX = 10;
      }

      // Check top edge
      if (newY < 10) {
        newY = 10;
      }

      setPosition({ x: newX, y: newY });
    }
  }, [x, y]);

  return (
    <div ref={menuRef} className="context-menu" style={{ left: position.x, top: position.y }}>
      <button className="context-menu-item" onClick={onPlay}>
        <svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z" /></svg>
        Play
      </button>
      <button className="context-menu-item" onClick={onAddToQueue}>
        <svg viewBox="0 0 24 24" fill="currentColor">
          <path d="M15 6H3v2h12V6zm0 4H3v2h12v-2zM3 16h8v-2H3v2zM17 6v8.18c-.31-.11-.65-.18-1-.18-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3V8h3V6h-5z" />
        </svg>
        Add to Queue
      </button>
      {onRemoveFromPlaylist && (
        <button className="context-menu-item" onClick={onRemoveFromPlaylist}>
          <svg viewBox="0 0 24 24" fill="currentColor">
            <path d="M19 13H5v-2h14v2z" />
          </svg>
          Remove from Playlist
        </button>
      )}
      {!isCached ? (
        <button className="context-menu-item" onClick={onDownload}>
          <svg viewBox="0 0 24 24" fill="currentColor">
            <path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z" />
          </svg>
          Download
        </button>
      ) : (
        <button className="context-menu-item" onClick={onDelete}>
          <svg viewBox="0 0 24 24" fill="currentColor">
            <path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z" />
          </svg>
          Remove Download
        </button>
      )}
    </div>
  );
}

interface SortMenuProps {
  id?: string;
  anchorRef: React.RefObject<HTMLElement | null>;
  currentOption: SortOption;
  currentDirection: SortDirection;
  onSelect: (option: SortOption, direction: SortDirection) => void;
}

function SortMenu({ id, anchorRef, currentOption, currentDirection, onSelect }: SortMenuProps) {
  const menuRef = useRef<HTMLDivElement>(null);
  const options: { label: string; value: SortOption }[] = [
    { label: 'Title', value: 'title' },
    { label: 'Artist', value: 'artist' },
    { label: 'Album', value: 'album' },
    { label: 'Added Date', value: 'added' },
  ];

  useEffect(() => {
    const menu = menuRef.current;
    if (!menu || !anchorRef.current) return;

    const handleToggle = (e: ToggleEvent) => {
      if (e.newState === 'open') {
        const buttonRect = anchorRef.current!.getBoundingClientRect();
        
        // Position below the button, aligned to the right
        menu.style.position = 'fixed';
        menu.style.top = `${buttonRect.bottom + 5}px`;
        menu.style.left = 'auto';
        menu.style.right = `${window.innerWidth - buttonRect.right}px`;
        menu.style.margin = '0';
      }
    };

    menu.addEventListener('toggle', handleToggle as any);
    return () => menu.removeEventListener('toggle', handleToggle as any);
  }, [anchorRef]);

  const handleSelect = (opt: SortOption, dir: SortDirection) => {
    onSelect(opt, dir);
    if (id) {
      const popover = document.getElementById(id);
      if (popover && 'hidePopover' in popover) {
        (popover as any).hidePopover();
      }
    }
  };

  return (
    <div 
      ref={menuRef}
      id={id}
      popover="auto"
      className="context-menu" 
      style={{ 
        margin: 0,
        minWidth: '200px', 
        zIndex: 100,
        position: 'fixed'
      }}
    >
      <div style={{ padding: '8px 16px', fontSize: '12px', color: 'var(--text-tertiary)', fontWeight: 600 }}>
        Sort by
      </div>
      {options.map(opt => (
        <button 
          key={opt.value}
          className="context-menu-item" 
          onClick={() => {
            if (currentOption === opt.value) {
              // Toggle direction
              handleSelect(opt.value, currentDirection === 'asc' ? 'desc' : 'asc');
            } else {
              // Default directions: Added -> Desc, Others -> Asc
              const defaultDir = opt.value === 'added' ? 'desc' : 'asc';
              handleSelect(opt.value, defaultDir);
            }
          }}
          style={{ justifyContent: 'space-between' }}
        >
          <span>{opt.label}</span>
          {currentOption === opt.value && (
            <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16" style={{ transform: currentDirection === 'desc' ? 'rotate(180deg)' : 'none' }}>
              <path d="M7 14l5-5 5 5z" />
            </svg>
          )}
        </button>
      ))}
    </div>
  );
}

