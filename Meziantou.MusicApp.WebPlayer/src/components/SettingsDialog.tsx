import { useState, useEffect, useRef } from 'react';
import type { AppSettings, StreamingQuality, ReplayGainMode } from '../types';
import { DEFAULT_SETTINGS } from '../constants';
import { useApp } from '../hooks';

const QUALITY_OPTIONS: { label: string; value: StreamingQuality }[] = [
  { label: 'Original (Raw)', value: { format: 'raw' } },
  { label: 'FLAC (Lossless)', value: { format: 'flac' } },
  { label: 'MP3 320kbps', value: { format: 'mp3', maxBitRate: 320 } },
  { label: 'MP3 256kbps', value: { format: 'mp3', maxBitRate: 256 } },
  { label: 'MP3 192kbps', value: { format: 'mp3', maxBitRate: 192 } },
  { label: 'MP3 128kbps', value: { format: 'mp3', maxBitRate: 128 } },
  { label: 'Opus 192kbps', value: { format: 'opus', maxBitRate: 192 } },
  { label: 'Opus 160kbps', value: { format: 'opus', maxBitRate: 160 } },
  { label: 'Opus 128kbps', value: { format: 'opus', maxBitRate: 128 } },
  { label: 'Opus 96kbps', value: { format: 'opus', maxBitRate: 96 } },
  { label: 'Opus 64kbps', value: { format: 'opus', maxBitRate: 64 } },
  { label: 'OGG 192kbps', value: { format: 'ogg', maxBitRate: 192 } },
  { label: 'OGG 128kbps', value: { format: 'ogg', maxBitRate: 128 } },
  { label: 'M4A/AAC 256kbps', value: { format: 'm4a', maxBitRate: 256 } },
  { label: 'M4A/AAC 128kbps', value: { format: 'm4a', maxBitRate: 128 } },
];

interface SettingsDialogProps {
  isOpen: boolean;
  onClose: () => void;
  onOpenDiagnostics?: () => void;
}

