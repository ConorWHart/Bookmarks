// Pasted cover images for Bookmarks, called from BrowserCoverPhotoReader.cs.
//
// On a phone, "Share image" in Chrome gives a link to Google's results page, not to the image, so a link can't
// be used. "Copy image" puts the picture itself on the clipboard, and that's what we read here.
//
// readClipboard(maxLong, maxChars): the image on the clipboard (needs a tap: call it from a button), shrunk so its
//   long edge is at most maxLong px, as a WebP data URL (JPEG where WebP can't be encoded) no longer than maxChars:
//   { dataUrl, width, height }. Its colour is read afterwards like any other cover's (covers.js). Or { error }: unsupported (no clipboard reading
//   here: paste into the link field instead), denied, noimage, unreadable, toobig.
// listen(element, dotnet, maxLong, maxChars): images pasted anywhere inside element (Ctrl+V, or the phone's own
//   Paste in the link field) go to dotnet.invokeMethodAsync('OnPasted', result) in the same shape. Text pastes are
//   left alone. Returns a handle with dispose().

import { shrink, encode } from './spinePhoto.js';

export async function readClipboard(maxLong, maxChars) {
    if (!navigator.clipboard || typeof navigator.clipboard.read !== 'function') {
        return { error: 'unsupported' };
    }

    let items;
    try {
        items = await navigator.clipboard.read();
    } catch (e) {
        // NotAllowedError: permission refused (or the "Paste" bubble dismissed on iOS). Anything else: no access.
        return { error: e && e.name === 'NotAllowedError' ? 'denied' : 'unsupported' };
    }

    for (const item of items) {
        const type = item.types.find(t => t.startsWith('image/'));
        if (type) {
            try {
                return await fromBlob(await item.getType(type), maxLong, maxChars);
            } catch {
                return { error: 'unreadable' };
            }
        }
    }

    return { error: 'noimage' };
}

export function listen(element, dotnet, maxLong, maxChars) {
    const onPaste = async e => {
        const files = e.clipboardData ? Array.from(e.clipboardData.files || []) : [];
        const file = files.find(f => f.type && f.type.startsWith('image/'));
        if (!file) {
            return; // ordinary text (a link): the field takes it as usual
        }

        e.preventDefault();
        let result;
        try {
            result = await fromBlob(file, maxLong, maxChars);
        } catch {
            result = { error: 'unreadable' };
        }

        try {
            await dotnet.invokeMethodAsync('OnPasted', result);
        } catch {
            // The editor closed meanwhile.
        }
    };

    element.addEventListener('paste', onPaste);
    return { dispose: () => element.removeEventListener('paste', onPaste) };
}

async function fromBlob(blob, maxLong, maxChars) {
    const canvas = await toCanvas(blob);
    if (!canvas || canvas.width < 8 || canvas.height < 8) {
        return { error: 'unreadable' };
    }

    let scale = Math.min(1, maxLong / Math.max(canvas.width, canvas.height));
    for (let attempt = 0; attempt < 6; attempt++) {
        const out = shrink(canvas, scale);
        const url = encode(out, 0.8, maxChars) || encode(out, 0.65, maxChars) || encode(out, 0.5, maxChars);
        if (url) {
            return { dataUrl: url, width: out.width, height: out.height };
        }

        scale *= 0.8; // still too big: a smaller picture
    }

    return { error: 'toobig' };
}

async function toCanvas(blob) {
    const url = URL.createObjectURL(blob);
    try {
        const img = new Image();
        img.decoding = 'async';
        img.src = url;
        await img.decode(); // fails for formats this browser can't show
        const canvas = document.createElement('canvas');
        canvas.width = img.naturalWidth;
        canvas.height = img.naturalHeight;
        const ctx = canvas.getContext('2d');
        // Transparent images (PNGs) would turn black in a JPEG and look odd on a shelf: put them on white.
        ctx.fillStyle = '#fff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.drawImage(img, 0, 0);
        return canvas;
    } finally {
        URL.revokeObjectURL(url);
    }
}
