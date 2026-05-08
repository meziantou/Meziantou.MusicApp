import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_SETTINGS } from '../constants';
import { SettingsDialog } from './SettingsDialog';

const useAppMock = vi.fn();
const useServiceWorkerUpdateMock = vi.fn();
const getScanStatusMock = vi.fn();

vi.mock('../hooks', () => ({
  useApp: () => useAppMock(),
  useServiceWorkerUpdate: () => useServiceWorkerUpdateMock(),
}));

vi.mock('../services', () => ({
  getApiService: vi.fn(() => ({
    getScanStatus: getScanStatusMock,
  })),
}));

describe('SettingsDialog', () => {
  beforeEach(() => {
    useAppMock.mockReturnValue({
      settings: {
        ...DEFAULT_SETTINGS,
        serverUrl: 'https://example.test',
      },
      updateSettings: vi.fn().mockResolvedValue(undefined),
      testConnection: vi.fn().mockResolvedValue(true),
      triggerLibraryScan: vi.fn().mockResolvedValue(undefined),
      cleanupTranscodingCache: vi.fn().mockResolvedValue(undefined),
      isOnline: true,
    });

    useServiceWorkerUpdateMock.mockReturnValue({
      checkForUpdate: vi.fn().mockResolvedValue(true),
      needRefresh: false,
      updateServiceWorker: vi.fn(),
    });
  });

  it('shows scan progression when the server is scanning', async () => {
    getScanStatusMock.mockResolvedValue({
      isScanning: true,
      isInitialScanCompleted: true,
      scanCount: 12,
      lastScanDate: '2026-01-02T12:00:00Z',
      percentage: 42,
      estimatedCompletionTime: '2026-01-02T12:15:00Z',
      invalidPlaylists: [],
    });

    const container = document.createElement('div');
    const root = createRoot(container);

    await act(async () => {
      root.render(
        <SettingsDialog
          isOpen
          onClose={() => undefined}
          onOpenDiagnostics={() => undefined}
        />,
      );
    });

    expect(container.querySelector('.scan-progress')).not.toBeNull();
    expect(container).toHaveTextContent('Library scan in progress');
    expect(container).toHaveTextContent('42%');
    expect(container).toHaveTextContent('Estimated completion:');
    expect(container.querySelector('.scan-progress-bar')).not.toHaveClass('indeterminate');
    expect(container.querySelector('[role="progressbar"]')).toHaveAttribute('aria-valuenow', '42');

    await act(async () => {
      root.unmount();
    });
  });

  it('shows an indeterminate progress indicator when the scan percentage is unknown', async () => {
    getScanStatusMock.mockResolvedValue({
      isScanning: true,
      isInitialScanCompleted: true,
      scanCount: 12,
      lastScanDate: '2026-01-02T12:00:00Z',
      percentage: null,
      estimatedCompletionTime: null,
      invalidPlaylists: [],
    });

    const container = document.createElement('div');
    const root = createRoot(container);

    await act(async () => {
      root.render(
        <SettingsDialog
          isOpen
          onClose={() => undefined}
          onOpenDiagnostics={() => undefined}
        />,
      );
    });

    expect(container.querySelector('.scan-progress')).not.toBeNull();
    expect(container).toHaveTextContent('In progress');
    expect(container.querySelector('.scan-progress-bar')).toHaveClass('indeterminate');
    expect(container.querySelector('[role="progressbar"]')).not.toHaveAttribute('aria-valuenow');

    await act(async () => {
      root.unmount();
    });
  });
});
