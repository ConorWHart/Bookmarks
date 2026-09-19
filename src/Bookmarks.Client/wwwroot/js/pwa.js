// App status for Bookmarks, called from BrowserAppStatusService.cs:
//  - online/offline changes
//  - "a new version is ready" when an updated service worker has installed and is waiting
//
// The service worker itself is registered early in index.html (window.bmSwRegistration),
// so offline support never depends on Blazor having started.

const UpdateCheckIntervalMs = 60 * 60 * 1000;

let dotnet = null;
let waitingWorker = null;
let timer = null;

export function start(dotnetRef) {
    dotnet = dotnetRef;
    window.addEventListener('online', reportOnline);
    window.addEventListener('offline', reportOnline);
    reportOnline();
    watchForUpdates();
}

export function stop() {
    window.removeEventListener('online', reportOnline);
    window.removeEventListener('offline', reportOnline);
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

    // A version that finished installing before this page loaded.
    // (No controller = first install, which isn't an update.)
    if (reg.waiting && navigator.serviceWorker.controller) {
        announce(reg.waiting);
    }

    reg.addEventListener('updatefound', () => {
        const worker = reg.installing;
        worker?.addEventListener('statechange', () => {
            if (worker.state === 'installed' && navigator.serviceWorker.controller) {
                announce(worker);
            }
        });
    });

    // Installed apps can stay open for days; look for new versions now and then.
    timer = setInterval(checkForUpdate, UpdateCheckIntervalMs);
}

async function checkForUpdate() {
    try {
        (await registration())?.update();
    } catch {
        // Offline or server unreachable; try again later.
    }
}

function announce(worker) {
    waitingWorker = worker;
    dotnet?.invokeMethodAsync('OnUpdateAvailable');
}
