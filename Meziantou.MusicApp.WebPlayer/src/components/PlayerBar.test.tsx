import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PlayerBar } from './PlayerBar';

const useAppMock = vi.fn();
const usePlayerMock = vi.fn();

vi.mock('../hooks', () => ({
  useApp: () => useAppMock(),
  usePlayer: () => usePlayerMock(),
}));

vi.mock('../services', () => ({
  audioPlayer: {
    getCurrentTime: vi.fn(() => 0),
    getDuration: vi.fn(() => 0),
    on: vi.fn(),
    off: vi.fn(),
  },
}));

vi.mock('./CoverImage', () => ({
  CoverImage: ({ showPlaceholderWhenHidden }: { showPlaceholderWhenHidden?: boolean }) => (
    <div data-testid="player-cover-placeholder-optin">{String(showPlaceholderWhenHidden)}</div>
  ),
}));

describe('PlayerBar', () => {
  beforeEach(() => {
    useAppMock.mockReturnValue({
      currentPlaylistId: null,
      selectPlaylist: vi.fn().mockResolvedValue(undefined),
      playlists: [],
    });

    usePlayerMock.mockReturnValue({
      playerState: {
        currentTrack: null,
        currentQuality: null,
        isPlaying: false,
        shuffleEnabled: false,
        repeatMode: 'off',
        queue: [],
        isAirPlayAvailable: false,
        isAirPlayActive: false,
        volume: 1,
        isMuted: false,
      },
      playerActions: {
        seek: vi.fn(),
        previous: vi.fn(),
        next: vi.fn(),
        setShuffle: vi.fn(),
        togglePlayPause: vi.fn(),
        cycleRepeatMode: vi.fn(),
        toggleMute: vi.fn(),
        setVolume: vi.fn(),
        getCurrentPlaylistId: vi.fn(() => null),
        showAirPlayPicker: vi.fn(),
      },
    });
  });

  it('enables placeholder opt-in for the cover image', () => {
    const container = document.createElement('div');
    const root = createRoot(container);

    act(() => {
      root.render(<PlayerBar onQueueClick={() => undefined} />);
    });

    const element = container.querySelector('[data-testid="player-cover-placeholder-optin"]');
    expect(element).not.toBeNull();
    expect(element).toHaveTextContent('true');

    act(() => {
      root.unmount();
    });
  });
});
