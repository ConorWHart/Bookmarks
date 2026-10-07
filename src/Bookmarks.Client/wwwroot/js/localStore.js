// IndexedDB storage for Bookmarks, called from IndexedDbLocalStore.cs.
//
// Records arrive and leave as JSON strings (serialized in .NET with BookmarksJsonContext) and are
// stored as plain objects, so they're readable in DevTools > Application > IndexedDB and indexable.
//
// One database per account: 'bookmarks' while signed out, 'bookmarks-{userId}' once signed in, so a
// second person signing in on the same device never sees the first person's library. open() is called
// with the name once, at startup, before anything reads or writes.
//
// Dexie is vendored (lib/dexie, Apache-2.0) so the service worker precaches it and the app works offline.
import Dexie from '../lib/dexie/dexie.min.js';

export const guestDatabase = 'bookmarks';

/** Tables holding library records. 'meta' is device-local and never copied between accounts. */
const dataTables = ['books', 'libraryItems', 'readingSessions', 'notes', 'tags', 'bookcases', 'shelves', 'presets'];

let db = null;

// Schema history. Never edit a released version: add create(n + 1) stores with only the changes
// (and an .upgrade() callback when existing rows need migrating).
// Only indexed fields are listed; every other property is stored as-is.
function define(name) {
    const database = new Dexie(name);
    database.version(1).stores({
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
    return database;
}

function active() {
    if (!db) {
        throw new Error('The local database has not been opened yet.');
    }

    return db;
}

/** Opens the account's database and asks the browser not to evict it. Returns the persistence state. */
export async function open(name) {
    db = define(name || guestDatabase);
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
    let rows = await active().table(table).toArray();
    if (!includeDeleted) {
        rows = rows.filter(r => !r.isDeleted);
    }

    return JSON.stringify(rows);
}

export async function get(table, id) {
    const row = await active().table(table).get(id);
    return row === undefined ? null : JSON.stringify(row);
}

export async function put(table, json) {
    await active().table(table).put(JSON.parse(json));
}

export async function getMeta(key) {
    const row = await active().table('meta').get(key);
    return row === undefined ? null : row.value;
}

export async function setMeta(key, value) {
    if (value === null || value === undefined) {
        await active().table('meta').delete(key);
    } else {
        await active().table('meta').put({ key, value });
    }
}

/** How many library records the signed-out database holds (0 when it doesn't exist). */
export async function countGuestRecords() {
    if (db?.name === guestDatabase) {
        return await countIn(db);
    }

    if (!(await Dexie.exists(guestDatabase))) {
        return 0;
    }

    const guest = define(guestDatabase);
    try {
        await guest.open();
        return await countIn(guest);
    } finally {
        guest.close();
    }
}

async function countIn(database) {
    let total = 0;
    for (const table of dataTables) {
        total += await database.table(table).count();
    }

    return total;
}

/**
 * Copies every record from the signed-out database into the open account database, re-keyed to
 * userId, then deletes the signed-out database. Server versions are reset because they belong to
 * whichever account pushed them (Phase 4). Preferences in 'meta' stay with the device, not the account.
 */
export async function moveGuestLibrary(userId) {
    const target = active();
    if (target.name === guestDatabase) {
        throw new Error('Sign in before moving the signed-out library.');
    }

    if (!(await Dexie.exists(guestDatabase))) {
        return 0;
    }

    const guest = define(guestDatabase);
    await guest.open();
    let moved = 0;

    try {
        for (const table of dataTables) {
            const rows = await guest.table(table).toArray();
            if (rows.length === 0) {
                continue;
            }

            for (const row of rows) {
                if (table === 'books') {
                    if (row.createdByUserId) {
                        row.createdByUserId = userId;
                    }
                } else {
                    row.userId = userId;
                    row.version = 0;
                }
            }

            await target.table(table).bulkPut(rows);
            moved += rows.length;
        }
    } finally {
        guest.close();
    }

    await Dexie.delete(guestDatabase);
    return moved;
}
