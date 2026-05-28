// Application Gateway v2 with WAF_v2 (OWASP 3.2) as the only public ingress.
// Backend = ACA env internal FQDN, resolved via the env's private DNS zone link.
@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object
@description('App Gateway subnet id.')
param subnetId string
@description('API container app FQDN (private, inside acaEnvDefaultDomain).')
param apiBackendFqdn string
@description('Web container app FQDN (private).')
param webBackendFqdn string
@description('MCP container app FQDN (external ACA ingress).')
param mcpBackendFqdn string
@description('User-assigned managed identity resource ID for App Gateway to pull TLS cert from Key Vault.')
param agwIdentityId string
@description('Key Vault secret URI of the TLS certificate. When non-empty, enables the HTTPS (443) listener and redirects HTTP→HTTPS. Format: https://kv-xxx.vault.azure.net/secrets/cert-name (omit version for always-latest).')
param tlsCertSecretUri string = ''
@description('Log Analytics workspace resource ID for AGW diagnostic settings (access + firewall logs).')
param lawId string

resource pip 'Microsoft.Network/publicIPAddresses@2024-01-01' = {
  name: 'pip-${prefix}-agw'
  location: location
  tags: tags
  sku: { name: 'Standard', tier: 'Regional' }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
    dnsSettings: {
      domainNameLabel: 'stocky'
    }
  }
}

resource wafPolicy 'Microsoft.Network/ApplicationGatewayWebApplicationFirewallPolicies@2024-01-01' = {
  name: 'waf-${prefix}'
  location: location
  tags: tags
  properties: {
    policySettings: {
      state: 'Enabled'
      mode: 'Prevention'
      requestBodyCheck: true
      maxRequestBodySizeInKb: 128
      fileUploadLimitInMb: 100
    }
    managedRules: {
      managedRuleSets: [
        {
          ruleSetType: 'OWASP'
          ruleSetVersion: '3.2'
        }
      ]
      // JWT Bearer tokens contain base64url characters and sub-strings that
      // routinely trigger OWASP SQL-injection (942xxx) and XSS (941xxx) rules.
      // Exclude the Authorization header from those rule groups so that
      // authenticated API calls (POST/PUT/DELETE) are not incorrectly blocked.
      exclusions: [
        {
          matchVariable: 'RequestHeaderNames'
          selectorMatchOperator: 'Equals'
          selector: 'authorization'
          exclusionManagedRuleSets: [
            {
              ruleSetType: 'OWASP'
              ruleSetVersion: '3.2'
              ruleGroups: [
                { ruleGroupName: 'REQUEST-942-APPLICATION-ATTACK-SQLI', rules: [] }
                { ruleGroupName: 'REQUEST-941-APPLICATION-ATTACK-XSS', rules: [] }
              ]
            }
          ]
        }
        {
          matchVariable: 'RequestHeaderNames'
          selectorMatchOperator: 'Equals'
          selector: 'x-mcp-service-key'
          exclusionManagedRuleSets: [
            {
              ruleSetType: 'OWASP'
              ruleSetVersion: '3.2'
              ruleGroups: [
                { ruleGroupName: 'REQUEST-942-APPLICATION-ATTACK-SQLI', rules: [] }
                { ruleGroupName: 'REQUEST-941-APPLICATION-ATTACK-XSS', rules: [] }
              ]
            }
          ]
        }
      ]
    }
  }
}

// Custom private DNS zone for the ACA env is created in privateDns.bicep.
// App Gateway resolves the env FQDN through Azure DNS which will hit that zone.
var agwName = 'agw-${prefix}'
var httpsEnabled = !empty(tlsCertSecretUri)
// Public FQDN is deterministic: <label>.<location>.cloudapp.azure.com
var publicFqdn = 'stocky.${location}.cloudapp.azure.com'

