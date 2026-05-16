// Entra-only SQL server: no SQL auth, public network disabled, PE only.
@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object
@description('Resource token for uniqueness.')
param resourceToken string
@description('Private endpoint subnet id.')
param peSubnetId string
@description('Private DNS zone id for database.windows.net.')
param dnsZoneId string
@description('Object id of the Entra group/user that will be SQL Entra admin (break-glass).')
param sqlEntraAdminObjectId string
@description('Display name of the Entra admin principal.')
param sqlEntraAdminLogin string

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${prefix}-${take(resourceToken, 8)}'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    restrictOutboundNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlEntraAdminLogin
      sid: sqlEntraAdminObjectId
      tenantId: subscription().tenantId
      principalType: 'Group'
      azureADOnlyAuthentication: true
    }
  }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'sqldb-${prefix}'
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5_1'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
  }
}

resource pe 'Microsoft.Network/privateEndpoints@2024-01-01' = {
  name: 'pep-${prefix}-sql'
  location: location
  tags: tags
  properties: {
    subnet: { id: peSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'sql'
        properties: {
          privateLinkServiceId: sql.id
          groupIds: [ 'sqlServer' ]
        }
      }
    ]
  }
}

resource peDns 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-01-01' = {
  parent: pe
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'sql'
        properties: { privateDnsZoneId: dnsZoneId }
      }
    ]
  }
}

output serverName string = sql.name
output serverFqdn string = sql.properties.fullyQualifiedDomainName
output dbName string = db.name
