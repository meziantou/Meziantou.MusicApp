import { useState, useEffect } from 'react';
import { storageService } from '../services';
import { useApp } from '../hooks';
import { formatBytes } from '../utils';

interface CacheDiagnosticsDialogProps {
  isOpen: boolean;
  onClose: () => void;
}

export function CacheDiagnosticsDialog({ isOpen, onClose }: CacheDiagnosticsDialogProps) {
  const { syncPlaylists, showToast, clearAllCachedTracks } = useApp();
  const [storageUsage, setStorageUsage] = useState<{ usage: number; quota: number } | null>(null);
  const [isClearing, setIsClearing] = useState(false);

  useEffect(() => {
    if (isOpen) {
      loadStorageEstimate();
    }
  }, [isOpen]);

  const loadStorageEstimate = async () => {
    const estimate = await storageService.getStorageEstimate();
    setStorageUsage(estimate);
  };

  const handleClearCovers = async () => {
    if (!confirm('Are you sure you want to clear all cached cover images? They will be re-downloaded as needed.')) {
      return;
    }

    setIsClearing(true);
    try {
      await storageService.clearCovers();
      showToast('Cover cache cleared', 'success');
      await loadStorageEstimate();
    } catch (error) {
      console.error('Failed to clear covers:', error);
      showToast('Failed to clear cover cache', 'error');
    } finally {
      setIsClearing(false);
    }
  };

  const handleClearSongs = async () => {
    if (!confirm('Are you sure you want to clear all downloaded songs? You will need to download them again for offline playback.')) {
      return;
    }

    setIsClearing(true);
    try {
      await clearAllCachedTracks();
      await loadStorageEstimate();
    } catch (error) {
      console.error('Failed to clear songs:', error);
      showToast('Failed to clear song cache', 'error');
    } finally {
      setIsClearing(false);
    }
  };

  const handleCleanupOrphans = async () => {
    setIsClearing(true);
    try {
      const count = await storageService.cleanupOrphanedTracks();
      showToast(`Removed ${count} orphaned tracks`, 'success');
      await loadStorageEstimate();
    } catch (error) {
      console.error('Failed to cleanup orphaned tracks:', error);
      showToast('Failed to cleanup orphaned tracks', 'error');
    } finally {
      setIsClearing(false);
    }
  };

  const handleRefreshPlaylists = async () => {
    setIsClearing(true);
    try {
      await syncPlaylists();
      showToast('Playlists refreshed', 'success');
    } catch (error) {
      console.error('Failed to refresh playlists:', error);
      showToast('Failed to refresh playlists', 'error');
    } finally {
      setIsClearing(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div className="dialog-overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog settings-dialog" role="dialog" aria-labelledby="diagnostics-title">
        <div className="dialog-header">
          <h2 id="diagnostics-title">Cache Diagnostics</h2>
          <button className="icon-button close-btn" aria-label="Close" onClick={onClose}>
            <svg viewBox="0 0 24 24" fill="currentColor">
              <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
            </svg>
          </button>
        </div>

        <div className="dialog-content">
          <section className="settings-section">
            <h3>Storage Usage</h3>
            {storageUsage ? (
              <div className="storage-info">
                <div className="storage-bar-container">
                  <div 
                    className="storage-bar-fill" 
                    style={{ width: `${(storageUsage.usage / storageUsage.quota) * 100}%` }}
                  />
                </div>
                <div className="storage-details">
                  <span>Used: {formatBytes(storageUsage.usage)}</span>
                  <span>Quota: {formatBytes(storageUsage.quota)}</span>
                </div>
              </div>
            ) : (
              <p>Loading storage usage...</p>
            )}
          </section>

          <section className="settings-section">
            <h3>Actions</h3>
            
            <div className="form-group">
              <button 
                className="secondary-button" 
                onClick={handleClearSongs}
                disabled={isClearing}
              >
                Clear Downloaded Songs
              </button>
              <small>Removes all offline tracks to free up space.</small>
            </div>

            <div className="form-group">
              <button 
                className="secondary-button" 
                onClick={handleClearCovers}
                disabled={isClearing}
              >
                Clear Cover Cache
              </button>
              <small>Removes all cached album art images. They will be downloaded again when needed.</small>
            </div>

            <div className="form-group">
              <button 
                className="secondary-button" 
                onClick={handleCleanupOrphans}
                disabled={isClearing}
              >
                Cleanup Orphaned Tracks
              </button>
              <small>Removes tracks that are not part of any cached playlist.</small>
            </div>

            <div className="form-group">
              <button 
                className="secondary-button" 
                onClick={handleRefreshPlaylists}
                disabled={isClearing}
              >
                Force Refresh Playlists
              </button>
              <small>Forces a full synchronization of playlists from the server.</small>
            </div>
          </section>
        </div>
        
        <div className="dialog-footer">
          <button className="primary-button" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}
