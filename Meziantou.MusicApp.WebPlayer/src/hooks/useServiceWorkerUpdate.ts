import { useRegisterSW } from 'virtual:pwa-register/react';

interface ServiceWorkerUpdateState {
  needRefresh: boolean;
  updateServiceWorker: (reloadPage?: boolean) => Promise<void>;
  offlineReady: boolean;
}

export function useServiceWorkerUpdate(): ServiceWorkerUpdateState {
  const {
    needRefresh: [needRefresh],
    offlineReady: [offlineReady],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(swUrl: string, registration: ServiceWorkerRegistration | undefined) {
      console.log('Service Worker registered:', swUrl);

      // Check for updates periodically (every hour)
      if (registration) {
        setInterval(() => {
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
