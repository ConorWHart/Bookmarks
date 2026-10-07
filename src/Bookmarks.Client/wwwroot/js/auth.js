// Sign-in for Bookmarks, on top of MSAL (vendored at lib/msal/msal-browser.min.js, loaded by index.html
// so the service worker precaches it and sign-in state survives offline starts).
//
// Offline-first rules:
//  - init() only reads MSAL's cached account; it never waits on the network, so the app starts signed in
//    even with no connection and only needs a token when it actually calls the API.
//  - Redirect flow only. Popups are blocked or confusing inside installed PWAs (especially on iOS).

let app = null;
let apiScope = '';

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
        return result.accessToken ?? null;
    } catch (error) {
        console.warn('Silent token request failed', error);
        return null;
    }
}
