/*
 * LEAP shell service worker — ZERO CACHING BY DESIGN (D-21).
 *
 * Its ONLY purpose is to make the phone add-to-home-screen ("install")
 * prompt work — a PWA needs a registered service worker to be installable.
 * It deliberately caches NOTHING: no offline support, no asset caching, no
 * response caching of any kind. There is therefore nothing to invalidate —
 * /config.js, /api/*, and auth responses always hit the network fresh.
 *
 * No fetch handler is registered (the preferred zero-caching choice): without
 * one, the browser handles every request normally, exactly as if no service
 * worker existed, so runtime config and auth can never be served stale.
 */
self.addEventListener("install", function () {
  self.skipWaiting();
});

self.addEventListener("activate", function (event) {
  event.waitUntil(self.clients.claim());
});
