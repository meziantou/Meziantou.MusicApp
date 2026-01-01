/// <reference types="vitest" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig({
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/setupTests.ts',
  },
  plugins: [
    react(),
    VitePWA({
      registerType: 'prompt',
      includeAssets: ['favicon.svg', 'apple-touch-icon.svg'],
      manifest: {
        name: 'Meziantou Music App Web Player',
        short_name: 'Music App',
        description: 'A web-based music player with offline support',
        theme_color: '#1a1a2e',
        background_color: '#1a1a2e',
        display: 'standalone',
        orientation: 'any',
        start_url: '/',
        icons: [
          {
            src: 'pwa-192x192.png',
            sizes: '192x192',
            type: 'image/png'
          },
          {
            src: 'pwa-512x512.png',
            sizes: '512x512',
            type: 'image/png'
          },
          {
            src: 'pwa-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'any maskable'
          }
        ],
        shortcuts: [
          {
            name: 'Play',
            url: '/?action=play',
            description: 'Resume playback'
          },
          {
            name: 'Pause',
            url: '/?action=pause',
            description: 'Pause playback'
          },
          {
            name: 'Next',
            url: '/?action=next',
            description: 'Skip to next track'
          },
          {
            name: 'Previous',
            url: '/?action=previous',
            description: 'Go to previous track'
          },
          {
            name: 'Volume Up',
            url: '/?action=volumeup',
            description: 'Increase volume'
          },
          {
            name: 'Volume Down',
            url: '/?action=volumedown',
            description: 'Decrease volume'
          }
        ]
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg,woff2}'],
        runtimeCaching: [
          {
            urlPattern: /^https?:\/\/.*\/api\/songs\/.*\/cover/,
            handler: 'CacheFirst',
            options: {
              cacheName: 'cover-art-cache',
              expiration: {
                maxEntries: 500,
                maxAgeSeconds: 60 * 60 * 24 * 30 // 30 days
              }
            }
          }
        ]
      }
    })
  ],
  build: {
    target: 'ES2022',
    sourcemap: true
  },
  server: {
    port: 3000
  }
});
