// Premium ACR (required for private endpoints), public access disabled,
// anonymous pull disabled, AAD-only auth.
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
@description('Private DNS zone id for azurecr.io.')
param dnsZoneId string

// ACR global name 5-50 chars, alphanumeric only (no hyphens). CAF: cr{workload}{env}{token}.
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  #disable-next-line BCP334
  name: take('cr${replace(prefix, '-', '')}${resourceToken}', 50)
  location: location
  tags: tags
  sku: { name: 'Premium' }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Disabled'
    anonymousPullEnabled: false
    zoneRedundancy: 'Disabled'
    networkRuleBypassOptions: 'AzureServices'
    policies: {
      retentionPolicy: { status: 'enabled', days: 30 }
      trustPolicy: { type: 'Notary', status: 'disabled' }
    }
  }
}

resource pe 'Microsoft.Network/privateEndpoints@2024-01-01' = {
  name: 'pep-${prefix}-acr'
  location: location
  tags: tags
  properties: {
    subnet: { id: peSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'acr'
        properties: {
          privateLinkServiceId: acr.id
          groupIds: [ 'registry' ]
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
        name: 'acr'
        properties: { privateDnsZoneId: dnsZoneId }
      }
    ]
  }
}

output acrName string = acr.name
output acrId string = acr.id
output acrLoginServer string = acr.properties.loginServer
