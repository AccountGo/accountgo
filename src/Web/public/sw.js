/**
 * Service Worker for AccountGo Multi-Tenant PWA
 *
 * Strategies:
 * - Navigations (HTML): Network-first with offline.html fallback.
 * - Vite content-hashed build assets (/assets/*): Cache-first with asset cache trimming.
 * - API requests (/api/*): Network-only, never cached, bypassing SW cache.
 */

const APP_CACHE_VERSION = 'accountgo-v1';
const SHELL_CACHE = `${APP_CACHE_VERSION}-shell`;
const ASSET_CACHE = `${APP_CACHE_VERSION}-assets`;
const OFFLINE_URL = '/offline.html';
const MAX_ASSET_ENTRIES = 100;

const PRECACHE_ASSETS = [
  OFFLINE_URL,
  '/manifest.webmanifest',
  '/favicon.svg',
  '/accountgo-mark.svg',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(SHELL_CACHE).then((cache) => cache.addAll(PRECACHE_ASSETS))
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(
          keys
            .filter((key) => key !== SHELL_CACHE && key !== ASSET_CACHE)
            .map((key) => caches.delete(key))
        )
      )
      .then(() => self.clients.claim())
  );
});

async function trimCache(cacheName, maxEntries) {
  try {
    const cache = await caches.open(cacheName);
    const keys = await cache.keys();
    if (keys.length <= maxEntries) return;
    await Promise.all(
      keys.slice(0, keys.length - maxEntries).map((key) => cache.delete(key))
    );
  } catch {
    // Ignore cache trimming errors
  }
}

self.addEventListener('fetch', (event) => {
  const { request } = event;
  if (request.method !== 'GET') return;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;

  // Let API and WebSocket endpoints pass straight to network
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hub/')) return;

  // Handle SPA HTML navigations
  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request, { cache: 'no-store' }).catch(async () => {
        const cachedOffline = await caches.match(OFFLINE_URL);
        return cachedOffline || new Response('Offline', { status: 503 });
      })
    );
    return;
  }

  // Cache-first for content-hashed Vite build assets
  if (url.pathname.startsWith('/assets/')) {
    event.respondWith(
      caches.match(request).then((cached) => {
        if (cached) return cached;
        return fetch(request).then((response) => {
          if (response.ok) {
            const clone = response.clone();
            caches
              .open(ASSET_CACHE)
              .then((cache) => cache.put(request, clone))
              .then(() => trimCache(ASSET_CACHE, MAX_ASSET_ENTRIES));
          }
          return response;
        });
      })
    );
  }
});

// Force activate on skip waiting message
self.addEventListener('message', (event) => {
  if (event?.data?.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
});
