import { useState, useEffect } from 'react';
import type { TrackInfo } from '../types';
import { formatDuration } from '../utils';
import { getApiService } from '../services';
import { CoverImage } from './CoverImage';

interface SongDetailsDialogProps {
  track: TrackInfo | null;
  onClose: () => void;
}

export function SongDetailsDialog({ track, onClose }: SongDetailsDialogProps) {
  const [lyrics, setLyrics] = useState<string | null>(null);
  const [lyricsLoading, setLyricsLoading] = useState(false);
  const [lyricsError, setLyricsError] = useState<string | null>(null);

  useEffect(() => {
    if (!track) {
      setLyrics(null);
      setLyricsError(null);
      return;
    }

    const fetchLyrics = async () => {
      setLyricsLoading(true);
      setLyricsError(null);
      try {
        const api = getApiService();
        const response = await api.getSongLyrics(track.id);
        setLyrics(response.lyrics);
      } catch (err) {
        setLyricsError('Failed to load lyrics');
        console.error('Error fetching lyrics:', err);
      } finally {
        setLyricsLoading(false);
      }
    };

    fetchLyrics();
  }, [track]);

  if (!track) return null;

  const formatDate = (dateStr: string | null): string => {
    if (!dateStr) return 'Unknown';
    try {
      const date = new Date(dateStr);
      return date.toLocaleDateString(undefined, {
        year: 'numeric',
        month: 'long',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
      });
    } catch {
      return dateStr;
    }
  };

  const formatSize = (bytes: number): string => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const formatBitRate = (bitRate: number | null): string => {
    if (!bitRate) return 'Unknown';
    return `${bitRate} kbps`;
  };

  return (
    <div className="dialog-overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog song-details-dialog" role="dialog" aria-labelledby="song-details-title">
        <div className="dialog-header">
          <h2 id="song-details-title">Song Details</h2>
          <button className="icon-button close-btn" aria-label="Close" onClick={onClose}>
            <svg viewBox="0 0 24 24" fill="currentColor">
              <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
            </svg>
          </button>
        </div>

        <div className="dialog-content">
          <div className="song-details-header">
            <CoverImage
              trackId={track.id}
              size={120}
              className="song-details-cover"
              alt={`${track.album || 'Album'} cover`}
            />
            <div className="song-details-title-section">
              <h3 className="song-details-track-title">{track.title}</h3>
              <p className="song-details-artist">{track.artists || 'Unknown Artist'}</p>
              <p className="song-details-album">{track.album || 'Unknown Album'}</p>
            </div>
          </div>

          <section className="song-details-section">
            <h4>Track Information</h4>
            <dl className="song-details-list">
              <div className="song-details-item">
                <dt>Duration</dt>
                <dd>{formatDuration(track.duration)}</dd>
              </div>
              {track.track && (
                <div className="song-details-item">
                  <dt>Track Number</dt>
                  <dd>{track.track}</dd>
                </div>
              )}
              {track.year && (
                <div className="song-details-item">
                  <dt>Year</dt>
                  <dd>{track.year}</dd>
                </div>
              )}
              {track.genre && (
                <div className="song-details-item">
                  <dt>Genre</dt>
                  <dd>{track.genre}</dd>
                </div>
              )}
              {track.isrc && (
                <div className="song-details-item">
                  <dt>ISRC</dt>
                  <dd>{track.isrc}</dd>
                </div>
              )}
            </dl>
          </section>

          <section className="song-details-section">
            <h4>File Information</h4>
            <dl className="song-details-list">
              <div className="song-details-item">
                <dt>Relative Path</dt>
                <dd className="song-details-path">{track.path}</dd>
              </div>
              <div className="song-details-item">
                <dt>Added On</dt>
                <dd>{formatDate(track.addedDate)}</dd>
              </div>
              <div className="song-details-item">
                <dt>Size</dt>
                <dd>{formatSize(track.size)}</dd>
              </div>
              <div className="song-details-item">
                <dt>Bit Rate</dt>
                <dd>{formatBitRate(track.bitRate)}</dd>
              </div>
              {track.contentType && (
                <div className="song-details-item">
                  <dt>Format</dt>
                  <dd>{track.contentType}</dd>
                </div>
              )}
            </dl>
          </section>

          {(track.replayGainTrackGain != null || track.replayGainAlbumGain != null) && (
            <section className="song-details-section">
              <h4>ReplayGain</h4>
              <dl className="song-details-list">
                {track.replayGainTrackGain != null && (
                  <div className="song-details-item">
                    <dt>Track Gain</dt>
                    <dd>{track.replayGainTrackGain.toFixed(2)} dB</dd>
                  </div>
                )}
                {track.replayGainTrackPeak != null && (
                  <div className="song-details-item">
                    <dt>Track Peak</dt>
                    <dd>{track.replayGainTrackPeak.toFixed(4)}</dd>
                  </div>
                )}
                {track.replayGainAlbumGain != null && (
                  <div className="song-details-item">
                    <dt>Album Gain</dt>
                    <dd>{track.replayGainAlbumGain.toFixed(2)} dB</dd>
                  </div>
                )}
                {track.replayGainAlbumPeak != null && (
                  <div className="song-details-item">
                    <dt>Album Peak</dt>
                    <dd>{track.replayGainAlbumPeak.toFixed(4)}</dd>
                  </div>
                )}
              </dl>
            </section>
          )}

          <section className="song-details-section song-details-lyrics-section">
            <h4>Lyrics</h4>
            {lyricsLoading ? (
              <div className="song-details-lyrics-loading">Loading lyrics...</div>
            ) : lyricsError ? (
              <div className="song-details-lyrics-error">{lyricsError}</div>
            ) : lyrics ? (
              <pre className="song-details-lyrics">{lyrics}</pre>
            ) : (
              <div className="song-details-lyrics-empty">No lyrics available</div>
            )}
          </section>
        </div>

        <div className="dialog-footer">
          <button className="primary-button" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}
