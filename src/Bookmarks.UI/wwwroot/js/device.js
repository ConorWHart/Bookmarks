// Small device checks for .NET, kept apart from the drag modules so asking doesn't load SortableJS.

/** A touch screen (phone, tablet): the Library offers Arrange mode instead of always-on dragging. */
export function isTouchScreen() {
    return matchMedia('(pointer: coarse)').matches;
}
