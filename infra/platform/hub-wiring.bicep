// ALZ platform-side wiring for the Stocky spoke. Deployed by the platform / connectivity
// team in the CONNECTIVITY subscription. The application landing zone deployment
// (../main.bicep) handles the spoke side; this file completes the three things only
// the platform team has rights to do:
//   1. hub -> spoke VNet peering
//   2. VNet links from each central Private DNS zone to the spoke VNet
//   3. egress firewall rules (FQDN + network) for the spoke source CIDRs
//
// Run:
//   az deployment sub create \
//     --location <region> \
//     --template-file infra/platform/hub-wiring.bicep \
//     --parameters infra/platform/hub-wiring.parameters.json
targetScope = 'subscription'

@description('Azure region for the subscription deployment record.')
param location string

@description('Resource group (in connectivity sub) that holds the hub VNet.')
param hubVnetResourceGroup string

@description('Hub VNet name.')
param hubVnetName string

@description('Resource group (in connectivity sub) that holds the central Private DNS zones.')
param dnsZonesResourceGroup string

@description('Resource group (in connectivity sub) that holds the Azure Firewall Policy.')
param firewallPolicyResourceGroup string

@description('Existing Azure Firewall Policy name in the hub.')
param firewallPolicyName string

@description('Spoke VNet full resource id (in the application sub).')
param spokeVnetResourceId string

@description('Spoke VNet name (used in peering + link resource names).')
param spokeVnetName string

@description('Spoke region (used to derive the ACA private DNS zone name).')
param spokeRegion string

@description('Source CIDRs in the spoke that traverse the hub firewall (ACA + PE subnets).')
param spokeSourceAddresses array

@description('Allow spoke to use hub VNet gateway (ExpressRoute / VPN).')
param allowGatewayTransit bool = false

@description('Tags applied to created resources.')
param tags object = {
  app: 'stocky'
  layer: 'platform-wiring'
}

// 1. hub -> spoke peering
module hubPeering 'modules/hubPeering.bicep' = {
  scope: resourceGroup(hubVnetResourceGroup)
  name: 'hub-peering-${spokeVnetName}'
  params: {
    hubVnetName: hubVnetName
    peeringName: 'peer-to-${spokeVnetName}'
    spokeVnetResourceId: spokeVnetResourceId
    allowForwardedTraffic: true
    allowGatewayTransit: allowGatewayTransit
  }
}

// 2. central Private DNS zone -> spoke VNet links
module dnsLinks 'modules/centralDnsLinks.bicep' = {
  scope: resourceGroup(dnsZonesResourceGroup)
  name: 'dns-links-${spokeVnetName}'
  params: {
    spokeVnetResourceId: spokeVnetResourceId
    spokeVnetName: spokeVnetName
    spokeRegion: spokeRegion
    tags: tags
  }
}

// 3. firewall egress allowlist for the spoke CIDRs
module fwRules 'modules/firewallRules.bicep' = {
  scope: resourceGroup(firewallPolicyResourceGroup)
  name: 'fw-rules-${spokeVnetName}'
  params: {
    firewallPolicyName: firewallPolicyName
    sourceAddresses: spokeSourceAddresses
  }
}

output deploymentLocation string = location
output hubPeeringId string = hubPeering.outputs.peeringId
output firewallRuleCollectionGroupId string = fwRules.outputs.ruleCollectionGroupId
