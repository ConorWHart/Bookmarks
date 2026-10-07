param location string
param tags object
param serverName string
param databaseName string
param adminLogin string
param adminObjectId string
param useFreeLimit bool

resource server 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // Entra-only: no SQL logins / passwords anywhere. The API connects with its managed identity.
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: adminLogin
      sid: adminObjectId
      tenantId: subscription().tenantId
      principalType: 'Group' // 'Bookmarks SQL Admins': Conor + the GitHub deploy identity
    }
  }
}

// Lets Azure services (the App Service) reach the server. Tighten with VNet integration later.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 34359738368 // 32 GB (free offer limit)
    autoPauseDelay: 60 // minutes
    minCapacity: json('0.5')
    useFreeLimit: useFreeLimit
    freeLimitExhaustionBehavior: useFreeLimit ? 'AutoPause' : null
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
