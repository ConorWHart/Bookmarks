// Browser plumbing for BrowserSyncService.cs:
//  - one sync at a time across tabs (Web Locks API), per account database
//  - tells the other tabs to reload after a pull changed the library (BroadcastChannel)
//  - reports when the tab becomes visible again, so a phone PWA catches up when reopened
//
// Every API is optional: without Web Locks the pass just runs; without BroadcastChannel other tabs
// catch up on their own next pass.

let dotnet = null;
let channel = null;
let lockName = null;

export function start(dotnetRef, databaseName) {
    dotnet = dotnetRef;
    lockName = `bookmarks-sync:${databaseName}`;

    if ('BroadcastChannel' in window) {
        channel = new BroadcastChannel(lockName);
        channel.onmessage = event => {
            if (event.data === 'changed') {
                dotnet?.invokeMethodAsync('OnOtherTabSynced');
            }
        };
    }

    document.addEventListener('visibilitychange', onVisibilityChange);
}

export function stop() {
    document.removeEventListener('visibilitychange', onVisibilityChange);
    channel?.close();
    channel = null;
    dotnet = null;
}

/**
 * Runs one sync pass (RunPassAsync in .NET) while holding the account's sync lock.
 * wait = false: skip and return false if another tab is syncing right now.
 * wait = true: queue behind it (Sync now).
 */
export async function runExclusive(dotnetRef, wait) {
    const run = async () => {
        const changed = await dotnetRef.invokeMethodAsync('RunPassAsync');
        if (changed) {
            channel?.postMessage('changed');
        }

        return true;
    };

    if (!navigator.locks) {
        return await run();
    }

    return await navigator.locks.request(lockName, { ifAvailable: !wait }, async lock => lock ? await run() : false);
}

/**
 * Runs a .NET method (no arguments) while holding the sync lock of the database named, so no tab can be in the
 * middle of a sync pass for that account (Settings > Delete account). Works without start().
 */
export async function withSyncLock(databaseName, dotnetRef, method) {
    const run = async () => await dotnetRef.invokeMethodAsync(method);
    if (!navigator.locks) {
        return await run();
    }

    return await navigator.locks.request(`bookmarks-sync:${databaseName}`, run);
}

function onVisibilityChange() {
    if (document.visibilityState === 'visible') {
        dotnet?.invokeMethodAsync('OnBecameVisible');
    }
}
