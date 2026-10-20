// For OverlayHistory.cs: go back `count` history entries and resolve once the browser has done it (popstate),
// or after a short timeout if it never comes.
export function back(count) {
    return new Promise(resolve => {
        let finished = false;
        const done = () => {
            if (!finished) {
                finished = true;
                window.removeEventListener('popstate', done);
                resolve();
            }
        };

        window.addEventListener('popstate', done);
        history.go(-Math.max(1, count || 1));
        setTimeout(done, 600);
    });
}
