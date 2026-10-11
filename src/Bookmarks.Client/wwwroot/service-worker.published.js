// Offline support for the published app.
// The app shell (framework, DLLs, CSS, JS, icons) is precached on install from the
// build-generated asset manifest, so the app starts with no network at all.
// Book covers (Phase 5) go in a separate, size-capped runtime cache: see "Cover cache" below.

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

// A new version installs in the background and waits; the app shows "Update available"
// and sends this when the user chooses Reload (see js/pwa.js).
self.addEventListener('message', event => {
    if (event.data?.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
// staticwebapp.config.json is read by Azure Static Web Apps and never served back to the browser,
// so precaching it fails the integrity check and aborts the whole install.
const offlineAssetsExclude = [/^service-worker\.js$/, /^staticwebapp\.config\.json$/];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = '/';
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    if (event.request.method === 'GET' && coverUrlPattern.test(event.request.url)) {
        return coverResponse(event);
    }

    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}

// ---------- Cover cache ----------
// Open Library covers, cache-first so shelves keep their covers offline. Only CORS responses are cached
// (the app loads Open Library covers with crossorigin="anonymous"): opaque responses can't be measured and
// Chrome counts each one as ~7 MB of quota. Entries are kept roughly least-recently-used within a byte budget;
// each entry records its size in an X-Bm-Size header so the total can be rebuilt after the worker restarts.

const coverCacheName = 'bm-covers-v1';
const coverUrlPattern = /^https:\/\/covers\.openlibrary\.org\//;
const coverBudgetBytes = 50 * 1024 * 1024;
const coverMaxEntryBytes = 2 * 1024 * 1024;
let coverBytes = null;          // running total; null until counted
const coverTouched = new Set(); // entries already moved to the "recent" end in this worker's lifetime

async function coverResponse(event) {
    const cache = await caches.open(coverCacheName);
    const key = event.request.url;
    const cached = await cache.match(key);
    if (cached) {
        if (!coverTouched.has(key)) {
            coverTouched.add(key);
            event.waitUntil(touchCover(cache, key, cached.clone()));
        }
        return cached;
    }

    const response = await fetch(event.request); // offline and not cached: fails, the app shows its fallback
    if (response.ok && (response.type === 'cors' || response.type === 'basic')) {
        event.waitUntil(storeCover(cache, key, response.clone()).catch(e => console.warn('Cover cache:', e)));
    }
    return response;
}

async function storeCover(cache, key, response) {
    const blob = await response.blob();
    if (blob.size === 0 || blob.size > coverMaxEntryBytes) {
        return;
    }

    await countCoverBytes(cache);
    const headers = new Headers({
        'Content-Type': response.headers.get('Content-Type') || 'image/jpeg',
        'Access-Control-Allow-Origin': '*',
        'X-Bm-Size': String(blob.size),
    });
    await cache.put(key, new Response(blob, { status: 200, headers }));
    coverTouched.add(key);
    coverBytes += blob.size;
    await trimCovers(cache);
}

// Re-inserting moves the entry to the end of cache.keys(), which is insertion-ordered: the front is least recent.
async function touchCover(cache, key, response) {
    await cache.delete(key);
    await cache.put(key, response);
}

async function countCoverBytes(cache) {
    if (coverBytes !== null) {
        return;
    }

    let total = 0;
    for (const request of await cache.keys()) {
        const response = await cache.match(request);
        total += Number(response?.headers.get('X-Bm-Size')) || 0;
    }
    coverBytes = total;
}

async function trimCovers(cache) {
    if (coverBytes <= coverBudgetBytes) {
        return;
    }

    // Trim to 90% so we don't trim again on every new cover.
    for (const request of await cache.keys()) {
        if (coverBytes <= coverBudgetBytes * 0.9) {
            break;
        }
        const response = await cache.match(request);
        coverBytes -= Number(response?.headers.get('X-Bm-Size')) || 0;
        await cache.delete(request);
        coverTouched.delete(request.url);
    }
}
