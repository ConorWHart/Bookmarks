// Barcode scanning for Bookmarks (Phase 5), called from BrowserBarcodeScanner.cs.
//
// Reads book barcodes (EAN-13 starting 978/979, i.e. ISBN-13) from the camera.
//  - The browser's own BarcodeDetector where it supports EAN-13 (Chrome on Android, ChromeOS, macOS).
//  - Otherwise ZXing (lib/zxing/zxing.min.js, Apache-2.0), loaded only when the scanner first opens
//    (iOS Safari, Windows, Firefox).
// Only valid ISBNs are reported; other EAN-13s (price or store codes) produce a hint instead.

const ScanIntervalMs = 150;
const NotIsbnHintIntervalMs = 3000;

let dotnet = null;
let stream = null;
let video = null;
let timer = null;
let busy = false;
let detector = null;
let zxing = null;
let canvas = null;
let lastNotIsbn = 0;

export function isSupported() {
    return !!(window.isSecureContext && navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
}

/** Starts the camera into videoElement and scans. Returns null when running, or an error code. */
export async function start(videoElement, dotnetRef) {
    await stop();
    dotnet = dotnetRef;
    video = videoElement;

    if (!isSupported()) {
        return 'unsupported';
    }

    try {
        stream = await navigator.mediaDevices.getUserMedia({
            audio: false,
            video: {
                facingMode: { ideal: 'environment' },
                // As sharp as the camera allows: barcodes need detail, and low-end webcams default to 640×480.
                width: { ideal: 1920 },
                height: { ideal: 1080 },
            },
        });
    } catch (e) {
        return cameraError(e);
    }

    await preferContinuousFocus();
    video.srcObject = stream;
    video.setAttribute('playsinline', '');
    video.muted = true;
    try {
        await video.play();
    } catch {
        // Autoplay rules: muted + playsinline normally allows it; frames still arrive.
    }

    try {
        await prepareDecoder();
    } catch (e) {
        console.warn('Scanner: no decoder available', e);
        await stop();
        return 'nodecoder';
    }

    timer = setInterval(scanFrame, ScanIntervalMs);
    return null;
}

export async function stop() {
    if (timer) {
        clearInterval(timer);
        timer = null;
    }
    if (stream) {
        stream.getTracks().forEach(t => t.stop());
        stream = null;
    }
    if (video) {
        video.srcObject = null;
        video = null;
    }
    busy = false;
}

async function preferContinuousFocus() {
    const track = stream?.getVideoTracks()[0];
    const caps = track?.getCapabilities ? track.getCapabilities() : {};
    if (caps.focusMode?.includes('continuous')) {
        try {
            await track.applyConstraints({ advanced: [{ focusMode: 'continuous' }] });
        } catch {
            // Not every camera that lists it accepts it.
        }
    }
}

export function torchAvailable() {
    const track = stream?.getVideoTracks()[0];
    const caps = track?.getCapabilities ? track.getCapabilities() : {};
    return !!caps.torch;
}

export async function setTorch(on) {
    const track = stream?.getVideoTracks()[0];
    if (!track) {
        return false;
    }
    try {
        await track.applyConstraints({ advanced: [{ torch: !!on }] });
        return true;
    } catch {
        return false;
    }
}

// ---------- Decoding ----------

async function prepareDecoder() {
    if ('BarcodeDetector' in window) {
        try {
            const formats = await window.BarcodeDetector.getSupportedFormats();
            if (formats.includes('ean_13')) {
                detector = new window.BarcodeDetector({ formats: ['ean_13'] });
                return;
            }
        } catch {
            // Fall through to ZXing.
        }
    }

    if (!zxing) {
        await loadScript('lib/zxing/zxing.min.js');
        const Z = window.ZXing;
        const hints = new Map();
        hints.set(Z.DecodeHintType.POSSIBLE_FORMATS, [Z.BarcodeFormat.EAN_13]);
        hints.set(Z.DecodeHintType.TRY_HARDER, true);
        const reader = new Z.MultiFormatReader();
        reader.setHints(hints);
        zxing = { Z, reader };
    }
    canvas ??= document.createElement('canvas');
}

async function scanFrame() {
    if (busy || !video || video.readyState < 2 || !video.videoWidth) {
        return;
    }

    busy = true;
    try {
        const codes = detector ? await detectNative() : detectZxing();
        for (const code of codes) {
            if (isIsbn13(code)) {
                await report(code);
                return;
            }
            notIsbn();
        }
    } catch (e) {
        console.warn('Scanner:', e);
    } finally {
        busy = false;
    }
}

async function detectNative() {
    const found = await detector.detect(video);
    return found.map(b => b.rawValue);
}

// ZXing looks at a different view of the frame each time, because what works depends on the camera:
//  - the guide box's band, enlarged: helps low-resolution webcams, where the bars are only a pixel or two wide;
//  - the middle 60 % at up to 1280 px: the usual case;
//  - the whole frame: when the barcode isn't where the guide is.
const ZxingViews = [
    { band: 0.36, side: 0.08, minWidth: 1280, maxWidth: 1920 },
    { band: 0.6, side: 0, minWidth: 0, maxWidth: 1280 },
    { band: 1.0, side: 0, minWidth: 0, maxWidth: 1280 },
];
let zxingView = 0;

function detectZxing() {
    const view = ZxingViews[zxingView];
    zxingView = (zxingView + 1) % ZxingViews.length;

    const vw = video.videoWidth;
    const vh = video.videoHeight;
    const sx = Math.round(vw * view.side);
    const sw = vw - 2 * sx;
    const sh = Math.round(vh * view.band);
    const width = Math.min(view.maxWidth, Math.max(view.minWidth, sw));
    const scale = width / sw;
    canvas.width = Math.round(sw * scale);
    canvas.height = Math.round(sh * scale);
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(video, sx, (vh - sh) / 2, sw, sh, 0, 0, canvas.width, canvas.height);

    const { Z, reader } = zxing;
    try {
        const source = new Z.HTMLCanvasElementLuminanceSource(canvas);
        const bitmap = new Z.BinaryBitmap(new Z.HybridBinarizer(source));
        return [reader.decodeWithState(bitmap).getText()];
    } catch {
        return []; // NotFoundException: no barcode in this frame
    } finally {
        reader.reset();
    }
}

async function report(code) {
    // Stop scanning before handing over, so the same book isn't reported twice.
    if (timer) {
        clearInterval(timer);
        timer = null;
    }
    navigator.vibrate?.(60);
    await dotnet?.invokeMethodAsync('OnDetected', code);
}

function notIsbn() {
    const now = Date.now();
    if (now - lastNotIsbn > NotIsbnHintIntervalMs) {
        lastNotIsbn = now;
        dotnet?.invokeMethodAsync('OnNotIsbn');
    }
}

/** EAN-13 in the Bookland range (978/979) with a correct check digit. */
export function isIsbn13(code) {
    if (!/^97[89]\d{10}$/.test(code)) {
        return false;
    }
    let sum = 0;
    for (let i = 0; i < 12; i++) {
        sum += Number(code[i]) * (i % 2 === 0 ? 1 : 3);
    }
    return (10 - (sum % 10)) % 10 === Number(code[12]);
}

function cameraError(e) {
    switch (e?.name) {
        case 'NotAllowedError':
        case 'SecurityError':
            return 'denied';
        case 'NotFoundError':
        case 'OverconstrainedError':
            return 'nocamera';
        case 'NotReadableError':
        case 'AbortError':
            return 'inuse';
        default:
            console.warn('Scanner: camera error', e);
            return 'unknown';
    }
}

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = src;
        s.async = true;
        s.onload = resolve;
        s.onerror = () => reject(new Error(`Couldn't load ${src}`));
        document.head.appendChild(s);
    });
}
