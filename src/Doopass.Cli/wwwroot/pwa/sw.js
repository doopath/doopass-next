/* Doopass service worker (PWA offline shell).
 *
 * Served from an embedded resource at /pwa/sw.js with
 * Service-Worker-Allowed: / (set by Program.cs), so the worker may take
 * the whole origin as its scope. Handles only same-origin GET requests;
 * the SignalR and REST traffic (/_blazor, /hubs, /api) is passed through
 * untouched: a cached /api/vault/value?key=... would put a decrypted secret
 * into Cache Storage on disk.
 *
 * Bump CACHE name when served assets change: old caches are evicted on
 * activate. Precache misses are tolerated (allSettled) so one flaky
 * asset can never brick the install.
 */
const CACHE = 'doopass-pwa-v3';
const PRECACHE = [
  '/',
  '/app.css',
  '/theme.js',
  '/app.js',
  '/_framework/blazor.web.js',
  '/pwa/manifest.webmanifest',
  '/pwa/icon-192.png',
  '/pwa/icon-512.png',
  '/pwa/icon-maskable-192.png',
  '/pwa/icon-maskable-512.png',
];

self.addEventListener('install', (event) => {
  event.waitUntil((async () => {
    const cache = await caches.open(CACHE);
    await Promise.allSettled(PRECACHE.map((u) => cache.add(u)));
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    const names = await caches.keys();
    await Promise.all(names.filter((n) => n !== CACHE).map((n) => caches.delete(n)));
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;

  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;
  if (url.pathname.startsWith('/_blazor')) return; // SignalR: never cached
  if (url.pathname.startsWith('/hubs/')) return;    // SignalR hub: never cached
  if (url.pathname.startsWith('/api/')) return;     // vault API: never cached

  // Change-of-page navigations prefer the network so a live server session
  // always wins; the cached shell covers offline starts.
  if (req.mode === 'navigate') {
    event.respondWith((async () => {
      try {
        return await fetch(req);
      } catch (err) {
        const cache = await caches.open(CACHE);
        return (await cache.match(req)) ||
               (await cache.match('/')) ||
               new Response('Offline', { status: 503, headers: { 'Content-Type': 'text/plain' } });
      }
    })());
    return;
  }

  // Other same-origin assets: cache first, fill the cache on miss.
  event.respondWith((async () => {
    const cache = await caches.open(CACHE);
    const cached = await cache.match(req);
    if (cached) return cached;
    try {
      const resp = await fetch(req);
      if (resp && resp.ok && resp.type === 'basic') {
        await cache.put(req, resp.clone());
      }
      return resp;
    } catch (err) {
      return new Response('Offline', { status: 503, headers: { 'Content-Type': 'text/plain' } });
    }
  })());
});
