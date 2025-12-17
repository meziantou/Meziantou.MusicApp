interface PlayingIndicatorProps {
  isPlaying: boolean;
  isPaused: boolean;
  onTogglePlay?: () => void;
}

export function PlayingIndicator({ isPlaying, isPaused, onTogglePlay }: PlayingIndicatorProps) {
  return (
    <>
      {(
        <button
          className="track-play-pause-btn"
          onClick={(e) => {
            e.stopPropagation();
            onTogglePlay?.();
          }}
          aria-label={isPlaying && !isPaused ? "Pause" : "Play"}
        >
          {isPlaying && !isPaused ? (
            <svg viewBox="0 0 24 24" fill="currentColor" width="24" height="24">
              <path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z" />
            </svg>
          ) : (
            <svg viewBox="0 0 24 24" fill="currentColor" width="24" height="24">
              <path d="M8 5v14l11-7z" />
            </svg>
          )}
        </button>
      )}
      {isPlaying && (
        <div className={`playing-animation ${isPaused ? 'paused' : ''}`}>
          <span></span><span></span><span></span>
        </div>
      )}
    </>
  );
}
