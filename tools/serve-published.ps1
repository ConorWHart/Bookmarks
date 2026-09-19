<#
.SYNOPSIS
    Publishes Bookmarks.Client in Release and serves it as static files, for testing the
    installed PWA and offline mode (the development service worker does nothing).

.EXAMPLE
    ./tools/serve-published.ps1            # http://localhost:5080
    ./tools/serve-published.ps1 -Port 8080 -NoPublish
#>
param(
    [int] $Port = 5080,
    [switch] $NoPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'publish/client'
$site = Join-Path $out 'wwwroot'

if (-not $NoPublish) {
    dotnet publish (Join-Path $root 'src/Bookmarks.Client') -c Release -o $out
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# serve's defaults would break the service worker: "cleanUrls" redirects /index.html to /,
# and a cached redirect can't be used to answer a navigation. Turn it off, add the SPA
# fallback (--single), and never cache service-worker.js so updates are noticed.
$config = @'
{
  "cleanUrls": false,
  "headers": [
    {
      "source": "service-worker.js",
      "headers": [ { "key": "Cache-Control", "value": "no-cache" } ]
    }
  ]
}
'@
# UTF-8 without a BOM (Windows PowerShell's -Encoding utf8 adds one, which breaks JSON.parse).
[IO.File]::WriteAllText((Join-Path $site 'serve.json'), $config)

Write-Host ""
Write-Host "Serving $site at http://localhost:$Port  (Ctrl+C to stop)" -ForegroundColor Green
Write-Host "Note: this is a different origin from https://localhost:7101, so it has its own (empty) library."
Write-Host ""

npx --yes serve --single --listen $Port $site
