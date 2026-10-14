// IndexedDB storage for Bookmarks, called from IndexedDbLocalStore.cs.
//
// Records arrive and leave as JSON strings (serialized in .NET with BookmarksJsonContext) and are
// stored as plain objects, so they're readable in DevTools > Application > IndexedDB and indexable.
//
// One database per account: 'bookmarks' while signed out, 'bookmarks-{userId}' once signed in, so a
// second person signing in on the same device never sees the first person's library. open() is called
// with the name once, at startup, before anything reads or writes.
//
// Sync (Phase 4): every local edit in an account database also writes an 'outbox' entry in the same
// transaction ({ key: 'type:id', type, id, stamp }); the stamp changes on every edit so a push can tell
// whether the record was edited while it was in flight. Rows written by the sync engine don't enter the outbox.
//
// Dexie is vendored (lib/dexie, Apache-2.0) so the service worker precaches it and the app works offline.
import Dexie from '../lib/dexie/dexie.min.js';

export const guestDatabase = 'bookmarks';

/**
 * Tables holding library records, with their sync entity type names. The names must match
 * SyncEntityTypes in Bookmarks.Shared. 'meta' and 'outbox' are device-local and never copied between accounts.
 */
const entityTypes = {
    books: 'book',
    libraryItems: 'libraryItem',
    readingSessions: 'readingSession',
    notes: 'note',
    tags: 'tag',
    bookcases: 'bookcase',
    shelves: 'shelf',
    presets: 'preset',
    shelfPlacements: 'placement',
};

const dataTables = Object.keys(entityTypes);

let db = null;

