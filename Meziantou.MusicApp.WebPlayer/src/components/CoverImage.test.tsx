import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CoverImage } from './CoverImage';

const useAppMock = vi.fn();

vi.mock('../hooks', () => ({
  useApp: () => useAppMock(),
}));

vi.mock('../services', () => ({
  getApiService: vi.fn(() => ({
    getSongCoverUrl: vi.fn(),
    getCoverHeaders: vi.fn(() => ({})),
  })),
  storageService: {
    getCachedCover: vi.fn(),
    isCoverMissing: vi.fn(),
    saveCachedCover: vi.fn(),
    addMissingCover: vi.fn(),
  },
}));

vi.mock('../utils', () => ({
  getNetworkType: vi.fn(() => 'wifi'),
}));

describe('CoverImage', () => {
  beforeEach(() => {
    useAppMock.mockReturnValue({
      cachedTrackIds: new Set<string>(),
      settings: { hideCoverArt: true },
    });
  });

  it('hides image when cover art is disabled and no opt-in is provided', () => {
    const container = document.createElement('div');
    const root = createRoot(container);

    act(() => {
      root.render(<CoverImage trackId="track-1" size={64} alt="cover" />);
    });
    expect(container.querySelector('img')).toBeNull();

    act(() => {
      root.unmount();
    });
  });

  it('shows placeholder when cover art is disabled and opt-in is enabled', () => {
    const container = document.createElement('div');
    const root = createRoot(container);

    act(() => {
      root.render(<CoverImage trackId="track-1" size={64} alt="cover" showPlaceholderWhenHidden />);
    });

    const image = container.querySelector('img');
    expect(image).not.toBeNull();
    expect(image?.getAttribute('src')).toContain('data:image/svg+xml');

    act(() => {
      root.unmount();
    });
  });
});
