# Sign-in page branding (Entra External ID)

Makes the hosted sign-in page (`bookmarksapp.ciamlogin.com`) look like Bookmarks: a dim wall of bookshelves,
a walnut card with the honey accent, our logo and wording. Nothing in the app changes.

## Files (`out/`)
| File | Where it goes | Limit |
|---|---|---|
| `favicon.png` (32×32) | Basics → Favicon | 5 KB |
| `background.jpg` (1920×1080) | Basics → Background image | 300 KB |
| `banner-logo.png` (245×36) | Sign-in form → Banner logo | 50 KB |
| `square-logo.png` (240×240) | Sign-in form → Square logo (light **and** dark) | 50 KB |
| `bookmarks-signin.css` | Layout → Custom CSS | — |

`make_assets.py` regenerates the images (Python + Pillow; it reads the app icon `src/Bookmarks.Client/wwwroot/icon-512.png`).
`preview-*.png` are mock-ups, not uploads.

## Upload steps
1. Go to [entra.microsoft.com](https://entra.microsoft.com). Use the settings (gear) icon → **Directories + subscriptions** to switch to the **external tenant** (`bookmarksapp`). Don't use the workforce tenant.
2. Open **Company branding** (search for it at the top if it isn't in the menu) → **Default sign-in experience** → **Edit** (or **Customize**).
3. **Basics** tab:
   - Favicon: `favicon.png`.
   - Background image: `background.jpg`.
   - Page background color: `#151210` (shown while the image loads).
4. **Layout** tab:
   - Template: **Full-screen background**.
   - Custom CSS: upload `bookmarks-signin.css`.
   - Header: off.
   - Footer: on. Untick "Privacy & Cookies" and "Terms of Use" (they point to Microsoft's pages, not ours).
5. **Sign-in form** tab:
   - Banner logo: `banner-logo.png`.
   - Square logo (light theme) and Square logo (dark theme): `square-logo.png`.
   - Username hint text: `Email`.
   - Sign-in page text: `Your library, on every device. Sign in to sync your shelves.`
   - Self-service password reset: leave as is (it's already on).
6. **Review + save**. Changes can take a few minutes to show. Use a private window to see them, since the old styles stay cached.

## Check
Do each of these on a desktop and a phone, using the live site → Sign in:
1. Sign in with email and password.
2. **No account? Create one**: the email code page, then the password and name pages.
3. **Forgot password?**: the email code, then the new password.
4. A wrong password, to check the error text is readable (pinkish red on the dark card).

If any text shows dark-on-dark, send a screenshot. The CSS only targets the classes Microsoft documents, and a page element they don't expose may need a fallback colour.

## Notes
- External ID tenants keep custom CSS support. Microsoft is retiring it only for *workforce* tenants. Even so, the CSS avoids the layout properties they're phasing out (`position`, `margin`, `display`, `transform`, `opacity`, …) and only sets colours, borders, padding, radius, shadows and fonts.
- The sign-in page can't load web fonts, so it uses Georgia for headings and Segoe UI (Windows/Edge), Inter or Roboto for body text. That's the same stack as the app.
