// Stands in for the page-wide touchmove listener SortableJS adds on import, which is patched out of
// lib/sortable/sortable.esm.min.js (see lib/sortable/PATCHES.md). A non-passive touchmove listener on the document makes
// every touch scroll wait for the main thread, so it is only added while some page actually has dragging switched on.
import Sortable from '../lib/sortable/sortable.esm.min.js';

const owners = new Set();

function guard(e) {
    // While a book or row is being dragged, the finger moves the drag copy, not the page.
    if (Sortable.active && e.cancelable) {
        e.preventDefault();
    }
}

/** Turn the guard on for an owner (e.g. 'shelves'); it stays on while any owner holds it. */
export function hold(owner) {
    if (owners.size === 0) {
        document.addEventListener('touchmove', guard, { passive: false });
    }
    owners.add(owner);
}

/** Turn the guard off for an owner; the listener goes when the last owner lets go. */
export function release(owner) {
    if (owners.delete(owner) && owners.size === 0) {
        document.removeEventListener('touchmove', guard);
    }
}

/** For tests: whether the listener is on. */
export function isHeld() {
    return owners.size > 0;
}
