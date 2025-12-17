import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import './styles/main.css';

// Register service worker for PWA (production only).
// In dev, ensure no lingering service worker interferes with Vite HMR.
if ('serviceWorker' in navigator) {
  window.addEventListener('load', async () => {
    if (import.meta.env.DEV) {
      try {
        const registrations = await navigator.serviceWorker.getRegistrations();
        await Promise.all(registrations.map(r => r.unregister()));
      } catch {
        // Ignore cleanup errors in dev
      }
      return;
    }

    try {
      const registration = await navigator.serviceWorker.register('/sw.js', {
        scope: '/'
      });
      console.log('Service Worker registered:', registration.scope);
    } catch (error) {
      console.log('Service Worker registration failed:', error);
    }
  });
}

// PWA install prompt
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

window.addEventListener('beforeinstallprompt', (e) => {
  e.preventDefault();
  // Store the prompt for later use
  const deferredPrompt = e as BeforeInstallPromptEvent;
  console.log('PWA install available', deferredPrompt);
});

// Render the React app
const container = document.getElementById('app');
if (container) {
  const root = createRoot(container);
  root.render(
    <StrictMode>
      <App />
    </StrictMode>
  );
}
