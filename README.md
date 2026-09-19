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

### Testing the PWA / offline mode

The development service worker deliberately does nothing. To test installation and offline behavior:

```powershell
./tools/serve-published.ps1        # publishes Release, then serves it at http://localhost:5080 (needs Node for npx)
```

Then open it in Chrome, install it (address bar > Install), and toggle DevTools > Network > Offline.
The script configures `serve` so `/index.html` isn't redirected (a cached redirect breaks offline navigation).
Deploying a new build while the app is open shows "A new version of Bookmarks is ready · Reload".

## Deploy infrastructure

```powershell
az login
az group create -n rg-bookmarks-dev -l westus3
# edit infra/main.parameters.json: your Entra UPN + object id (az ad signed-in-user show --query id -o tsv)
az deployment group create -g rg-bookmarks-dev -f infra/main.bicep -p infra/main.parameters.json
```

After the first deployment, grant the API's managed identity access to the database. Connect to the database as the Entra admin and run:

```sql
CREATE USER [app-bookmarks-api-dev-<suffix>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [app-bookmarks-api-dev-<suffix>];
ALTER ROLE db_datawriter ADD MEMBER [app-bookmarks-api-dev-<suffix>];
ALTER ROLE db_ddladmin  ADD MEMBER [app-bookmarks-api-dev-<suffix>];  -- for EF migrations; remove later if migrations run elsewhere
```

## Roadmap

1. ✅ Scaffold: solution, CI, Bicep, first shelf view
2. Local-only app: IndexedDB store, add/edit books, bookcases and shelves, offline PWA
3. API + Azure SQL + Entra External ID sign-in
4. Sync engine: outbox push, checkpoint pull, conflicts
5. Book lookup (Open Library / Google Books), barcode scanning, cover caching, dominant-color extraction
6. Library customization: presets, more display styles, themes, drag ordering
7. Polish, then .NET MAUI Blazor Hybrid

See the architecture plan in the Claude project for design decisions.
