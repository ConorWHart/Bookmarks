// Phones (Phase 8e): the Library's toolbar slides away while the page scrolls down and comes back on any scroll up
// (or near the top), so the shelves get the whole screen. Pure JS: adds/removes a class, no calls into .NET.
// Blazor never rewrites the element's class attribute (it doesn't change between renders), so the class sticks.

const TopZonePx = 80;  // always shown this close to the top
const MinMovePx = 8;   // ignore jitter

const watched = new Map(); // element -> { onScroll, hiddenClass }

/** Starts watching; safe to call after every render (each element is wired once). */
export function watch(el, hiddenClass) {
    for (const [other, entry] of watched) {
        if (!other.isConnected) {
            window.removeEventListener('scroll', entry.onScroll);
            watched.delete(other);
        }
    }

    if (!el || watched.has(el)) {
        return;
    }

    let lastY = window.scrollY;
    let queued = false;
    const onScroll = () => {
        if (queued) {
            return;
        }

        queued = true;
        requestAnimationFrame(() => {
            queued = false;
            const y = window.scrollY;
            const dy = y - lastY;
            if (y < TopZonePx) {
                el.classList.remove(hiddenClass);
                lastY = y;
            } else if (dy > MinMovePx) {
                // Not while typing in it (the search field), or the keyboard's own scroll would hide it.
                if (!el.contains(document.activeElement)) {
                    el.classList.add(hiddenClass);
                }
                lastY = y;
            } else if (dy < -MinMovePx) {
                el.classList.remove(hiddenClass);
                lastY = y;
            }
        });
    };

    window.addEventListener('scroll', onScroll, { passive: true });
    watched.set(el, { onScroll, hiddenClass });
}

export function dispose() {
    for (const [el, entry] of watched) {
        window.removeEventListener('scroll', entry.onScroll);
        el.classList.remove(entry.hiddenClass);
    }
    watched.clear();
}
