// Spine photos for Bookmarks (Phase 7e), called from BrowserSpinePhotoEditor.cs.
//
// load(input, img, aspect): reads the photo chosen in a file input into <img> and starts Cropper.js on it
//   (lib/cropper, MIT, loaded the first time). aspect = spine width / height, or 0 for a free crop.
//   Returns null, or an error code: nofile, notimage, unreadable, nocropper.
// loadUrl(url, img, aspect): the same for an image on the web (the book's cover). The site must allow reading it
//   (Open Library does); otherwise 'blocked'. Also 'unreadable', 'nocropper'.
// rotate(degrees), setAspect(aspect): adjust the crop.
// exportPhoto(maxLong, maxChars): the crop, downscaled so its long edge is at most maxLong px, as a WebP data URL
//   (JPEG where WebP can't be encoded) no longer than maxChars, plus its dominant colour.
// close(): stops cropping and frees the photo.

import { pick } from './covers.js';

let cropper = null;
let objectUrl = null;
let loading = null;

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = src;
        s.async = true;
        s.onload = () => resolve();
        s.onerror = () => reject(new Error('Could not load ' + src));
        document.head.appendChild(s);
    });
}

function ensureCropper() {
    if (window.Cropper) {
        return Promise.resolve();
    }

    if (!loading) {
        if (!document.querySelector('link[data-bm-cropper]')) {
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = 'lib/cropper/cropper.min.css';
            link.dataset.bmCropper = '1';
            document.head.appendChild(link);
        }

        loading = loadScript('lib/cropper/cropper.min.js').catch(e => {
            loading = null; // let the next attempt retry
            throw e;
        });
    }

    return loading;
}

function destroy() {
    if (cropper) {
        cropper.destroy();
        cropper = null;
    }

    if (objectUrl) {
        URL.revokeObjectURL(objectUrl);
        objectUrl = null;
    }
}

export async function load(input, img, aspect) {
    const file = input && input.files && input.files[0];
    if (!file) {
        return 'nofile';
    }

    if (file.type && !file.type.startsWith('image/')) {
        input.value = '';
        return 'notimage';
    }

    try {
        await ensureCropper();
    } catch {
        input.value = '';
        return 'nocropper';
    }

    destroy();
    objectUrl = URL.createObjectURL(file);
    input.value = ''; // so choosing the same file again still fires "change"
    img.removeAttribute('crossorigin'); // set when the cover was loaded before; not needed for a local file
    img.src = objectUrl;
    try {
        await img.decode(); // fails for formats this browser can't show (e.g. HEIC outside Safari)
    } catch {
        destroy();
        img.removeAttribute('src');
        return 'unreadable';
    }

    start(img, aspect, true);
    return null;
}

export async function loadUrl(url, img, aspect) {
    try {
        await ensureCropper();
    } catch {
        return 'nocropper';
    }

    destroy();
    img.crossOrigin = 'anonymous'; // so the crop can be read back from the canvas
    img.src = url;
    try {
        await img.decode();
    } catch {
        img.removeAttribute('src');
        return 'unreadable';
    }

    try {
        // A site that doesn't allow reading its images leaves the canvas "tainted": find out now, not at Use photo.
        const probe = document.createElement('canvas');
        probe.width = probe.height = 1;
        const ctx = probe.getContext('2d');
        ctx.drawImage(img, 0, 0, 1, 1);
        ctx.getImageData(0, 0, 1, 1);
    } catch {
        img.removeAttribute('src');
        return 'blocked';
    }

    start(img, aspect, false);
    return null;
}

function start(img, aspect, fromCamera) {
    cropper = new window.Cropper(img, {
        viewMode: 1,             // the crop box stays inside the photo
        dragMode: 'move',        // dragging the photo pans it; the box is moved by its own handles
        aspectRatio: aspect > 0 ? aspect : NaN,
        autoCropArea: 0.85,
        background: false,
        responsive: true,
        restore: false,
        checkOrientation: fromCamera, // phone photos: honour the camera's rotation tag
        checkCrossOrigin: false,      // crossOrigin is set above; don't add a cache-busting query to cover links
        toggleDragModeOnDblclick: false,
    });
}

export function rotate(degrees) {
    if (cropper) {
        cropper.rotate(degrees);
    }
}

export function setAspect(aspect) {
    if (cropper) {
        cropper.setAspectRatio(aspect > 0 ? aspect : NaN);
    }
}

export function exportPhoto(maxLong, maxChars) {
    if (!cropper) {
        return null;
    }

    const crop = cropper.getCroppedCanvas({
        maxWidth: 4096,
        maxHeight: 4096,
        imageSmoothingEnabled: true,
        imageSmoothingQuality: 'high',
        fillColor: '#000',
    });
    if (!crop || crop.width < 2 || crop.height < 2) {
        return null;
    }

    let scale = Math.min(1, maxLong / Math.max(crop.width, crop.height));
    let url = null;
    let out = null;
    for (let attempt = 0; attempt < 6; attempt++) {
        out = shrink(crop, scale);
        url = encode(out, 0.8, maxChars) || encode(out, 0.65, maxChars) || encode(out, 0.5, maxChars);
        if (url) {
            break;
        }

        scale *= 0.8; // still too big: a smaller picture
    }

    if (!url) {
        return null;
    }

    return { dataUrl: url, color: colorOf(out), width: out.width, height: out.height };
}

export function close() {
    destroy();
}

function shrink(source, scale) {
    const w = Math.max(1, Math.round(source.width * scale));
    const h = Math.max(1, Math.round(source.height * scale));
    if (w === source.width && h === source.height) {
        return source;
    }

    // Halve in steps first: one big jump down looks grainy even with high-quality smoothing.
    let current = source;
    while (current.width / 2 >= w && current.height / 2 >= h) {
        current = draw(current, Math.round(current.width / 2), Math.round(current.height / 2));
    }

    return draw(current, w, h);
}

function draw(source, w, h) {
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(source, 0, 0, w, h);
    return canvas;
}

function encode(canvas, quality, maxChars) {
    let url = canvas.toDataURL('image/webp', quality);
    if (!url.startsWith('data:image/webp')) {
        url = canvas.toDataURL('image/jpeg', quality); // Safari before 14 can't encode WebP
    }

    return url.length <= maxChars ? url : null;
}

function colorOf(canvas) {
    try {
        const small = draw(canvas, 16, 48);
        return pick(small.getContext('2d').getImageData(0, 0, small.width, small.height).data);
    } catch {
        return null;
    }
}
