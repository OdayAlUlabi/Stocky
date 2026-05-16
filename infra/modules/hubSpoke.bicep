// ALZ hub-and-spoke wiring for the Stocky application landing zone.
// Creates spoke -> hub VNet peering. The reverse (hub -> spoke) peering must be
// created in the connectivity subscription by the platform team, since this
// deployment does not have rights on the hub VNet.
@description('Spoke (workload) VNet name in this resource group.')
param spokeVnetName string

@description('Resource id of the hub VNet (in the connectivity subscription).')
param hubVnetResourceId string

@description('Allow traffic from hub VNet to be forwarded through the spoke (default false).')
param allowForwardedTraffic bool = true

@description('Use the hub VNet remote gateway (ExpressRoute / VPN). Requires gateway in hub.')
param useRemoteGateways bool = false

resource spokeToHub 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings@2024-01-01' = {
  name: '${spokeVnetName}/peer-to-hub'
  properties: {
    allowVirtualNetworkAccess: true
    allowForwardedTraffic: allowForwardedTraffic
    allowGatewayTransit: false
    useRemoteGateways: useRemoteGateways
    remoteVirtualNetwork: { id: hubVnetResourceId }
  }
}

output peeringId string = spokeToHub.id
