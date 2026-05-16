// Hub-side peering: hub VNet -> spoke VNet. Deployed by the platform team in the
// connectivity subscription, in the RG that holds the hub VNet.
@description('Hub VNet name (existing).')
param hubVnetName string

@description('Peering name on the hub side (default: peer-to-<spokeVnetName>).')
param peeringName string

@description('Spoke VNet resource id (full cross-sub id).')
param spokeVnetResourceId string

@description('Allow traffic from spoke to be forwarded through the hub.')
param allowForwardedTraffic bool = true

@description('Allow spoke to use the hub VNet gateway (ExpressRoute / VPN).')
param allowGatewayTransit bool = false

resource hubToSpoke 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings@2024-01-01' = {
  name: '${hubVnetName}/${peeringName}'
  properties: {
    allowVirtualNetworkAccess: true
    allowForwardedTraffic: allowForwardedTraffic
    allowGatewayTransit: allowGatewayTransit
    useRemoteGateways: false
    remoteVirtualNetwork: { id: spokeVnetResourceId }
  }
}

output peeringId string = hubToSpoke.id