resource appgw 'Microsoft.Network/applicationGateways@2024-01-01' = {
  name: agwName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${agwIdentityId}': {}
    }
  }
  properties: {
    sku: {
      name: 'WAF_v2'
      tier: 'WAF_v2'
    }
    autoscaleConfiguration: {
      minCapacity: 1
      maxCapacity: 3
    }
    firewallPolicy: { id: wafPolicy.id }
    gatewayIPConfigurations: [
      {
        name: 'gwip'
        properties: { subnet: { id: subnetId } }
      }
    ]
    frontendIPConfigurations: [
      {
        name: 'feip-public'
        properties: { publicIPAddress: { id: pip.id } }
      }
    ]
    sslCertificates: httpsEnabled ? [
      {
        name: 'tls-cert'
        properties: {
          keyVaultSecretId: tlsCertSecretUri
        }
      }
    ] : []
    frontendPorts: [
      {
        name: 'port-443'
        properties: { port: 443 }
      }
      {
        name: 'port-80'
        properties: { port: 80 }
      }
    ]
    backendAddressPools: [
      {
        name: 'pool-api'
        properties: { backendAddresses: [ { fqdn: apiBackendFqdn } ] }
      }
      {
        name: 'pool-web'
        properties: { backendAddresses: [ { fqdn: webBackendFqdn } ] }
      }
      {
        name: 'pool-mcp'
        properties: { backendAddresses: [ { fqdn: mcpBackendFqdn } ] }
      }
    ]
    backendHttpSettingsCollection: [
      {
        name: 'https-api'
        properties: {
          port: 443
          protocol: 'Https'
          pickHostNameFromBackendAddress: true
          requestTimeout: 60
          probe: { id: resourceId('Microsoft.Network/applicationGateways/probes', agwName, 'probe-api') }
        }
      }
      {
        name: 'https-web'
        properties: {
          // ACA env has allowInsecure=false; talk to the internal ingress on 443 (same as api).
          port: 443
          protocol: 'Https'
          pickHostNameFromBackendAddress: true
          requestTimeout: 60
          probe: { id: resourceId('Microsoft.Network/applicationGateways/probes', agwName, 'probe-web') }
        }
      }
      {
        name: 'https-mcp'
        properties: {
          port: 443
          protocol: 'Https'
          pickHostNameFromBackendAddress: true
          requestTimeout: 60
          probe: { id: resourceId('Microsoft.Network/applicationGateways/probes', agwName, 'probe-mcp') }
        }
      }
    ]
    probes: [
      {
        name: 'probe-api'
        properties: {
          protocol: 'Https'
          path: '/health'
          host: apiBackendFqdn
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: false
          match: { statusCodes: [ '200-399' ] }
        }
      }
      {
        name: 'probe-web'
        properties: {
          protocol: 'Https'
          path: '/'
          host: webBackendFqdn
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: false
          match: { statusCodes: [ '200-399' ] }
        }
      }
      {
        name: 'probe-mcp'
        properties: {
          protocol: 'Https'
          path: '/'
          host: mcpBackendFqdn
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: false
          match: { statusCodes: [ '200-399' ] }
        }
      }
    ]
    httpListeners: concat(
      [
        {
          name: 'listener-http'
          properties: {
            frontendIPConfiguration: { id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', agwName, 'feip-public') }
            frontendPort: { id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', agwName, 'port-80') }
            protocol: 'Http'
          }
        }
      ],
      httpsEnabled ? [
        {
          name: 'listener-https'
          properties: {
            frontendIPConfiguration: { id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', agwName, 'feip-public') }
            frontendPort: { id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', agwName, 'port-443') }
            protocol: 'Https'
            sslCertificate: { id: resourceId('Microsoft.Network/applicationGateways/sslCertificates', agwName, 'tls-cert') }
          }
        }
      ] : []
    )
    redirectConfigurations: httpsEnabled ? [
      {
        name: 'redirect-http-to-https'
        properties: {
          redirectType: 'Permanent'
          targetListener: { id: resourceId('Microsoft.Network/applicationGateways/httpListeners', agwName, 'listener-https') }
          includePath: true
          includeQueryString: true
        }
      }
    ] : []
    // Rewrite ACA-internal Location headers to the public App Gateway FQDN.
    // When ACA's envoy proxy issues a redirect (e.g. HTTP→HTTPS or trailing-slash
    // normalisation) it uses the Host it received (the ACA FQDN) in the Location
    // value. This rule rewrites those to the public URL before the browser sees them.
    rewriteRuleSets: [
      {
        name: 'fix-location'
        properties: {
          rewriteRules: [
            {
              name: 'rewrite-aca-location'
              ruleSequence: 100
              conditions: [
                {
                  variable: 'http_resp_Location'
                  pattern: 'https?://[^/]+\\.azurecontainerapps\\.io(/.*)?'
                  ignoreCase: true
                  negate: false
                }
              ]
              actionSet: {
                responseHeaderConfigurations: [
                  {
                    headerName: 'Location'
                    headerValue: '${httpsEnabled ? 'https' : 'http'}://${publicFqdn}{http_resp_Location_1}'
                  }
                ]
              }
            }
          ]
        }
      }
    ]
    requestRoutingRules: httpsEnabled ? [
      {
        name: 'http-to-https'
        properties: {
          ruleType: 'Basic'
          priority: 50
          httpListener: { id: resourceId('Microsoft.Network/applicationGateways/httpListeners', agwName, 'listener-http') }
          redirectConfiguration: { id: resourceId('Microsoft.Network/applicationGateways/redirectConfigurations', agwName, 'redirect-http-to-https') }
        }
      }
      {
        name: 'https-to-backends'
        properties: {
          ruleType: 'PathBasedRouting'
          priority: 100
          httpListener: { id: resourceId('Microsoft.Network/applicationGateways/httpListeners', agwName, 'listener-https') }
          urlPathMap: { id: resourceId('Microsoft.Network/applicationGateways/urlPathMaps', agwName, 'paths') }
        }
      }
    ] : [
      {
        name: 'http-to-web'
        properties: {
          ruleType: 'PathBasedRouting'
          priority: 100
          httpListener: { id: resourceId('Microsoft.Network/applicationGateways/httpListeners', agwName, 'listener-http') }
          urlPathMap: { id: resourceId('Microsoft.Network/applicationGateways/urlPathMaps', agwName, 'paths') }
        }
      }
    ]
    urlPathMaps: [
      {
        name: 'paths'
        properties: {
          defaultBackendAddressPool: { id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', agwName, 'pool-web') }
          defaultBackendHttpSettings: { id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', agwName, 'https-web') }
          defaultRewriteRuleSet: { id: resourceId('Microsoft.Network/applicationGateways/rewriteRuleSets', agwName, 'fix-location') }
          pathRules: [
            {
              name: 'api'
              properties: {
                paths: [ '/api/*', '/hubs/*', '/health', '/admin/refresh/*' ]
                backendAddressPool: { id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', agwName, 'pool-api') }
                backendHttpSettings: { id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', agwName, 'https-api') }
                rewriteRuleSet: { id: resourceId('Microsoft.Network/applicationGateways/rewriteRuleSets', agwName, 'fix-location') }
              }
            }
            {
              name: 'mcp'
              properties: {
                paths: [ '/mcp', '/mcp/*' ]
                backendAddressPool: { id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', agwName, 'pool-mcp') }
                backendHttpSettings: { id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', agwName, 'https-mcp') }
                rewriteRuleSet: { id: resourceId('Microsoft.Network/applicationGateways/rewriteRuleSets', agwName, 'fix-location') }
              }
            }
            {
              name: 'mcp-oauth'
              properties: {
                paths: [ '/.well-known/oauth-authorization-server', '/authorize', '/token' ]
                backendAddressPool: { id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', agwName, 'pool-mcp') }
                backendHttpSettings: { id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', agwName, 'https-mcp') }
                rewriteRuleSet: { id: resourceId('Microsoft.Network/applicationGateways/rewriteRuleSets', agwName, 'fix-location') }
              }
            }
          ]
        }
      }
    ]
  }
}

resource agwDiagSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'agw-diag'
  scope: appgw
  properties: {
    workspaceId: lawId
    logs: [
      { category: 'ApplicationGatewayAccessLog',   enabled: true }
      { category: 'ApplicationGatewayFirewallLog',  enabled: true }
    ]
  }
}

output appGwPublicIp string = pip.properties.ipAddress
output appGwFqdn string = pip.properties.dnsSettings.fqdn
