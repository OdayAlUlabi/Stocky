// Private DNS zone for an internal Container Apps environment.
//
// Azure does NOT auto-create this zone. Without it, any VNet resource
// (App Gateway, VMs, other apps) cannot resolve the internal FQDN:
//   {app}.internal.{defaultDomain}
//
// The wildcard A record routes all names in the zone to the environment's
// static IP, which is the single ingress point for the internal env.
@description('ACA environment defaultDomain (e.g. abc123.swedencentral.azurecontainerapps.io).')
param defaultDomain string
@description('ACA environment static IP address.')
param staticIp string
@description('Tags applied to all resources.')
param tags object

resource zone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: defaultDomain
  location: 'global'
  tags: tags
}

resource wildcard 'Microsoft.Network/privateDnsZones/A@2024-06-01' = {
  parent: zone
  name: '*'
  properties: {
    ttl: 3600
    aRecords: [ { ipv4Address: staticIp } ]
  }
}

// NOTE: Azure auto-creates the VNet link for internal ACA environments with VNet integration.
// Do NOT declare a vnetLink here — it will conflict on subsequent provisions.

output zoneId string = zone.id
