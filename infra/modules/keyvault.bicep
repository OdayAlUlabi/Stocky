@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object
@description('Resource token (lowercase) for uniqueness of globally-scoped names.')
param resourceToken string
@description('Private endpoint subnet id.')
param peSubnetId string
@description('Private DNS zone id for vaultcore.')
param dnsZoneId string

// KV global name <=24 chars, alphanumeric+hyphen. CAF: kv-{workload}-{env}-{token}.
resource kv 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
  name: take('kv-${prefix}-${resourceToken}', 24)
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    publicNetworkAccess: 'Disabled'
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: true
    enabledForTemplateDeployment: false
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

resource pe 'Microsoft.Network/privateEndpoints@2024-01-01' = {
  name: 'pep-${prefix}-kv'
  location: location
  tags: tags
  properties: {
    subnet: { id: peSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'kv'
        properties: {
          privateLinkServiceId: kv.id
          groupIds: [ 'vault' ]
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
        name: 'vault'
        properties: { privateDnsZoneId: dnsZoneId }
      }
    ]
  }
}

output kvName string = kv.name
output kvId string = kv.id
output kvUri string = kv.properties.vaultUri
