// Drag books along a shelf, or onto another shelf (Phase 6c). Uses SortableJS for the gesture only:
// when a book is dropped, the DOM is put back exactly as it was and .NET is told where it went, so
// Blazor stays the one owner of the page and re-draws the shelves from the saved order.
//
// Mouse: press and drag. Touch: press and hold (~0.45 s) to pick a book up; a quick swipe still scrolls.
import Sortable from '../lib/sortable/sortable.esm.min.js';

const instances = new Map(); // shelf element -> Sortable
let dotnet = null;
let origin = null;           // where the dragged book started: { parent, next }

const HoldMs = 450;

/** Wire up every shelf under root (call after each render; new shelves are picked up, removed ones dropped). */
export function sync(root, dotnetRef) {
    dotnet = dotnetRef;

    for (const [el, sortable] of instances) {
        if (!el.isConnected || !root.contains(el)) {
            el.removeEventListener('contextmenu', blockTouchMenu);
            sortable.destroy();
            instances.delete(el);
        }
    }

    for (const el of root.querySelectorAll('[data-shelf-id]')) {
        if (!instances.has(el)) {
            el.addEventListener('contextmenu', blockTouchMenu);
            instances.set(el, create(el));
        }
    }
}

/** Remove every shelf's drag handling (leaving the Bookcases view, or the page). */
export function dispose() {
    for (const [el, sortable] of instances) {
        el.removeEventListener('contextmenu', blockTouchMenu);
        sortable.destroy();
    }
    instances.clear();
    dotnet = null;
    origin = null;
}

function create(el) {
    return Sortable.create(el, {
        group: 'bm-books',
        draggable: '[data-item-id]',
        animation: 150,

        // Touch: hold to pick up, so swiping along a shelf (or down the page) still scrolls.
        delay: HoldMs,
        delayOnTouchOnly: true,
        touchStartThreshold: 6,

        // The fallback (a copy that follows the pointer) behaves the same on mouse and touch,
        // and lets the copy leave the shelf's scrolling box.
        forceFallback: true,
        fallbackOnBody: true,
        fallbackTolerance: 4,

        ghostClass: 'bm-drag-ghost',
        chosenClass: 'bm-drag-chosen',
        dragClass: 'bm-drag-copy',

        scroll: true,
        scrollSensitivity: 60,
        bubbleScroll: true,

        onChoose: () => {
            // Picked up (after the hold on touch): a short buzz where supported.
            if (navigator.vibrate) {
                try { navigator.vibrate(15); } catch { /* not allowed: fine */ }
            }
        },

        onStart: evt => {
            origin = { parent: evt.from, next: evt.item.nextSibling };
            document.body.classList.add('bm-dragging');
        },

        onEnd: evt => {
            document.body.classList.remove('bm-dragging');
            if (!origin) {
                return;
            }

            const { parent, next } = origin;
            origin = null;

            // Put the book back where Blazor left it; .NET re-draws it in its new place.
            parent.insertBefore(evt.item, next && next.parentNode === parent ? next : null);
            swallowNextClick();

            const itemId = evt.item.getAttribute('data-item-id');
            const fromShelf = evt.from.getAttribute('data-shelf-id');
            const toShelf = evt.to.getAttribute('data-shelf-id');
            const index = evt.newDraggableIndex;

            if (!itemId || !fromShelf || !toShelf || index === undefined) {
                return;
            }
            if (fromShelf === toShelf && index === evt.oldDraggableIndex) {
                return; // dropped where it started
            }

            dotnet?.invokeMethodAsync('OnBookDropped', itemId, fromShelf, toShelf, index)
                .catch(err => console.error('Bookmarks: saving the drop failed', err));
        },
    });
}

// Letting go after a drag can fire a click on the book, which would open its details.
function swallowNextClick() {
    const stop = e => {
        e.stopPropagation();
        e.preventDefault();
    };
    window.addEventListener('click', stop, { capture: true, once: true });
    setTimeout(() => window.removeEventListener('click', stop, { capture: true }), 80);
}

// On Android, holding a finger on a cover opens the image menu instead of picking the book up.
// Mouse right-click is left alone.
function blockTouchMenu(e) {
    const touch = e.pointerType === 'touch' || e.sourceCapabilities?.firesTouchEvents;
    if (touch && e.target.closest?.('[data-item-id]')) {
        e.preventDefault();
    }
}
