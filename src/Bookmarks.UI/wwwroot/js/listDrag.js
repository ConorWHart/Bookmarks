// Drag-to-reorder for plain lists by a grip handle (Phase 6c): bookcases and their shelves on the Bookcases page.
// Like shelfDrag.js, SortableJS only handles the gesture: on drop the DOM is put back and .NET is told the move,
// so Blazor stays the one owner of the page.
//
// Markup:  <div data-sort-list="shelves">  …  <div data-sort-item="shelves" data-sort-id="{guid}">
//                                                 <span data-sort-handle="shelves">⋮⋮</span> …
// Each list only reorders its own items (a shelf can't be dropped into another bookcase), and only the handle
// starts a drag, so text fields and buttons in the rows work as usual.
import Sortable from '../lib/sortable/sortable.esm.min.js';

const instances = new Map(); // list element -> Sortable
let dotnet = null;
let origin = null;

/** Wire up every [data-sort-list] under root (call after each render). */
export function sync(root, dotnetRef) {
    dotnet = dotnetRef;

    for (const [el, sortable] of instances) {
        if (!el.isConnected || !root.contains(el)) {
            sortable.destroy();
            instances.delete(el);
        }
    }

    // root itself can be a list (the bookcases), and querySelectorAll only looks below it.
    const lists = [...root.querySelectorAll('[data-sort-list]')];
    if (root.matches('[data-sort-list]')) {
        lists.unshift(root);
    }

    for (const el of lists) {
        if (!instances.has(el)) {
            instances.set(el, create(el, el.getAttribute('data-sort-list')));
        }
    }
}

export function dispose() {
    for (const sortable of instances.values()) {
        sortable.destroy();
    }
    instances.clear();
    dotnet = null;
    origin = null;
}

let groupCount = 0;

function create(el, kind) {
    return Sortable.create(el, {
        group: { name: `bm-sort-${++groupCount}`, pull: false, put: false },
        draggable: `[data-sort-item="${kind}"]`,
        handle: `[data-sort-handle="${kind}"]`,
        animation: 150,

        // The handle has touch-action: none, so a touch on it can start straight away.
        forceFallback: true,
        fallbackOnBody: false, // the copy stays inside the page's scoped styles
        fallbackTolerance: 3,

        ghostClass: 'bm-sort-ghost',
        chosenClass: 'bm-sort-chosen',
        dragClass: 'bm-sort-copy',

        scroll: true,
        scrollSensitivity: 80,
        bubbleScroll: true,

        onStart: evt => {
            origin = { parent: evt.from, next: evt.item.nextSibling };
            if (navigator.vibrate) {
                try { navigator.vibrate(15); } catch { /* fine */ }
            }
        },

        onEnd: evt => {
            if (!origin) {
                return;
            }

            const { parent, next } = origin;
            origin = null;
            parent.insertBefore(evt.item, next && next.parentNode === parent ? next : null);

            const id = evt.item.getAttribute('data-sort-id');
            const from = evt.oldDraggableIndex;
            const to = evt.newDraggableIndex;
            if (!id || from === undefined || to === undefined || from === to) {
                return;
            }

            dotnet?.invokeMethodAsync('OnReordered', kind, id, from, to)
                .catch(err => console.error('Bookmarks: saving the new order failed', err));
        },
    });
}
