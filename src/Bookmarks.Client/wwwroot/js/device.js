// Settings → This device (BrowserDeviceStorage.cs): storage estimate, persistent storage, the cover cache.
// Every API is optional; anything the browser can't tell comes back as null.

const coverCacheName = 'bm-covers-v1'; // must match service-worker.published.js

/** [usage, quota, persisted (true/false/null), canRequestPersistence, coverCount, coverBytes] */
export async function storageInfo() {
    const storage = navigator.storage;
    let usage = null;
    let quota = null;
    let persisted = null;

    if (storage?.estimate) {
        try {
            const estimate = await storage.estimate();
            usage = estimate.usage ?? null;
            quota = estimate.quota ?? null;
        } catch {
            // Leave unknown.
        }
    }

    if (storage?.persisted) {
        try {
            persisted = await storage.persisted();
        } catch {
            // Leave unknown.
        }
    }

    const covers = await coverCacheInfo();
    return [usage, quota, persisted, !!storage?.persist, covers?.count ?? null, covers?.bytes ?? null];
}

/** Asks to keep this site's data. Returns true when it's persisted afterwards. */
export async function requestPersistence() {
    if (!navigator.storage?.persist) {
        return false;
    }

    try {
        return await navigator.storage.persist();
    } catch {
        return false;
    }
}

/**
 * Size of the service worker's cover cache, or null when there is none (development builds have no cover cache).
 * Each entry carries its size in X-Bm-Size (set by the worker); entries without it are measured.
 */
async function coverCacheInfo() {
    if (!('caches' in window) || !(await caches.has(coverCacheName))) {
        return null;
    }

    try {
        const cache = await caches.open(coverCacheName);
        const keys = await cache.keys();
        let bytes = 0;
        for (const request of keys) {
            const response = await cache.match(request);
            const size = Number(response?.headers.get('X-Bm-Size'));
            bytes += Number.isFinite(size) && size > 0 ? size : (response ? (await response.blob()).size : 0);
        }

        return { count: keys.length, bytes };
    } catch {
        return null;
    }
}

/**
 * Deletes the cover cache. The service worker keeps a running byte total that is now too high until it restarts;
 * it only makes it trim a little early, never keep too much.
 */
export async function clearCoverCache() {
    if ('caches' in window) {
        try {
            await caches.delete(coverCacheName);
        } catch {
            // Not fatal: covers are re-downloaded or replaced as needed.
        }
    }
}

export function reload() {
    location.reload();
}
