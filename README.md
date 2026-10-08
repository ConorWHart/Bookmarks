# Bookmarks

Your physical library, virtually: an offline-first Blazor WebAssembly PWA with an Azure backend.

## Solution layout

| Project | Purpose |
|---|---|
| `src/Bookmarks.Shared` | Domain entities, sync base type and wire contracts, shared by client and API |
| `src/Bookmarks.Core` | Client-side logic with no UI: local store abstraction, library query engine (filter, sort, group), shelf layout, rank keys, and later the sync engine |
| `src/Bookmarks.UI` | Razor Class Library: every page and component (MudBlazor + custom shelf rendering). Shared with the future MAUI app |
| `src/Bookmarks.Client` | Blazor WebAssembly PWA host: service worker, manifest, and the browser storage implementation |
| `src/Bookmarks.Api` | ASP.NET Core Minimal API (Azure SQL via EF Core, sync endpoints) |
| `tests/Bookmarks.Tests` | xUnit tests for Core |
| `infra/` | Bicep: Azure SQL (serverless/free), App Service (API), Static Web App (client), App Insights |

## Prerequisites

- .NET 10 SDK (installed with Visual Studio 2026 and the "ASP.NET and web development" workload)
- Optional: Azure CLI with Bicep (`az bicep install`) for infrastructure

## Run locally

1. Open `Bookmarks.slnx` in Visual Studio 2026.
2. Set **Bookmarks.Client** as the startup project (or configure multiple startup projects with `Bookmarks.Api`).
3. Press F5. The library is stored in the browser's IndexedDB; an empty library offers "Load demo library".

Ports: client `https://localhost:7101`, API `https://localhost:7201`.

### API and database

The API uses SQL Server LocalDB in development (`appsettings.Development.json`). Create or update the schema from the
**Package Manager Console** (Default project: `Bookmarks.Api`):

```powershell
Add-Migration <Name> -Project Bookmarks.Api -StartupProject Bookmarks.Api -OutputDir Data/Migrations
Update-Database -Project Bookmarks.Api -StartupProject Bookmarks.Api
```

The EF tools use `DesignTimeDbContextFactory` (LocalDB), so they don't need auth settings. Every `/api` endpoint
except `/api/version` needs an Entra External ID access token with the `access_as_user` scope (`Auth` section in
appsettings). `tests/Bookmarks.Api.Tests` runs its SQL Server tests in Docker (Testcontainers) and skips them when
Docker isn't running.

### Testing the PWA / offline mode

The development service worker deliberately does nothing. To test installation and offline behavior:

```powershell
./tools/serve-published.ps1        # publishes Release, then serves it at http://localhost:5080 (needs Node for npx)
```

Then open it in Chrome, install it (address bar > Install), and toggle DevTools > Network > Offline.
The script configures `serve` so `/index.html` isn't redirected (a cached redirect breaks offline navigation).
Deploying a new build while the app is open shows "A new version of Bookmarks is ready · Reload".

## Azure, accounts and deployment

One-time setup of the Entra External ID tenant (email + password sign-in), the app registrations, the GitHub deploy identity and the SQL admin group: see [docs/azure-setup.md](docs/azure-setup.md).

Deployment runs from GitHub Actions on every push to `main` that builds green (`.github/workflows/ci.yml`, job
**Deploy to Azure (dev)**, GitHub environment `dev`). It signs in with OIDC (no stored Azure credentials), deploys
`infra/main.bicep`, applies an idempotent EF Core migration script, creates the API identity's SQL user, then
publishes the API to App Service and the PWA to Static Web Apps. The Static Web Apps deployment token is fetched
at deploy time and masked, so no deployment secret is stored in GitHub either.

The deployed API URL is written into `wwwroot/appsettings.Production.json` during the build, so the published PWA
talks to App Service while local runs keep using `https://localhost:7201`.

After the **first** deploy, add the Static Web App's URL as a redirect URI on the `Bookmarks` app registration
(`https://<name>.azurestaticapps.net/authentication/login-callback`) — see `docs/azure-setup.md`, step 10.

## Roadmap

1. ✅ Scaffold: solution, CI, Bicep, first shelf view
2. ✅ Local-only app: IndexedDB store, add/edit books, bookcases and shelves, offline PWA
3. ✅ API + Azure SQL + Entra External ID sign-in (email + password), deployed from GitHub Actions
4. Sync engine: outbox push, checkpoint pull, conflicts
5. Book lookup (Open Library / Google Books), barcode scanning, cover caching, dominant-color extraction
6. Library customization: presets, more display styles, themes, drag ordering
7. Polish and nice-to-haves (e.g. Google sign-in), then .NET MAUI Blazor Hybrid

See the architecture plan in the Claude project for design decisions.
