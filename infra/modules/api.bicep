param location string
param tags object
param planName string
param appName string
param identityName string
param planSku string
param sqlServerFqdn string
param sqlDatabaseName string
param appInsightsConnectionString string
param corsOrigins array

@description('Entra External ID authority the API validates tokens against.')
param authAuthority string

@description('Application (client) id of the "Bookmarks API" app registration.')
param authAudience string

// A user-assigned identity (rather than system-assigned) so its client id is known here: the deploy
// workflow creates the matching SQL user with CREATE USER ... WITH SID, which needs no directory lookup.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: tags
}

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
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: identity.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: planSku != 'F1'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      // Health check needs a Basic plan or better.
      healthCheckPath: planSku == 'F1' ? null : '/health'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'Auth__Authority'
          value: authAuthority
        }
        {
          name: 'Auth__Audience'
          value: authAudience
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
          // The user-assigned identity signs in to SQL; the deploy workflow creates its database user.
          connectionString: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;'
        }
      ]
    }
  }
}

output appName string = app.name
output defaultHostname string = app.properties.defaultHostName
output identityName string = identity.name
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
