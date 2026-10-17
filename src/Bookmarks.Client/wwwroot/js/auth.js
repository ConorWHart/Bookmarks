// Sign-in for Bookmarks, on top of MSAL (vendored at lib/msal/msal-browser.min.js, loaded by index.html
// so the service worker precaches it and sign-in state survives offline starts).
//
// Offline-first rules:
//  - init() only reads MSAL's cached account; it never waits on the network, so the app starts signed in
//    even with no connection and only needs a token when it actually calls the API.
//  - Redirect flow only. Popups are blocked or confusing inside installed PWAs (especially on iOS).

let app = null;
let apiScope = '';

// True after a silent token request failed in a way only the user can fix (the sign-in session ended).
// SPA refresh tokens last 24 hours; after that MSAL tries a hidden iframe, which times out when the
// browser blocks the sign-in site's cookies there. Either way the user has to sign in again.
let signInNeeded = false;

const NeedsInteraction = new Set([
    'timed_out', 'monitor_window_timeout', 'login_required', 'interaction_required', 'consent_required',
    'no_tokens_found', 'refresh_token_expired', 'bad_token',
]);

function needsInteraction(error) {
    if (!navigator.onLine) {
        return false; // offline: wait for the network, not the user
    }
    const msal = globalThis.msal;
    return (msal?.InteractionRequiredAuthError && error instanceof msal.InteractionRequiredAuthError)
        || NeedsInteraction.has(error?.errorCode);
}

/** True when the last token request needs the user to sign in again (see getToken). */
export function needsSignIn() {
    return signInNeeded;
}

/** [userId, displayName, email] for the active account, or null. */
function describe(account) {
    if (!account) {
        return null;
    }

    const claims = account.idTokenClaims ?? {};
    // oid is the directory object id: the same value the API stores as UserId.
    const userId = claims.oid ?? account.localAccountId;
    return [userId ?? '', claims.name ?? account.name ?? '', account.username ?? ''];
}

/**
 * Creates the MSAL app, completes a sign-in redirect if we just came back from one,
 * and returns the cached account (or null).
 */
export async function init(clientId, authority, knownAuthority, redirectUri, scope) {
    apiScope = scope;

    const msal = globalThis.msal;
    if (!msal) {
        throw new Error('MSAL failed to load (lib/msal/msal-browser.min.js).');
    }

    app = new msal.PublicClientApplication({
        auth: {
            clientId,
            authority,
            knownAuthorities: [knownAuthority],
            redirectUri,
            postLogoutRedirectUri: new URL('.', document.baseURI).href,
            // Return to the page the user started from once the redirect has been processed.
            navigateToLoginRequestUrl: true,
        },
        cache: {
            // Survives tab closes and app restarts, which is what an installed PWA needs.
            cacheLocation: 'localStorage',
            storeAuthStateInCookie: false,
        },
    });

    await app.initialize();

    try {
        const result = await app.handleRedirectPromise();
        if (result?.account) {
            app.setActiveAccount(result.account);
        }
    } catch (error) {
        // A failed or abandoned redirect shouldn't stop the app from starting.
        console.warn('Sign-in redirect could not be completed', error);
    }

    if (!app.getActiveAccount()) {
        const accounts = app.getAllAccounts();
        if (accounts.length > 0) {
            app.setActiveAccount(accounts[0]);
        }
    }

    return describe(app.getActiveAccount());
}

export function currentAccount() {
    return app ? describe(app.getActiveAccount()) : null;
}

export async function signIn() {
    await app.loginRedirect({ scopes: [apiScope], prompt: 'select_account' });
}

/**
 * Signs in again as the same account after the session ended. A full-page redirect: if the sign-in site
 * still remembers the user it comes straight back without asking; otherwise it shows the sign-in page.
 * Unsynced changes are safe in IndexedDB and sync once the app is back.
 */
export async function renewSignIn() {
    const account = app?.getActiveAccount();
    if (!account) {
        await signIn();
        return;
    }
    await app.acquireTokenRedirect({ account, scopes: [apiScope] });
}

// ---------- Device sessions (see claude/device-sessions-plan.md) ----------
// Our API's own session token, which keeps the device signed in for sync for 90 days after its last use
// (Entra alone only lasts 24 hours in a single-page app). One per account, shared by every tab.

function sessionKey() {
    const userId = describe(app?.getActiveAccount())?.[0];
    return userId ? `bookmarks.session.${userId}` : null;
}

/** [token, rotateAfter, expiresAt] (ISO dates) for the current account, or null. */
export function getSession() {
    const key = sessionKey();
    if (!key) {
        return null;
    }
    try {
        const value = JSON.parse(localStorage.getItem(key) ?? 'null');
        return value?.token ? [value.token, value.rotateAfter ?? '', value.expiresAt ?? ''] : null;
    } catch {
        return null;
    }
}

export function setSession(token, rotateAfter, expiresAt) {
    const key = sessionKey();
    if (key) {
        try {
            localStorage.setItem(key, JSON.stringify({ token, rotateAfter, expiresAt }));
        } catch (error) {
            console.warn('Could not store the device session', error);
        }
    }
}

export function clearSession() {
    const key = sessionKey();
    if (key) {
        try {
            localStorage.removeItem(key);
        } catch {
            // Storage blocked: nothing was stored either.
        }
    }
}

/** "Chrome on Windows", for recognising the session on the server. */
export function deviceName() {
    const ua = navigator.userAgent;
    const browser = /Edg\//.test(ua) ? 'Edge'
        : /OPR\//.test(ua) ? 'Opera'
        : /Firefox\//.test(ua) ? 'Firefox'
        : /Chrome\//.test(ua) ? 'Chrome'
        : /Safari\//.test(ua) ? 'Safari'
        : 'Browser';
    const os = /iPhone|iPad|iPod/.test(ua) ? 'iOS'
        : /Android/.test(ua) ? 'Android'
        : /Windows/.test(ua) ? 'Windows'
        : /Mac OS X|Macintosh/.test(ua) ? 'macOS'
        : /Linux/.test(ua) ? 'Linux'
        : 'unknown device';
    const installed = window.matchMedia?.('(display-mode: standalone)').matches ? ' (app)' : '';
    return `${browser} on ${os}${installed}`;
}

export async function signOut() {
    await app.logoutRedirect({ account: app.getActiveAccount() });
}

/**
 * An access token from the cache, refreshed silently if needed.
 * Returns null when there's no account, no network, or the user has to sign in again;
 * callers treat that as "not connected right now" rather than an error.
 */
export async function getToken() {
    const account = app?.getActiveAccount();
    if (!account) {
        return null;
    }

    try {
        const result = await app.acquireTokenSilent({ account, scopes: [apiScope] });
        signInNeeded = false;
        return result.accessToken ?? null;
    } catch (error) {
        signInNeeded = needsInteraction(error);
        console.warn(signInNeeded ? 'Sign-in has ended; the user needs to sign in again' : 'Silent token request failed', error);
        return null;
    }
}
