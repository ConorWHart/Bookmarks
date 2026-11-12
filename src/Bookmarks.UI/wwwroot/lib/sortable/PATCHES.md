# Local changes to SortableJS 1.15.6

`sortable.esm.min.js` is the upstream build with one change. Re-apply it after any upgrade.

## Page-wide touchmove listener removed (2026-11, bookshelf scroll lag)

Upstream registers this as soon as the module is imported, and never removes it:

```js
qt&&y(document,"touchmove",function(o){(g.active||vt)&&o.cancelable&&o.preventDefault()}),
```

`y` adds listeners with `{passive:false}`, so with the module loaded Chrome cannot scroll a touch page on its own
thread: every swipe waits for JavaScript, and any busy moment in Blazor shows as scroll lag. That line is deleted.

`js/touchGuard.js` adds the equivalent guard (`Sortable.active` → `preventDefault`) only while a page has drag
handling switched on (shelfDrag.js while shelves are wired, listDrag.js while grips are showing). `vt` (awaiting a
native drag start) is never set in our use: both modules use `forceFallback: true`.
