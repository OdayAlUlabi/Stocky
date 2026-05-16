// Azure Firewall Policy rule collection group for Stocky workload egress.
// Attached to the existing hub Firewall Policy. Allows the minimal set of
// FQDNs and network destinations the ACA workload needs when forced-tunneling
// 0.0.0.0/0 through the hub Azure Firewall.
//
// All Azure-internal SQL / Key Vault / ACR / Storage traffic stays on the spoke
// via Private Endpoints, so those FQDNs are intentionally NOT in this allowlist.
@description('Existing hub Azure Firewall Policy name.')
param firewallPolicyName string

@description('Rule collection group name.')
param ruleCollectionGroupName string = 'rcg-stocky-egress'

@description('Priority for the rule collection group (lower = higher priority).')
param priority int = 500

@description('Source CIDRs allowed to use these rules (spoke ACA + PE subnets).')
param sourceAddresses array

resource policy 'Microsoft.Network/firewallPolicies@2024-01-01' existing = {
  name: firewallPolicyName
}

resource rcg 'Microsoft.Network/firewallPolicies/ruleCollectionGroups@2024-01-01' = {
  parent: policy
  name: ruleCollectionGroupName
  properties: {
    priority: priority
    ruleCollections: [
      {
        ruleCollectionType: 'FirewallPolicyFilterRuleCollection'
        name: 'arc-stocky-allow-fqdns'
        priority: 500
        action: { type: 'Allow' }
        rules: [
          {
            ruleType: 'ApplicationRule'
            name: 'allow-mcr-and-docker'
            sourceAddresses: sourceAddresses
            protocols: [ { protocolType: 'Https', port: 443 } ]
            targetFqdns: [
              'mcr.microsoft.com'
              '*.data.mcr.microsoft.com'
              '*.cdn.mscr.io'
            ]
          }
          {
            ruleType: 'ApplicationRule'
            name: 'allow-aad-and-arm'
            sourceAddresses: sourceAddresses
            protocols: [ { protocolType: 'Https', port: 443 } ]
            targetFqdns: [
              #disable-next-line no-hardcoded-env-urls
              'login.microsoftonline.com'
              'login.microsoft.com'
              'login.windows.net'
              'graph.microsoft.com'
              #disable-next-line no-hardcoded-env-urls
              'management.azure.com'
            ]
          }
          {
            ruleType: 'ApplicationRule'
            name: 'allow-azure-monitor'
            sourceAddresses: sourceAddresses
            protocols: [ { protocolType: 'Https', port: 443 } ]
            targetFqdns: [
              '*.in.applicationinsights.azure.com'
              '*.livediagnostics.monitor.azure.com'
              '*.monitor.azure.com'
              'dc.applicationinsights.azure.com'
              'dc.services.visualstudio.com'
              '*.ods.opinsights.azure.com'
              '*.oms.opinsights.azure.com'
              '*.agentsvc.azure-automation.net'
            ]
          }
          {
            ruleType: 'ApplicationRule'
            name: 'allow-aca-control-plane'
            sourceAddresses: sourceAddresses
            protocols: [ { protocolType: 'Https', port: 443 } ]
            targetFqdns: [
              'mcr.microsoft.com'
              '*.azurecontainerapps.io'
              '*.azurefd.net'
              '*.blob.${environment().suffixes.storage}'
            ]
          }
          {
            ruleType: 'ApplicationRule'
            name: 'allow-nuget-and-certs'
            sourceAddresses: sourceAddresses
            protocols: [ { protocolType: 'Https', port: 443 }, { protocolType: 'Http', port: 80 } ]
            targetFqdns: [
              'crl.microsoft.com'
              'crl3.digicert.com'
              'crl4.digicert.com'
              'ocsp.digicert.com'
              'ctldl.windowsupdate.com'
            ]
          }
          {
            ruleType: 'ApplicationRule'
            name: 'allow-github-runner'
            sourceAddresses: sourceAddresses
            protocols: [ { protocolType: 'Https', port: 443 } ]
            targetFqdns: [
              'api.github.com'
              'github.com'
              '*.actions.githubusercontent.com'
              'codeload.github.com'
              'objects.githubusercontent.com'
              'objects-origin.githubusercontent.com'
              'release-assets.githubusercontent.com'
              #disable-next-line no-hardcoded-env-urls
              '*.blob.core.windows.net'
              'ghcr.io'
              'pkg-containers.githubusercontent.com'
              'raw.githubusercontent.com'
            ]
          }
        ]
      }
      {
        ruleCollectionType: 'FirewallPolicyFilterRuleCollection'
        name: 'nrc-stocky-allow-ntp'
        priority: 510
        action: { type: 'Allow' }
        rules: [
          {
            ruleType: 'NetworkRule'
            name: 'allow-ntp'
            sourceAddresses: sourceAddresses
            destinationAddresses: [ '*' ]
            destinationPorts: [ '123' ]
            ipProtocols: [ 'UDP' ]
          }
        ]
      }
    ]
  }
}

output ruleCollectionGroupId string = rcg.id
