// Bookmarks: Azure infrastructure
// Deployed from GitHub Actions (.github/workflows/ci.yml, job "deploy") after a green build on main.
// To run it by hand:
//   az group create -n rg-bookmarks-dev -l westus3
//   az deployment group create -g rg-bookmarks-dev -f infra/main.bicep -p infra/main.parameters.json
//
// Resources:
//   - Azure SQL logical server (Entra-only auth) + serverless database on the free offer
//   - Linux App Service plan + Web App for Bookmarks.Api, with a user-assigned identity for SQL
//   - Log Analytics + Application Insights
//   - Static Web App (Free) for the Blazor WASM PWA

targetScope = 'resourceGroup'

@description('Short environment name, used in resource names (dev, prod, ...).')
@maxLength(8)
param environmentName string = 'dev'

@description('Region for everything except the Static Web App.')
param location string = resourceGroup().location

@description('Static Web Apps is only available in a handful of regions.')
@allowed([ 'westus2', 'centralus', 'eastus2', 'westeurope', 'eastasia' ])
param staticWebAppLocation string = 'westus2'

@description('Name of the Entra group that administers the SQL server.')
param sqlAdminLogin string

@description('Object id of that Entra group.')
param sqlAdminObjectId string

@description('Entra External ID authority, e.g. https://bookmarksapp.ciamlogin.com/{tenantId}/v2.0')
param authAuthority string

@description('Application (client) id of the "Bookmarks API" app registration.')
param authAudience string

@description('App Service plan SKU. F1 is free but has daily CPU quotas; B1 is the cheapest always-on option.')
@allowed([ 'F1', 'B1', 'B2', 'P0v3' ])
param apiPlanSku string = 'F1'

@description('Use the Azure SQL free offer (100k vCore-seconds + 32 GB per month, one per subscription).')
param useSqlFreeLimit bool = true

var suffix = uniqueString(subscription().id, resourceGroup().id, environmentName)
var tags = {
  app: 'bookmarks'
  env: environmentName
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    tags: tags
    name: 'bookmarks-${environmentName}-${suffix}'
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    tags: tags
    serverName: 'sql-bookmarks-${environmentName}-${suffix}'
    databaseName: 'bookmarks'
    adminLogin: sqlAdminLogin
    adminObjectId: sqlAdminObjectId
    useFreeLimit: useSqlFreeLimit
  }
}

module staticWebApp 'modules/staticwebapp.bicep' = {
  name: 'staticWebApp'
  params: {
    location: staticWebAppLocation
    tags: tags
    name: 'swa-bookmarks-${environmentName}-${suffix}'
  }
}

module api 'modules/api.bicep' = {
  name: 'api'
  params: {
    location: location
    tags: tags
    planName: 'asp-bookmarks-${environmentName}-${suffix}'
    appName: 'app-bookmarks-api-${environmentName}-${suffix}'
    identityName: 'id-bookmarks-api-${environmentName}-${suffix}'
    planSku: apiPlanSku
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    authAuthority: authAuthority
    authAudience: authAudience
    corsOrigins: [
      'https://${staticWebApp.outputs.defaultHostname}'
    ]
  }
}

output apiAppName string = api.outputs.appName
output apiUrl string = 'https://${api.outputs.defaultHostname}'
output apiIdentityName string = api.outputs.identityName
output apiIdentityClientId string = api.outputs.identityClientId
output staticWebAppName string = staticWebApp.outputs.name
output clientUrl string = 'https://${staticWebApp.outputs.defaultHostname}'
output sqlServerName string = sql.outputs.serverName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
