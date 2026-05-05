import { useEffect, useRef } from 'react';
import { useRegisterSW } from 'virtual:pwa-register/react';

interface ServiceWorkerUpdateState {
  needRefresh: boolean;
  updateServiceWorker: (reloadPage?: boolean) => Promise<void>;
  offlineReady: boolean;
}

export function useServiceWorkerUpdate(): ServiceWorkerUpdateState {
  const updateIntervalRef = useRef<number | null>(null);

  useEffect(() => {
    return () => {
      if (updateIntervalRef.current !== null) {
        window.clearInterval(updateIntervalRef.current);
        updateIntervalRef.current = null;
      }
    };
  }, []);

  const {
    needRefresh: [needRefresh],
    offlineReady: [offlineReady],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(swUrl: string, registration: ServiceWorkerRegistration | undefined) {
      console.log('Service Worker registered:', swUrl);

      // Check for updates periodically (every hour)
      if (registration) {
        if (updateIntervalRef.current !== null) {
          window.clearInterval(updateIntervalRef.current);
        }

        updateIntervalRef.current = window.setInterval(() => {
          if (!navigator.onLine || document.hidden) {
            return;
          }
          registration.update();
        }, 60 * 60 * 1000);
      }
    },
    onRegisterError(error: Error) {
      console.error('Service Worker registration error:', error);
    },
  });

  return {
    needRefresh,
    updateServiceWorker,
    offlineReady,
  };
}
