// Reports an element's width to .NET as it changes (Phase 6d: the cover grid picks its column count from it).
const watched = new Map();

export function watch(element, dotnetRef) {
    unwatch(element);
    let last = Math.round(element.clientWidth);
    let frame = 0;
    const observer = new ResizeObserver(() => {
        cancelAnimationFrame(frame);
        frame = requestAnimationFrame(() => {
            const width = Math.round(element.clientWidth);
            if (width !== last && width > 0) {
                last = width;
                dotnetRef.invokeMethodAsync('OnWidthChanged', width);
            }
        });
    });
    observer.observe(element);
    watched.set(element, () => { cancelAnimationFrame(frame); observer.disconnect(); });
    return last;
}

export function unwatch(element) {
    const stop = watched.get(element);
    if (stop) {
        stop();
        watched.delete(element);
    }
}
