import { useState, useEffect, useRef } from 'react';
import { getApiService, storageService } from '../services';
import { getNetworkType } from '../utils';
import { useApp } from '../hooks';

const COVER_PLACEHOLDER_SVG = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='#666'><path d='M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z'/></svg>";
const COVER_PLACEHOLDER_DATA_URI = `data:image/svg+xml,${encodeURIComponent(COVER_PLACEHOLDER_SVG)}`;

interface CoverImageProps {
  trackId: string;
  size: number;
  className?: string;
  alt?: string;
  onClick?: () => void;
}

export function CoverImage({
  trackId,
  size,
  className = '',
  alt = '',
  onClick,
}: CoverImageProps) {
  const { cachedTrackIds, settings } = useApp();
  const [coverSrc, setCoverSrc] = useState(COVER_PLACEHOLDER_DATA_URI);
  const coverBlobUrlRef = useRef<string | null>(null);

  useEffect(() => {
    // Cleanup previous blob URL
    if (coverBlobUrlRef.current && coverBlobUrlRef.current.startsWith('blob:')) {
      URL.revokeObjectURL(coverBlobUrlRef.current);
      coverBlobUrlRef.current = null;
    }

    if (!trackId || settings.hideCoverArt) {
      setCoverSrc(COVER_PLACEHOLDER_DATA_URI);
      return;
    }

    let cancelled = false;

    const loadCover = async () => {
      try {
        // 1. Try to get from cache first
        const cachedBlob = await storageService.getCachedCover(trackId);
        if (cachedBlob) {
          if (!cancelled) {
            const blobUrl = URL.createObjectURL(cachedBlob);
            coverBlobUrlRef.current = blobUrl;
            setCoverSrc(blobUrl);
          }
          return;
        }

        // Check if we already know the cover is missing
        if (await storageService.isCoverMissing(trackId)) {
          if (!cancelled) setCoverSrc(COVER_PLACEHOLDER_DATA_URI);
          return;
        }

        // 2. If not in cache, check if we should download it
        const networkType = getNetworkType();
        const isCached = cachedTrackIds.has(trackId);
        
        // If offline and not cached, we can't do anything
        if (!navigator.onLine) {
          if (!cancelled) setCoverSrc(COVER_PLACEHOLDER_DATA_URI);
          return;
        }

        // In low data mode, only download if track is cached or playing (we assume if we are showing cover, we might want it)
        const shouldDownload = networkType !== 'low-data' || isCached;

        if (!shouldDownload) {
          if (!cancelled) setCoverSrc(COVER_PLACEHOLDER_DATA_URI);
          return;
        }

        // 3. Fetch from server
        const api = getApiService();
        const coverUrl = api.getSongCoverUrl(trackId, size);
        
        const response = await fetch(coverUrl, { headers: api.getAuthHeaders() });
        if (!response.ok) {
          if (response.status === 404) {
            storageService.addMissingCover(trackId).catch(console.error);
          }
          throw new Error('Failed to load cover');
        }
        
        const blob = await response.blob();
        
        // 4. Save to cache
        // We don't await this to not block rendering, but we should catch errors
        storageService.saveCachedCover(trackId, blob).catch(console.error);

        if (!cancelled) {
          const blobUrl = URL.createObjectURL(blob);
          coverBlobUrlRef.current = blobUrl;
          setCoverSrc(blobUrl);
        }
      } catch (error) {
        console.error('Error loading cover:', error);
        if (!cancelled) {
          setCoverSrc(COVER_PLACEHOLDER_DATA_URI);
        }
      }
    };

    loadCover();

    return () => {
      cancelled = true;
    };
  }, [trackId, size, cachedTrackIds, settings.hideCoverArt]);

  if (settings.hideCoverArt) {
    return null;
  }

  return (
    <img
      className={className}
      src={coverSrc}
      alt={alt}
      onClick={onClick}
    />
  );
}
