import '@testing-library/jest-dom';
import { vi } from 'vitest';

// Mock HTMLMediaElement
Object.defineProperty(window.HTMLMediaElement.prototype, 'play', {
  configurable: true,
  value: vi.fn().mockResolvedValue(undefined),
});

Object.defineProperty(window.HTMLMediaElement.prototype, 'pause', {
  configurable: true,
  value: vi.fn(),
});

Object.defineProperty(window.HTMLMediaElement.prototype, 'load', {
  configurable: true,
  value: vi.fn(),
});

// Mock URL.createObjectURL and revokeObjectURL
globalThis.URL.createObjectURL = vi.fn(() => 'blob:mock-url');
globalThis.URL.revokeObjectURL = vi.fn();

// Mock AudioContext
window.AudioContext = vi.fn().mockImplementation(() => ({
  createGain: vi.fn().mockReturnValue({
    connect: vi.fn(),
    gain: { value: 1 }
  }),
  createMediaElementSource: vi.fn().mockReturnValue({
    connect: vi.fn()
  }),
  destination: {}
}));

// Mock fetch
globalThis.fetch = vi.fn().mockResolvedValue({
  ok: true,
  blob: () => Promise.resolve(new Blob(['mock-audio-data'], { type: 'audio/mp3' })),
});

// Mock MediaSession
globalThis.MediaMetadata = class MediaMetadata {
  title: string;
  artist: string;
  album: string;
  artwork: { src: string; sizes?: string; type?: string }[];

  constructor(init: any) {
    this.title = init.title;
    this.artist = init.artist;
    this.album = init.album;
    this.artwork = init.artwork;
  }
};

Object.defineProperty(navigator, 'mediaSession', {
  writable: true,
  value: {
    metadata: null,
    playbackState: 'none',
    setActionHandler: vi.fn(),
    setPositionState: vi.fn(),
  },
});
