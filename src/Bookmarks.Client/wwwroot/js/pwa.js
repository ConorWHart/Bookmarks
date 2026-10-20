// App status for Bookmarks, called from BrowserAppStatusService.cs:
//  - online/offline changes
//  - "a new version is ready" when an updated service worker has installed and is waiting
//
// When to look for a new version: at start, when back online, when the app comes back to the screen (an installed
// app on a phone is usually resumed, not reloaded, and its timers don't run in the background), hourly while open,
// and on demand (Settings > Check for updates).
//
// The service worker itself is registered early in index.html (window.bmSwRegistration),
// so offline support never depends on Blazor having started.

const UpdateCheckIntervalMs = 60 * 60 * 1000;
// Coming back to the screen checks at most this often.
const VisibleCheckMinMs = 60 * 1000;

let dotnet = null;
let waitingWorker = null;
let timer = null;
let lastCheck = 0;
const tracked = new WeakSet();

export function start(dotnetRef) {
    dotnet = dotnetRef;
    window.addEventListener('online', reportOnline);
    window.addEventListener('offline', reportOnline);
    document.addEventListener('visibilitychange', onVisibilityChange);
    reportOnline();
    watchForUpdates();
}

export function stop() {
    window.removeEventListener('online', reportOnline);
    window.removeEventListener('offline', reportOnline);
    document.removeEventListener('visibilitychange', onVisibilityChange);
    if (timer) {
        clearInterval(timer);
    }

    dotnet = null;
}

/** Activates the waiting version, then reloads once it has taken control. */
export function applyUpdate() {
    if (!waitingWorker || !navigator.serviceWorker) {
        location.reload();
        return;
    }

    navigator.serviceWorker.addEventListener('controllerchange', () => location.reload(), { once: true });
    waitingWorker.postMessage({ type: 'SKIP_WAITING' });
}

function reportOnline() {
    dotnet?.invokeMethodAsync('OnOnlineChanged', navigator.onLine);
    if (navigator.onLine) {
        checkForUpdate();
    }
}

async function registration() {
    return window.bmSwRegistration ? await window.bmSwRegistration : null;
}

async function watchForUpdates() {
    const reg = await registration();
    if (!reg) {
        return;
    }

    // A version that finished installing before this page loaded, or is still downloading: the browser checks on
    // every page load and an update can start before Blazor (and this code) is running.
    // (No controller = first install, which isn't an update.)
    if (reg.waiting) {
        track(reg.waiting);
    }

    if (reg.installing) {
        track(reg.installing);
    }

    reg.addEventListener('updatefound', () => track(reg.installing));

    // Installed apps can stay open for days; look for new versions now and then.
    timer = setInterval(checkForUpdate, UpdateCheckIntervalMs);
}

/** Announces the worker once it has installed (now, or when its download finishes). */
function track(worker) {
    if (!worker || tracked.has(worker)) {
        return;
    }

    tracked.add(worker);
    const check = () => {
        if (worker.state === 'installed' && navigator.serviceWorker.controller) {
            announce(worker);
        }
    };

    worker.addEventListener('statechange', check);
    check();
}

function onVisibilityChange() {
    if (document.visibilityState === 'visible' && Date.now() - lastCheck > VisibleCheckMinMs) {
        checkForUpdate();
    }
}

async function checkForUpdate() {
    lastCheck = Date.now();
    try {
        await (await registration())?.update();
    } catch {
        // Offline or server unreachable; try again later.
    }
}

/**
 * Settings > Check for updates. Returns 'ready' (a new version is waiting: the app shows Reload), 'downloading'
 * (found; Reload is offered when it finishes), 'current', 'unsupported' or 'failed'.
 */
export async function checkNow() {
    const reg = await registration();
    if (!reg) {
        return 'unsupported';
    }

    lastCheck = Date.now();
    try {
        await reg.update();
    } catch {
        return 'failed';
    }

    if (reg.waiting && navigator.serviceWorker.controller) {
        track(reg.waiting);
        return 'ready';
    }

    if (reg.installing) {
        track(reg.installing);
        return 'downloading';
    }

    return 'current';
}

function announce(worker) {
    waitingWorker = worker;
    dotnet?.invokeMethodAsync('OnUpdateAvailable');
}
