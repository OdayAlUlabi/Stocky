// Link the 5 central Private DNS zones to the spoke VNet so private endpoints
// in the spoke resolve correctly. Deployed in the connectivity subscription,
// in the RG that hosts the central Private DNS zones.
@description('Spoke VNet resource id (full cross-sub id).')
param spokeVnetResourceId string

@description('Short name used in the link resource name. Typically the spoke VNet name.')
param spokeVnetName string

@description('Azure region of the spoke (used to derive the ACA private DNS zone name).')
param spokeRegion string

@description('Tags applied to the link resources.')
param tags object = {}

var zoneNames = [
  #disable-next-line no-hardcoded-env-urls
  'privatelink.database.windows.net'
  'privatelink.vaultcore.azure.net'
  'privatelink.azurecr.io'
  'privatelink.blob.${environment().suffixes.storage}'
  'privatelink.${spokeRegion}.azurecontainerapps.io'
]

resource zones 'Microsoft.Network/privateDnsZones@2024-06-01' existing = [for z in zoneNames: {
  name: z
}]

resource links 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (z, i) in zoneNames: {
  parent: zones[i]
  name: 'link-${spokeVnetName}'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: spokeVnetResourceId }
  }
}]
