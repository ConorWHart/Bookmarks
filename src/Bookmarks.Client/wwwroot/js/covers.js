// Cover helpers for Bookmarks (Phase 5), called from BrowserCoverColorService.cs.
//
// dominantColor(url): the color a book's spine should take from its cover, as "#RRGGBB", or null.
// The image must be CORS-readable (Open Library covers are); otherwise the canvas is tainted and
// we return null rather than throw.

const W = 32;
const H = 48;
const EDGE = 1; // ignore the outermost pixels: scans often have a white or black border

export async function dominantColor(url) {
    try {
        const img = new Image();
        img.crossOrigin = 'anonymous';
        img.decoding = 'async';
        img.src = url;
        await img.decode();
        if (img.naturalWidth < 8 || img.naturalHeight < 8) {
            return null; // Open Library's "no cover" placeholder is 1×1
        }

        const canvas = document.createElement('canvas');
        canvas.width = W;
        canvas.height = H;
        const ctx = canvas.getContext('2d', { willReadFrequently: true });
        ctx.drawImage(img, 0, 0, W, H);
        return pick(ctx.getImageData(0, 0, W, H).data);
    } catch {
        return null;
    }
}

// palette(url, max): up to max distinct colours from the cover, most prominent first (Phase 7e, spine designs),
// as "#RRGGBB" strings; [] when the image can't be read.
export async function palette(url, max) {
    try {
        const img = new Image();
        img.crossOrigin = 'anonymous';
        img.decoding = 'async';
        img.src = url;
        await img.decode();
        if (img.naturalWidth < 8 || img.naturalHeight < 8) {
            return [];
        }

        const canvas = document.createElement('canvas');
        canvas.width = W;
        canvas.height = H;
        const ctx = canvas.getContext('2d', { willReadFrequently: true });
        ctx.drawImage(img, 0, 0, W, H);
        return pickMany(ctx.getImageData(0, 0, W, H).data, max || 4);
    } catch {
        return [];
    }
}

// The dominant colour (pick) first, then the strongest other peaks, each far enough from those already chosen to
// read as a different colour. Vivid colours get a boost so an accent in the artwork beats more of the background.
export function pickMany(data, max) {
    const first = pick(data);
    if (!first) {
        return [];
    }

    const buckets = new Map();
    for (let y = EDGE; y < H - EDGE; y++) {
        for (let x = EDGE; x < W - EDGE; x++) {
            const i = (y * W + x) * 4;
            if (data[i + 3] < 128) {
                continue;
            }

            const r = data[i], g = data[i + 1], b = data[i + 2];
            const hi = Math.max(r, g, b);
            const lo = Math.min(r, g, b);
            const saturation = hi === 0 ? 0 : (hi - lo) / hi;
            add(buckets, ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4), r, g, b, 1 + saturation * 2);
        }
    }

    const ranked = [...buckets.keys()]
        .map(key => {
            const e = buckets.get(key);
            return { score: neighbourhood(buckets, key), rgb: [e.r / e.count, e.g / e.count, e.b / e.count] };
        })
        .sort((a, b) => b.score - a.score);

    const toRgb = h => [1, 3, 5].map(i => parseInt(h.substr(i, 2), 16));
    const chosen = [toRgb(first)];
    for (const c of ranked) {
        if (chosen.length >= max) {
            break;
        }

        if (chosen.every(o => Math.hypot(o[0] - c.rgb[0], o[1] - c.rgb[1], o[2] - c.rgb[2]) >= 60)) {
            chosen.push(c.rgb);
        }
    }

    const hex = v => Math.round(v).toString(16).padStart(2, '0');
    return chosen.map(c => ('#' + hex(c[0]) + hex(c[1]) + hex(c[2])).toUpperCase());
}

// Buckets pixels on a 16-level-per-channel grid. "Vivid" pixels (saturation ≥ 0.3, not too dark) are preferred
// when there are enough of them, weighted by saturation × brightness so a gold title beats a muddy background.
// The winner is the bucket with the most weight in its 3×3×3 neighbourhood (so a gradient isn't split across
// buckets); its plain average color is returned. Tuned on real Open Library covers (2026-10-10).
export function pick(data) {
    const all = new Map();
    const vivid = new Map();
    let total = 0;
    let vividCount = 0;

    for (let y = EDGE; y < H - EDGE; y++) {
        for (let x = EDGE; x < W - EDGE; x++) {
            const i = (y * W + x) * 4;
            if (data[i + 3] < 128) {
                continue;
            }

            const r = data[i], g = data[i + 1], b = data[i + 2];
            const key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            add(all, key, r, g, b, 1);
            total++;

            const max = Math.max(r, g, b);
            const min = Math.min(r, g, b);
            const saturation = max === 0 ? 0 : (max - min) / max;
            if (saturation >= 0.3 && max >= 60) {
                add(vivid, key, r, g, b, saturation * (max / 255));
                vividCount++;
            }
        }
    }

    if (total === 0) {
        return null;
    }

    const source = vividCount >= total * 0.15 ? vivid : all;
    let best = null;
    let bestScore = -1;
    for (const [key, bucket] of source) {
        const score = neighbourhood(source, key);
        if (score > bestScore) {
            best = bucket;
            bestScore = score;
        }
    }

    const hex = v => Math.round(v / best.count).toString(16).padStart(2, '0');
    return ('#' + hex(best.r) + hex(best.g) + hex(best.b)).toUpperCase();
}

function add(map, key, r, g, b, weight) {
    let e = map.get(key);
    if (!e) {
        e = { weight: 0, count: 0, r: 0, g: 0, b: 0 };
        map.set(key, e);
    }
    e.weight += weight;
    e.count++;
    e.r += r;
    e.g += g;
    e.b += b;
}

function neighbourhood(map, key) {
    const r = key >> 8, g = (key >> 4) & 15, b = key & 15;
    let sum = 0;
    for (let dr = -1; dr <= 1; dr++) {
        for (let dg = -1; dg <= 1; dg++) {
            for (let db = -1; db <= 1; db++) {
                const rr = r + dr, gg = g + dg, bb = b + db;
                if (rr < 0 || gg < 0 || bb < 0 || rr > 15 || gg > 15 || bb > 15) {
                    continue;
                }
                const e = map.get((rr << 8) | (gg << 4) | bb);
                if (e) {
                    // The bucket itself counts double so the winner is a real peak, not a gap between two.
                    sum += (dr === 0 && dg === 0 && db === 0) ? e.weight * 2 : e.weight;
                }
            }
        }
    }
    return sum;
}
