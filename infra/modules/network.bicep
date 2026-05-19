// stocky network: single VNet, two delegated subnets.
// 10.40.0.0/20 keeps room for future growth (bastion, self-hosted runners).
@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags applied to all resources.')
param tags object

@description('Spoke VNet address space (CIDR). Coordinate with the platform team to avoid hub overlap.')
param vnetAddressSpace string = '10.40.0.0/20'

@description('Private IP of the hub Azure Firewall. When non-empty, a route table forces 0.0.0.0/0 through it on workload subnets (ALZ forced tunneling).')
param hubFirewallPrivateIp string = ''

var addressSpace = vnetAddressSpace
var acaSubnetCidr    = cidrSubnet(addressSpace, 23, 0)  // /23 ACA infrastructure subnet (min /23)
var peSubnetCidr    = cidrSubnet(addressSpace, 27, 16)  // /27 private endpoints (offset 16x /27 = /23 boundary)
var appGwSubnetCidr = cidrSubnet(addressSpace, 27, 17)  // /27 App Gateway
var runnerSubnetCidr = cidrSubnet(addressSpace, 27, 18) // /27 self-hosted runner VM

var forceTunnel = !empty(hubFirewallPrivateIp)

resource routeTable 'Microsoft.Network/routeTables@2024-01-01' = if (forceTunnel) {
  name: 'rt-${prefix}-egress'
  location: location
  tags: tags
  properties: {
    disableBgpRoutePropagation: true
    routes: [
      {
        name: 'default-to-hub-firewall'
        properties: {
          addressPrefix: '0.0.0.0/0'
          nextHopType: 'VirtualAppliance'
          nextHopIpAddress: hubFirewallPrivateIp
        }
      }
    ]
  }
}

resource nsgAca 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: 'nsg-${prefix}-aca'
  location: location
  tags: tags
  // ACA managed environments manage their own subnet-level traffic; the NSG
  // exists only to satisfy the org policy that mandates an NSG on every subnet.
  // Default rules (allow VNet/AzureLoadBalancer inbound, allow all outbound)
  // are sufficient; no custom rules are added so ACA can function unimpeded.
  properties: {
    securityRules: []
  }
}

resource nsgPe 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: 'nsg-${prefix}-pe'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'DenyInboundFromInternet'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

resource nsgRunner 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: 'nsg-${prefix}-runner'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'DenyInboundFromInternet'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

// NAT Gateway for runner VM outbound internet (apt, Docker, GitHub API, runner binary).
// In ALZ mode the 0.0.0.0/0 UDR in routeTable takes precedence, so the NAT GW is
// bypassed and traffic goes via the hub firewall instead — having both is safe.
resource natGwPip 'Microsoft.Network/publicIPAddresses@2024-01-01' = {
  name: 'pip-${prefix}-runner-nat'
  location: location
  tags: tags
  sku: { name: 'Standard' }
  properties: { publicIPAllocationMethod: 'Static' }
}

resource natGw 'Microsoft.Network/natGateways@2024-01-01' = {
  name: 'ngw-${prefix}-runner'
  location: location
  tags: tags
  sku: { name: 'Standard' }
  properties: {
    idleTimeoutInMinutes: 4
    publicIpAddresses: [ { id: natGwPip.id } ]
  }
}

resource nsgAppGw 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: 'nsg-${prefix}-agw'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'AllowGatewayManager'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '65200-65535'
          sourceAddressPrefix: 'GatewayManager'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowAzureLoadBalancer'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'AzureLoadBalancer'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowHttpsInbound'
        properties: {
          priority: 200
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowHttpInbound'
        properties: {
          priority: 210
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '80'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: 'vnet-${prefix}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [ addressSpace ] }
    subnets: [
      {
        name: 'snet-aca-infra'
        properties: {
          addressPrefix: acaSubnetCidr
          // ACA workload profile envs require delegation to
          // Microsoft.App/environments via the infrastructure subnet.
          delegations: [
            {
              name: 'aca-delegation'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
          networkSecurityGroup: { id: nsgAca.id }
          privateEndpointNetworkPolicies: 'Disabled'
          routeTable: forceTunnel ? { id: routeTable.id } : null
        }
      }
      {
        name: 'snet-pe'
        properties: {
          addressPrefix: peSubnetCidr
          networkSecurityGroup: { id: nsgPe.id }
          privateEndpointNetworkPolicies: 'Disabled'
          routeTable: forceTunnel ? { id: routeTable.id } : null
        }
      }
      {
        name: 'snet-appgw'
        properties: {
          addressPrefix: appGwSubnetCidr
          networkSecurityGroup: { id: nsgAppGw.id }
        }
      }
      {
        name: 'snet-runner'
        properties: {
          addressPrefix: runnerSubnetCidr
          networkSecurityGroup: { id: nsgRunner.id }
          natGateway: { id: natGw.id }
          privateEndpointNetworkPolicies: 'Disabled'
          routeTable: forceTunnel ? { id: routeTable.id } : null
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output acaSubnetId string = '${vnet.id}/subnets/snet-aca-infra'
output peSubnetId string = '${vnet.id}/subnets/snet-pe'
output appGwSubnetId string = '${vnet.id}/subnets/snet-appgw'
output runnerSubnetId string = '${vnet.id}/subnets/snet-runner'
