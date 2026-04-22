/// <reference types="vite/client" />
/// <reference types="vite-plugin-pwa/react" />

declare const __COMMIT_HASH__: string;

interface HTMLMediaElement {
  webkitShowPlaybackTargetPicker?: () => void;
  webkitCurrentPlaybackTargetIsWireless?: boolean;
}

interface WebKitPlaybackTargetAvailabilityEvent extends Event {
  availability: 'available' | 'not-available';
}
