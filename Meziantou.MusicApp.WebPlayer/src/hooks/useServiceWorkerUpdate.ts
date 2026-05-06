import { useCallback } from 'react';
import { useRegisterSW } from 'virtual:pwa-register/react';

interface ServiceWorkerUpdateState {
  needRefresh: boolean;
  updateServiceWorker: (reloadPage?: boolean) => Promise<void>;
  offlineReady: boolean;
  checkForUpdate: () => Promise<boolean>;
}

// Module-level so that multiple hook consumers share the registration and
// the auto-update interval is set up exactly once for the lifetime of the page.
let sharedRegistration: ServiceWorkerRegistration | null = null;
let intervalHandle: number | null = null;

function ensureAutoUpdateInterval(registration: ServiceWorkerRegistration): void {
  sharedRegistration = registration;
  if (intervalHandle !== null) return;
  intervalHandle = window.setInterval(() => {
    if (!navigator.onLine || document.hidden) return;
    registration.update();
  }, 60 * 60 * 1000);
}

export function useServiceWorkerUpdate(): ServiceWorkerUpdateState {
  const {
    needRefresh: [needRefresh],
    offlineReady: [offlineReady],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(swUrl: string, registration: ServiceWorkerRegistration | undefined) {
      console.log('Service Worker registered:', swUrl);
      if (registration) {
        ensureAutoUpdateInterval(registration);
      }
    },
    onRegisterError(error: Error) {
      console.error('Service Worker registration error:', error);
    },
  });

  const checkForUpdate = useCallback(async (): Promise<boolean> => {
    const registration = sharedRegistration;
    if (!registration) return false;
    try {
      await registration.update();
      return true;
    } catch (error) {
      console.error('Service Worker manual update failed:', error);
      return false;
    }
  }, []);

  return {
    needRefresh,
    updateServiceWorker,
    offlineReady,
    checkForUpdate,
  };
}