export function SettingsDialog({ isOpen, onClose, onOpenDiagnostics }: SettingsDialogProps) {
  const { settings, updateSettings, testConnection } = useApp();

  const [formData, setFormData] = useState<AppSettings>(settings);
  const [connectionStatus, setConnectionStatus] = useState<'idle' | 'testing' | 'success' | 'error'>('idle');
  const settingsRef = useRef(settings);
  const prevIsOpenRef = useRef(isOpen);

  // Update form data when dialog opens or settings change (e.g. initial load)
  useEffect(() => {
    if (isOpen) {
      // If dialog just opened, or if settings changed (e.g. loaded from storage), update form
      // We check if settings actually changed to avoid overwriting user input unnecessarily
      // but for initial load, settings will change from default to loaded
      if (!prevIsOpenRef.current || settings !== settingsRef.current) {
         setFormData(settings);
         if (!prevIsOpenRef.current) {
            setConnectionStatus('idle');
         }
      }
    }
    prevIsOpenRef.current = isOpen;
    settingsRef.current = settings;
  }, [isOpen, settings]);

  if (!isOpen) return null;

  const getQualityIndex = (quality: StreamingQuality): number => {
    return QUALITY_OPTIONS.findIndex(
      opt => opt.value.format === quality.format && opt.value.maxBitRate === quality.maxBitRate
    );
  };

  const handleTestConnection = async () => {
    setConnectionStatus('testing');
    const success = await testConnection();
    setConnectionStatus(success ? 'success' : 'error');
  };

  const handleSave = async () => {
    console.log('[SettingsDialog] handleSave called with formData:', formData);
    await updateSettings(formData);
    console.log('[SettingsDialog] updateSettings completed');
    onClose();
  };

  const handleInputChange = (field: keyof AppSettings, value: string | number | boolean) => {
    setFormData(prev => ({ ...prev, [field]: value }));
  };

  const handleQualityChange = (field: 'normalQuality' | 'lowDataQuality' | 'downloadQuality', index: number) => {
    const quality = QUALITY_OPTIONS[index]?.value ?? DEFAULT_SETTINGS[field];
    setFormData(prev => ({ ...prev, [field]: quality }));
  };

  return (
    <div className="dialog-overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog settings-dialog" role="dialog" aria-labelledby="settings-title">
        <div className="dialog-header">
          <h2 id="settings-title">Settings</h2>
          <button className="icon-button close-btn" aria-label="Close" onClick={onClose}>
            <svg viewBox="0 0 24 24" fill="currentColor">
              <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
            </svg>
          </button>
        </div>

        <div className="dialog-content">
          <section className="settings-section">
            <h3>Server Connection</h3>

            <div className="form-group">
              <label htmlFor="server-url">Server URL</label>
              <input
                type="url"
                id="server-url"
                placeholder="https://your-server.com"
                value={formData.serverUrl}
                onChange={(e) => handleInputChange('serverUrl', e.target.value)}
              />
            </div>

            <div className="form-group">
              <label htmlFor="auth-token">Auth Token (optional)</label>
              <input
                type="password"
                id="auth-token"
                placeholder="Your API token (optional)"
                value={formData.authToken}
                onChange={(e) => handleInputChange('authToken', e.target.value)}
              />
            </div>

            <button
              className="secondary-button test-connection-btn"
              onClick={handleTestConnection}
              disabled={connectionStatus === 'testing'}
            >
              {connectionStatus === 'testing' ? 'Testing...' : 'Test Connection'}
            </button>
            <span className={`connection-status ${connectionStatus === 'success' ? 'success' : connectionStatus === 'error' ? 'error' : ''}`}>
              {connectionStatus === 'success' && '✓ Connected successfully'}
              {connectionStatus === 'error' && '✗ Connection failed'}
            </span>
          </section>

          <section className="settings-section">
            <h3>Streaming Quality</h3>

            <div className="form-group">
              <label htmlFor="normal-quality">Normal Data Quality</label>
              <select
                id="normal-quality"
                value={Math.max(0, getQualityIndex(formData.normalQuality))}
                onChange={(e) => handleQualityChange('normalQuality', parseInt(e.target.value, 10))}
              >
                {QUALITY_OPTIONS.map((opt, index) => (
                  <option key={index} value={index}>{opt.label}</option>
                ))}
              </select>
              <small>Quality when connected to WiFi or Ethernet</small>
            </div>

            <div className="form-group">
              <label htmlFor="low-data-quality">Low Data Quality</label>
              <select
                id="low-data-quality"
                value={Math.max(0, getQualityIndex(formData.lowDataQuality))}
                onChange={(e) => handleQualityChange('lowDataQuality', parseInt(e.target.value, 10))}
              >
                {QUALITY_OPTIONS.map((opt, index) => (
                  <option key={index} value={index}>{opt.label}</option>
                ))}
              </select>
              <small>Quality when on mobile data (lower = less data usage)</small>
            </div>

            <div className="form-group">
              <label htmlFor="download-quality">Download Quality</label>
              <select
                id="download-quality"
                value={Math.max(0, getQualityIndex(formData.downloadQuality))}
                onChange={(e) => handleQualityChange('downloadQuality', parseInt(e.target.value, 10))}
              >
                {QUALITY_OPTIONS.map((opt, index) => (
                  <option key={index} value={index}>{opt.label}</option>
                ))}
              </select>
              <small>Quality for offline cached tracks</small>
            </div>

            <div className="form-group checkbox-group">
              <label>
                <input
                  type="checkbox"
                  id="prevent-download-low-data"
                  checked={formData.preventDownloadOnLowData}
                  onChange={(e) => handleInputChange('preventDownloadOnLowData', e.target.checked)}
                />
                Prevent download on Low Data Mode
              </label>
              <small>When enabled, only cached tracks will play when in Low Data Mode</small>
            </div>
          </section>

          <section className="settings-section">
            <h3>Interface</h3>
            <div className="form-group checkbox-group">
              <label>
                <input
                  type="checkbox"
                  id="hide-cover-art"
                  checked={formData.hideCoverArt}
                  onChange={(e) => handleInputChange('hideCoverArt', e.target.checked)}
                />
                Hide Cover Art
              </label>
              <small>Do not load cover images to save memory and data</small>
            </div>
          </section>

          <section className="settings-section">
            <h3>Playback</h3>

            <div className="form-group checkbox-group">
              <label>
                <input
                  type="checkbox"
                  id="scrobble-enabled"
                  checked={formData.scrobbleEnabled}
                  onChange={(e) => handleInputChange('scrobbleEnabled', e.target.checked)}
                />
                Enable Scrobble
              </label>
              <small>Submit played tracks to the server</small>
            </div>

            <div className="form-group">
              <label htmlFor="replaygain-mode">ReplayGain Mode</label>
              <select
                id="replaygain-mode"
                value={formData.replayGainMode}
                onChange={(e) => handleInputChange('replayGainMode', e.target.value as ReplayGainMode)}
              >
                <option value="off">Off</option>
                <option value="track">Track</option>
                <option value="album">Album</option>
              </select>
              <small>Normalize volume levels across tracks</small>
            </div>

            <div className="form-group">
              <label htmlFor="replaygain-preamp">ReplayGain Preamp</label>
              <div className="range-with-value">
                <input
                  type="range"
                  id="replaygain-preamp"
                  min="-15"
                  max="15"
                  step="1"
                  value={formData.replayGainPreamp}
                  onChange={(e) => handleInputChange('replayGainPreamp', parseInt(e.target.value, 10))}
                />
                <span className="range-value">{formData.replayGainPreamp} dB</span>
              </div>
            </div>

            <div className="form-group checkbox-group">
              <label>
                <input
                  type="checkbox"
                  id="show-replaygain-warning"
                  checked={formData.showReplayGainWarning}
                  onChange={(e) => handleInputChange('showReplayGainWarning', e.target.checked)}
                />
                Show ReplayGain Warning
              </label>
              <small>Show an indicator when a track is missing ReplayGain data</small>
            </div>
          </section>

          {onOpenDiagnostics && (
            <section className="settings-section">
              <h3>Advanced</h3>
              <div className="form-group">
                <button 
                  className="btn btn-secondary" 
                  onClick={onOpenDiagnostics}
                  style={{ width: '100%' }}
                >
                  Cache Diagnostics
                </button>
              </div>
            </section>
          )}
        </div>

        <div className="dialog-footer">
          <button className="secondary-button cancel-btn" onClick={onClose}>
            Cancel
          </button>
          <button className="primary-button save-btn" onClick={handleSave}>
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
