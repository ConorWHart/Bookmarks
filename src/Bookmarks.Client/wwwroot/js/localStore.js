// IndexedDB storage for Bookmarks, called from IndexedDbLocalStore.cs.
//
// Records arrive and leave as JSON strings (serialized in .NET with BookmarksJsonContext) and are
// stored as plain objects, so they're readable in DevTools > Application > IndexedDB and indexable.
//
// Dexie is vendored (lib/dexie, Apache-2.0) so the service worker precaches it and the app works offline.
import Dexie from '../lib/dexie/dexie.min.js';

const db = new Dexie('bookmarks');

// Schema history. Never edit a released version: add db.version(n + 1) with only the changes
// (and an .upgrade() callback when existing rows need migrating).
// Only indexed fields are listed; every other property is stored as-is.
db.version(1).stores({
    books: 'id, isbn13',
    libraryItems: 'id, bookId, shelfId, version',
    readingSessions: 'id, libraryItemId, version',
    notes: 'id, libraryItemId, version',
    tags: 'id, version',
    bookcases: 'id, version',
    shelves: 'id, bookcaseId, version',
    presets: 'id, version',
    // Key/value: sync checkpoints (Phase 4) and device preferences ("pref:*").
    meta: 'key',
});

/** Opens the database and asks the browser not to evict it. Returns the persistence state. */
export async function open() {
    await db.open();
    return await requestPersistence();
}

async function requestPersistence() {
    const storage = navigator.storage;
    if (!storage || !storage.persist) {
        return 'unsupported';
    }

    try {
        if (await storage.persisted()) {
            return 'persisted';
        }

        // Chrome decides silently (installed PWAs and engaged sites are granted);
        // Safari grants it to home-screen apps, which protects against the 7-day eviction.
        return (await storage.persist()) ? 'granted' : 'denied';
    } catch {
        return 'error';
    }
}

export async function getAll(table, includeDeleted) {
    let rows = await db.table(table).toArray();
    if (!includeDeleted) {
        rows = rows.filter(r => !r.isDeleted);
    }

    return JSON.stringify(rows);
}

export async function get(table, id) {
    const row = await db.table(table).get(id);
    return row === undefined ? null : JSON.stringify(row);
}

export async function put(table, json) {
    await db.table(table).put(JSON.parse(json));
}

export async function getMeta(key) {
    const row = await db.table('meta').get(key);
    return row === undefined ? null : row.value;
}

export async function setMeta(key, value) {
    if (value === null || value === undefined) {
        await db.table('meta').delete(key);
    } else {
        await db.table('meta').put({ key, value });
    }
}
