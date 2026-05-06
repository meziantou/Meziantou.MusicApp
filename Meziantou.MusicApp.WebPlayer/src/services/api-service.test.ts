import { afterEach, describe, it, expect, vi } from 'vitest';
import { ApiService } from './api-service';

describe('ApiService', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('getCoverHeaders prefers modern image formats', () => {
    const service = new ApiService('https://example.com');

    expect(service.getCoverHeaders()).toEqual({
      Accept: 'image/avif,image/webp,image/png,image/jpeg;q=0.8,*/*;q=0.5'
    });
  });

  it('triggerScan adds force query when requested', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        isScanning: false,
        isInitialScanCompleted: true,
        scanCount: 0,
        lastScanDate: null,
        percentage: null,
        estimatedCompletionTime: null,
        invalidPlaylists: [],
      }),
    });
    vi.stubGlobal('fetch', fetchMock);

    const service = new ApiService('https://example.com');
    await service.triggerScan(true);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith(
      'https://example.com/api/scan.json?force=true',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('cleanupTranscodingCache calls cleanup endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        deletedFileCount: 3,
        failedFileCount: 0,
      }),
    });
    vi.stubGlobal('fetch', fetchMock);

    const service = new ApiService('https://example.com');
    await service.cleanupTranscodingCache();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith(
      'https://example.com/api/cache/transcoding/cleanup.json',
      expect.objectContaining({ method: 'POST' }),
    );
  });
});
