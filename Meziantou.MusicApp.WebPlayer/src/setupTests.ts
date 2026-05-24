import { vi } from 'vitest';

// Enable React act() support for createRoot-based tests.
(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

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

Object.defineProperty(window.HTMLMediaElement.prototype, 'webkitShowPlaybackTargetPicker', {
  configurable: true,
  value: vi.fn(),
});

Object.defineProperty(window.HTMLMediaElement.prototype, 'webkitCurrentPlaybackTargetIsWireless', {
  configurable: true,
  writable: true,
  value: false,
});

// Mock URL.createObjectURL and revokeObjectURL
globalThis.URL.createObjectURL = vi.fn(() => 'blob:mock-url');
globalThis.URL.revokeObjectURL = vi.fn();

// Mock AudioContext
class MockAudioContext {
  public readonly destination = {};
  public readonly state: AudioContextState = 'running';

  createGain() {
    return {
      connect: vi.fn(),
      gain: { value: 1 },
    };
  }

  createMediaElementSource() {
    return {
      connect: vi.fn(),
    };
  }

  createBiquadFilter() {
    return {
      connect: vi.fn(),
      gain: { value: 0 },
      frequency: { value: 0 },
      Q: { value: 1 },
      type: 'peaking' as BiquadFilterType,
    };
  }

  async resume() {
    return undefined;
  }
}

window.AudioContext = MockAudioContext as unknown as typeof AudioContext;

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