// Schema history. Never edit a released version: add version(n + 1) stores with only the changes
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
        // Key/value: sync checkpoint and device id ("sync:*") and device preferences ("pref:*").
        meta: 'key',
    });

    // Phase 4: books became ordinary synced records, and local edits are queued in the outbox.
    database.version(2).stores({
        books: 'id, isbn13, version',
        outbox: 'key',
    }).upgrade(tx => tx.table('books').toCollection().modify(book => {
        if (!book.userId && book.createdByUserId) {
            book.userId = book.createdByUserId;
        }

        delete book.createdByUserId;
        if (book.version === undefined) {
            book.version = 0;
        }

        if (book.isDeleted === undefined) {
            book.isDeleted = false;
        }

        if (!book.updatedAt) {
            book.updatedAt = new Date().toISOString();
        }
    }));

    // Version 3 only ever existed on a development machine (a collections experiment). It stays declared
    // so that database upgrades cleanly; version 4 removes its tables.
    database.version(3).stores({
        collections: 'id, version',
        collectionItems: 'id, collectionId, libraryItemId, version',
    });

    // Phase 6b: a book can be on a shelf in several bookcases (one placement each). Bookcase.isHidden needs no index.
    database.version(4).stores({
        collections: null,
        collectionItems: null,
        shelfPlacements: 'id, shelfId, libraryItemId, version',
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

/**
 * Saves a local edit. With an entity type (account databases), the outbox entry is written in the same
 * transaction; without one (the guest database, which never syncs) only the record is written.
 */
export async function put(table, json, entityType) {
    const row = JSON.parse(json);
    const d = active();
    if (!entityType) {
        await d.table(table).put(row);
        return;
    }

    await d.transaction('rw', d.table(table), d.table('outbox'), async () => {
        await d.table(table).put(row);
        await d.table('outbox').put(outboxEntry(entityType, row.id));
    });
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

// ---------- Sync ----------

function outboxKey(type, id) {
    return `${type}:${id}`;
}

function outboxEntry(type, id) {
    return { key: outboxKey(type, id), type, id, stamp: newStamp() };
}

function newStamp() {
    // randomUUID needs a secure context (https or localhost), which the app always has; the fallback is for safety.
    return crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`;
}

/** Milliseconds for an ISO timestamp; 0 when missing or unreadable (counts as oldest). */
function time(value) {
    const ms = Date.parse(value);
    return Number.isNaN(ms) ? 0 : ms;
}

/** Every pending local change as [{ entityType, id, stamp }]. */
export async function getOutbox() {
    const entries = await active().table('outbox').toArray();
    return JSON.stringify(entries.map(e => ({ entityType: e.type, id: e.id, stamp: e.stamp })));
}

export async function countOutbox() {
    return await active().table('outbox').count();
}

/** Queues every record that has never been synced (version 0). Returns how many were queued. */
export async function enqueueUnsynced() {
    const d = active();
    let queued = 0;
    await d.transaction('rw', [...dataTables, 'outbox'], async () => {
        for (const table of dataTables) {
            const rows = (await d.table(table).toArray()).filter(r => !(r.version > 0));
            if (rows.length > 0) {
                await d.table('outbox').bulkPut(rows.map(r => outboxEntry(entityTypes[table], r.id)));
                queued += rows.length;
            }
        }
    });

    return queued;
}

/**
 * The server's answer to a push, in one transaction: drop the outbox entry if the record wasn't edited
 * meanwhile (same stamp), and when accepted (version not null) raise the record's version to the server's.
 */
export async function completePush(table, entityType, id, stamp, version) {
    const d = active();
    await d.transaction('rw', d.table(table), d.table('outbox'), async () => {
        const key = outboxKey(entityType, id);
        const entry = await d.table('outbox').get(key);
        if (entry && entry.stamp === stamp) {
            await d.table('outbox').delete(key);
        }

        if (version !== null && version !== undefined) {
            const row = await d.table(table).get(id);
            if (row && !(row.version >= version)) {
                row.version = version;
                await d.table(table).put(row);
            }
        }
    });
}

/**
 * Stores a record from the server (see ILocalStore.ApplyPulledAsync for the rules). Returns true when
 * local data changed.
 */
export async function applyPulled(table, entityType, json) {
    const incoming = JSON.parse(json);
    const d = active();
    return await d.transaction('rw', d.table(table), d.table('outbox'), async () => {
        const local = await d.table(table).get(incoming.id);
        if (!local) {
            await d.table(table).put(incoming);
            return true;
        }

        const key = outboxKey(entityType, incoming.id);
        if (await d.table('outbox').get(key)) {
            if (time(incoming.updatedAt) > time(local.updatedAt)) {
                await d.table(table).put(incoming);
                await d.table('outbox').delete(key);
                return true;
            }

            // Our pending edit is newer: keep it queued, based on the version we've now seen.
            if (!(local.version >= incoming.version)) {
                local.version = incoming.version;
                await d.table(table).put(local);
            }

            return false;
        }

        if (local.version >= incoming.version) {
            return false;
        }

        await d.table(table).put(incoming);
        return true;
    });
}

/** Closes and deletes the open account database (sign-out "Remove from this device"). */
export async function eraseDatabase() {
    const d = active();
    if (d.name === guestDatabase) {
        throw new Error('Only an account database can be removed.');
    }

    d.close();
    db = null;
    await Dexie.delete(d.name);
}

// ---------- Guest library ----------

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
 * userId and queued for sync, then deletes the signed-out database. Server versions are reset because
 * the records have never been pushed for this account. Preferences in 'meta' stay with the device.
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
                row.userId = userId;
                row.version = 0;
                // Pre-Phase 4 books recorded their owner here; books are now ordinary synced records.
                delete row.createdByUserId;
            }

            await target.transaction('rw', target.table(table), target.table('outbox'), async () => {
                await target.table(table).bulkPut(rows);
                await target.table('outbox').bulkPut(rows.map(r => outboxEntry(entityTypes[table], r.id)));
            });
            moved += rows.length;
        }
    } finally {
        guest.close();
    }

    await Dexie.delete(guestDatabase);
    return moved;
}
