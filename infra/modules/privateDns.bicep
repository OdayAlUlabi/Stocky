// Private DNS zones for the workload's private endpoints.
//
// Two modes:
//   1. Standalone (default): create the 5 zones in this RG and link them to the workload VNet.
//   2. ALZ / central DNS: zones already exist in the connectivity subscription; we just
//      reference them by resource id so PE zone groups can attach. The platform team is
//      responsible for creating the VNet links from those central zones to this spoke VNet.
@description('Workload VNet resource id (used only in standalone mode).')
param vnetId string
@description('Tags applied to all resources.')
param tags object
@description('Azure region for ACA env zone (regional).')
param acaRegion string

@description('Resource id of the resource group that hosts the central Private DNS zones (typically in the connectivity subscription). When empty, zones are created locally.')
param centralDnsZonesRgId string = ''

var useCentralDns = !empty(centralDnsZonesRgId)

// Order: sql, kv, acr, blob, aca
var zoneNames = [
  #disable-next-line no-hardcoded-env-urls
  'privatelink.database.windows.net'
  'privatelink.vaultcore.azure.net'
  'privatelink.azurecr.io'
  'privatelink.blob.${environment().suffixes.storage}'
  'privatelink.${acaRegion}.azurecontainerapps.io'
]

// ---- Standalone mode: create zones + VNet links ----
resource zones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for z in zoneNames: if (!useCentralDns) {
  name: z
  location: 'global'
  tags: tags
}]

resource zoneLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (z, i) in zoneNames: if (!useCentralDns) {
  parent: zones[i]
  name: 'link-vnet'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnetId }
  }
}]

// ---- Central DNS mode: compute cross-subscription resource ids for existing zones ----
// centralDnsZonesRgId format: /subscriptions/{sub}/resourceGroups/{rg}
var centralZoneIds = [for z in zoneNames: '${centralDnsZonesRgId}/providers/Microsoft.Network/privateDnsZones/${z}']

output sqlZoneId  string = useCentralDns ? centralZoneIds[0] : zones[0].id
output kvZoneId   string = useCentralDns ? centralZoneIds[1] : zones[1].id
output acrZoneId  string = useCentralDns ? centralZoneIds[2] : zones[2].id
output blobZoneId string = useCentralDns ? centralZoneIds[3] : zones[3].id
output acaZoneId  string = useCentralDns ? centralZoneIds[4] : zones[4].id
output dnsMode string = useCentralDns ? 'central' : 'standalone'
