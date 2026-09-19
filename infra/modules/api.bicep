param location string
param tags object
param planName string
param appName string
param planSku string
param sqlServerFqdn string
param sqlDatabaseName string
param appInsightsConnectionString string
param corsOrigins array

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: planSku
  }
  properties: {
    reserved: true // Linux
  }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: planSku != 'F1'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/health'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        // Array config binds as Cors:AllowedOrigins:0, :1, ...
        {
          name: 'Cors__AllowedOrigins__0'
          value: corsOrigins[0]
        }
      ]
      connectionStrings: [
        {
          name: 'Bookmarks'
          type: 'SQLAzure'
          // Managed identity auth; the identity must be created as a DB user (see README).
          connectionString: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;'
        }
      ]
    }
  }
}

output appName string = app.name
output defaultHostname string = app.properties.defaultHostName
output principalId string = app.identity.principalId
