// Sharing the app's link for BrowserAppSharer.cs (Settings and the side menu → Share Bookmarks).
// Browsers only open the share sheet or write the clipboard right after a tap, so the module is loaded ahead of time
// (canShare) and these run straight from the click.

/** Whether the device has a share sheet (phones, Windows and ChromeOS Chrome/Edge, Safari). */
export function canShare() {
    return typeof navigator.share === 'function';
}

/** Opens the share sheet; without one (or if it refuses), copies the link. Returns shared | cancelled | copied | failed. */
export async function share(title, text, url) {
    if (canShare()) {
        try {
            await navigator.share({ title, text, url });
            return 'shared';
        } catch (e) {
            if (e && e.name === 'AbortError') {
                return 'cancelled';
            }
            // NotAllowedError and the like: fall back to copying.
        }
    }
    return (await copy(url)) ? 'copied' : 'failed';
}

/** Copies the text. Returns whether it worked. */
export async function copy(text) {
    try {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch {
        // Try the old way below.
    }

    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    let ok = false;
    try {
        ok = document.execCommand('copy');
    } catch {
        ok = false;
    }
    area.remove();
    return ok;
}
