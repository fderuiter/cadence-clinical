const CACHE_NAME = "portal-cache-v1";
const ASSETS = [
  "/subject-portal/",
  "/subject-portal/index.html",
  "/subject-portal/src/style.css",
  "/subject-portal/src/index.js",
  "/subject-portal/manifest.json",
];
const DEMO_SHELL_ASSETS = [
  "/subject-portal/",
  "/subject-portal/index.html",
  "/subject-portal/manifest.json",
];
const IS_MSW_MODE =
  typeof importScripts === "function" &&
  typeof self !== "undefined" &&
  typeof self.location?.search === "string" &&
  new URLSearchParams(self.location.search).get("msw") === "1";

// Helper to fetch with timeout
function fetchWithTimeout(request, timeoutMs) {
  return new Promise((resolve, reject) => {
    const timeoutId = setTimeout(() => {
      reject(new Error("Network timeout"));
    }, timeoutMs);

    fetch(request)
      .then((response) => {
        clearTimeout(timeoutId);
        resolve(response);
      })
      .catch((error) => {
        clearTimeout(timeoutId);
        reject(error);
      });
  });
}

// Install event: Pre-cache core shell resources
self.addEventListener("install", (event) => {
  const preCacheAssets = IS_MSW_MODE ? DEMO_SHELL_ASSETS : ASSETS;
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      console.log("[Service Worker] Pre-caching offline assets");
      return cache.addAll(preCacheAssets).catch((err) => {
        console.warn(
          "[Service Worker] Some pre-cache assets could not be retrieved during install:",
          err
        );
      });
    })
  );
  self.skipWaiting();
});

// Activate event: Clean up old caches while preserving MSW client-tracking caches
self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) => {
      return Promise.all(
        keys.map((key) => {
          if (key !== CACHE_NAME && !key.startsWith("msw:")) {
            console.log("[Service Worker] Removing old cache:", key);
            return caches.delete(key);
          }
        })
      );
    })
  );
  self.clients.claim();
});

// Fetch event: Network-First falling back to Cache for static shell assets;
// in MSW mode, non-static/API calls delegate directly to mockServiceWorker.js.
self.addEventListener("fetch", (event) => {
  if (event.request.method !== "GET") {
    return;
  }

  const url = new URL(event.request.url);

  // Skip non-http/https protocols (e.g., chrome-extension)
  if (!url.protocol.startsWith("http")) {
    return;
  }

  const isStaticAsset =
    url.origin === self.location.origin &&
    url.pathname.startsWith("/subject-portal/") &&
    !/(^|\/)api\//.test(url.pathname) &&
    !url.pathname.endsWith("/mockServiceWorker.js");

  if (isStaticAsset) {
    // Limit network requests for application shell assets to a maximum of 2 seconds before racing to the local cache fallback
    event.respondWith(
      fetchWithTimeout(event.request, 2000)
        .then((response) => {
          // If response is valid, update the cache dynamically
          if (
            response &&
            response.status === 200 &&
            response.type === "basic"
          ) {
            const responseToCache = response.clone();
            caches.open(CACHE_NAME).then((cache) => {
              cache.put(event.request, responseToCache);
            });
          }
          return response;
        })
        .catch(() => {
          // Fallback to cache on network failure or timeout
          console.log(
            "[Service Worker] Network failed or timed out, serving from cache:",
            event.request.url
          );
          return caches.match(event.request).then((cachedResponse) => {
            if (cachedResponse) {
              return cachedResponse;
            }
            return caches.match(url.pathname).then((pathMatchedResponse) => {
              if (pathMatchedResponse) {
                return pathMatchedResponse;
              }
              // If even cache fails (e.g., first time visiting a non-cached route while offline)
              if (event.request.mode === "navigate") {
                return caches
                  .match("/subject-portal/index.html")
                  .then(
                    (shellResponse) =>
                      shellResponse || caches.match("/subject-portal/")
                  );
              }
            });
          });
        })
    );
    if (IS_MSW_MODE && typeof event.stopImmediatePropagation === "function") {
      event.stopImmediatePropagation();
    }
  } else if (!IS_MSW_MODE) {
    // Uncached dynamic API calls bypass this static asset timeout behavior to avoid returning stale data.
    event.respondWith(fetch(event.request));
  }
});

if (IS_MSW_MODE) {
  importScripts("./mockServiceWorker.js");
}
