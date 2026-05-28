targetScope = 'resourceGroup'

@minLength(1)
@description('Environment name used for resource naming (e.g., dev, prod).')
param environmentName string

@minLength(1)
@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Tags applied to all resources.')
param tags object = {
  'azd-env-name': environmentName
  app: 'stocky'
}

@secure()
@description('Pre-shared key used by the MCP server when calling the Stocky API. Set once via: azd env set MCP_SERVICE_KEY <value>')
param mcpServiceKey string = ''

@description('User id (sub claim) the MCP server impersonates when calling the API. Set via: azd env set MCP_OWNER_ID <sub>')
param mcpOwnerId string = ''

@description('Object id of the Entra group/user that will be SQL Entra admin (break-glass).')
param sqlEntraAdminObjectId string

@description('Display name of the Entra SQL admin principal.')
param sqlEntraAdminLogin string

@description('GitHub repo in owner/repo form for OIDC federated credentials.')
param githubRepo string = 'OdayAlUlabi/Stocky'

@description('GitHub OIDC subjects to federate. Default: main + prod environment.')
param githubSubjects array = [
  'repo:OdayAlUlabi/Stocky:ref:refs/heads/main'
  'repo:OdayAlUlabi/Stocky:environment:prod'
]

// ---------- ALZ / Hub-and-Spoke parameters ----------
// All optional. Empty values keep the deployment self-contained for dev sandboxes.
// Populate in prod/parameters file to connect into the platform landing zone.
@description('Spoke VNet address space. Must not overlap the hub. Coordinate with platform team.')
param vnetAddressSpace string = '10.40.0.0/20'

@description('Resource id of the hub VNet in the connectivity subscription. When non-empty, spoke peers to hub.')
param hubVnetResourceId string = ''

@description('Private IP of the hub Azure Firewall. When non-empty, ACA + PE subnets force 0.0.0.0/0 through it (ALZ forced tunneling).')
param hubFirewallPrivateIp string = ''

@description('Use hub VNet remote gateway (ExpressRoute / VPN) for on-prem connectivity.')
param useHubRemoteGateways bool = false

@description('Resource id of the RG holding central Private DNS zones (in connectivity sub). When non-empty, zones are not created locally.')
param centralDnsZonesRgId string = ''

// ---------- Self-hosted GitHub runner parameters ----------
@description('Deploy an in-VNet self-hosted GitHub Actions runner (Ubuntu VM). Strongly recommended in ALZ mode so CI can reach private endpoints.')
param deployGitHubRunner bool = true

@description('GitHub owner (org or user) the runner registers to.')
param githubOwner string = 'OdayAlUlabi'

@description('GitHub repository name. Leave empty to register at org scope.')
param githubRunnerRepo string = 'Stocky'

@description('GitHub App id for runner registration tokens. Leave empty to use PAT mode.')
param githubAppId string = ''

@description('GitHub App installation id. Leave empty to use PAT mode.')
param githubInstallationId string = ''

@description('Key Vault secret name that holds the GitHub App PEM private key. Populated out-of-band before first runner execution.')
param githubAppPrivateKeySecretName string = 'github-app-private-key'

@description('Self-hosted runner labels (comma-separated) appended to "self-hosted,linux".')
param githubRunnerLabels string = 'stocky'

@description('VM SKU for the runner. Standard_B2s is sufficient for most CI workloads.')
param githubRunnerVmSize string = 'Standard_B2s'

@description('Image tag to deploy. Use "placeholder" (default) for first-time infrastructure bootstrap. Set to a real ACR tag (e.g. "latest") to deploy app images and configure ACR pull credentials.')
param imageTag string = 'placeholder'

@description('Key Vault secret URI of the App Gateway TLS certificate. When set, enables HTTPS (443) listener and HTTP→HTTPS redirect. Format: https://kv-xxx.vault.azure.net/secrets/cert-name — use the create-tls-cert.ps1 script to generate one.')
param tlsCertSecretUri string = ''

var resourceToken = uniqueString(subscription().id, resourceGroup().id, environmentName)
var prefix = 'stocky-${environmentName}'
var alzConnected = !empty(hubVnetResourceId)
var canDeployRunner = deployGitHubRunner && (!empty(githubAppId) && !empty(githubInstallationId) || true) // PAT mode uses 'github-runner-pat' KV secret when App creds are empty

// ---------- Observability ----------
module obs 'modules/observability.bicep' = {
  name: 'obs'
  params: {
    prefix: prefix
    location: location
    tags: tags
  }
}

// ---------- Network + Private DNS ----------
module net 'modules/network.bicep' = {
  name: 'net'
  params: {
    prefix: prefix
    location: location
    tags: tags
    vnetAddressSpace: vnetAddressSpace
    hubFirewallPrivateIp: hubFirewallPrivateIp
  }
}

module dns 'modules/privateDns.bicep' = {
  name: 'dns'
  params: {
    vnetId: net.outputs.vnetId
    tags: tags
    acaRegion: location
    centralDnsZonesRgId: centralDnsZonesRgId
  }
}

// ---------- ALZ hub peering (only when hub VNet supplied) ----------
module hub 'modules/hubSpoke.bicep' = if (alzConnected) {
  name: 'hub'
  params: {
    spokeVnetName: net.outputs.vnetName
    hubVnetResourceId: hubVnetResourceId
    allowForwardedTraffic: true
    useRemoteGateways: useHubRemoteGateways
  }
}

// ---------- Identities ----------
module ids 'modules/identities.bicep' = {
  name: 'ids'
  params: {
    prefix: prefix
    location: location
    tags: tags
    githubRepo: githubRepo
    githubSubjects: githubSubjects
  }
}

// ---------- Data plane ----------
module kv 'modules/keyvault.bicep' = {
  name: 'kv'
  params: {
    prefix: prefix
    location: location
    tags: tags
    resourceToken: resourceToken
    peSubnetId: net.outputs.peSubnetId
    dnsZoneId: dns.outputs.kvZoneId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    prefix: prefix
    location: location
    tags: tags
    resourceToken: resourceToken
    peSubnetId: net.outputs.peSubnetId
    dnsZoneId: dns.outputs.sqlZoneId
    sqlEntraAdminObjectId: sqlEntraAdminObjectId
    sqlEntraAdminLogin: sqlEntraAdminLogin
  }
}

module acr 'modules/acr.bicep' = {
  name: 'acr'
  params: {
    prefix: prefix
    location: location
    tags: tags
    resourceToken: resourceToken
    peSubnetId: net.outputs.peSubnetId
    dnsZoneId: dns.outputs.acrZoneId
  }
}

// ---------- RBAC (must precede containerApps so ACR pull works) ----------
module rbac 'modules/rbac.bicep' = {
  name: 'rbac'
  params: {
    prefix: prefix
    kvId: kv.outputs.kvId
    acrId: acr.outputs.acrId
    apiPrincipalId: ids.outputs.apiIdPrincipalId
    cicdPrincipalId: ids.outputs.cicdIdPrincipalId
    migratorPrincipalId: ids.outputs.migratorIdPrincipalId
    runnerPrincipalId: ids.outputs.runnerIdPrincipalId
    agwPrincipalId: ids.outputs.agwIdPrincipalId
    mcpPrincipalId: ids.outputs.mcpIdPrincipalId
  }
}

// ---------- Container Apps env + workloads ----------
module env 'modules/containerEnv.bicep' = {
  name: 'env'
  params: {
    prefix: prefix
    location: location
    tags: tags
    infrastructureSubnetId: net.outputs.acaSubnetId
    lawCustomerId: obs.outputs.lawCustomerId
    lawPrimaryKey: obs.outputs.lawPrimaryKey
  }
}

// Private DNS zone for the internal ACA env so App Gateway (and anything
// else in the VNet) can resolve {app}.internal.{defaultDomain}.
module acaDns 'modules/acaInternalDns.bicep' = {
  name: 'acaDns'
  params: {
    defaultDomain: env.outputs.envDefaultDomain
    staticIp: env.outputs.envStaticIp
    tags: tags
  }
}

module apps 'modules/containerApps.bicep' = {
  name: 'apps'
  params: {
    prefix: prefix
    location: location
    tags: tags
    envId: env.outputs.envId
    acrLoginServer: acr.outputs.acrLoginServer
    apiIdentityId: ids.outputs.apiIdId
    apiIdentityClientId: ids.outputs.apiIdClientId
    apiSqlIdentityId: ids.outputs.apiSqlIdId
    apiSqlIdentityClientId: ids.outputs.apiSqlIdClientId
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDbName: sql.outputs.dbName
    appiConnectionString: obs.outputs.appiConnectionString
    publicHostname: 'stocky.${location}.cloudapp.azure.com'
    sqlSpClientId: 'c5a4c107-a912-4d72-b513-513ced854386'
    kvUri: kv.outputs.kvUri
    mcpIdentityId: ids.outputs.mcpIdId
    mcpOwnerId: mcpOwnerId
    imageTag: imageTag
  }
  dependsOn: [ rbac ]
}

module jobs 'modules/containerJobs.bicep' = {
  name: 'jobs'
  params: {
    prefix: prefix
    location: location
    tags: tags
    envId: env.outputs.envId
    acrLoginServer: acr.outputs.acrLoginServer
    migratorIdentityId: ids.outputs.migratorIdId
    migratorIdentityClientId: ids.outputs.migratorIdClientId
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDbName: sql.outputs.dbName
    imageTag: imageTag
  }
  dependsOn: [ rbac ]
}

// ---------- Self-hosted GitHub Actions runner (VM, in-spoke, ALZ-friendly) ----------
module runner 'modules/githubRunnerVM.bicep' = if (canDeployRunner) {
  name: 'runner'
  params: {
    prefix: prefix
    location: location
    tags: tags
    subnetId: net.outputs.runnerSubnetId
    runnerIdentityId: ids.outputs.runnerIdId
    keyVaultUri: kv.outputs.kvUri
    githubOwner: githubOwner
    githubRepo: githubRunnerRepo
    githubAppId: githubAppId
    githubInstallationId: githubInstallationId
    githubAppPrivateKeySecretName: githubAppPrivateKeySecretName
    runnerLabels: githubRunnerLabels
    vmSize: githubRunnerVmSize
  }
  dependsOn: [ rbac ]
}

// ---------- App Gateway ----------
module appgw 'modules/appGateway.bicep' = {
  name: 'appgw'
  params: {
    prefix: prefix
    location: location
    tags: tags
    subnetId: net.outputs.appGwSubnetId
    apiBackendFqdn: apps.outputs.apiFqdn
    webBackendFqdn: apps.outputs.webMvcFqdn
    agwIdentityId: ids.outputs.agwIdId
    tlsCertSecretUri: tlsCertSecretUri
    lawId: obs.outputs.lawId
  }
}

// MCP service key stored in Key Vault (ARM management-plane write works even
// when publicNetworkAccess is Disabled, because AzureServices bypass is set).
// Bicep creates/updates it on every 'azd up'; skipped when mcpServiceKey is empty.
resource kvRef 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: take('kv-${prefix}-${resourceToken}', 24)
}

resource mcpServiceKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(mcpServiceKey)) {
  parent: kvRef
  name: 'mcp-service-key'
  properties: {
    value: mcpServiceKey
    attributes: { enabled: true }
  }
}

// ---------- Outputs ----------
output GH_RUNNER_DEPLOYED bool = canDeployRunner
output GH_RUNNER_VM_NAME string = canDeployRunner ? runner!.outputs.runnerVmName : ''
output GH_RUNNER_LABELS string = canDeployRunner ? runner!.outputs.runnerLabelsOut : ''
output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = resourceGroup().name
output PUBLIC_IP string = appgw.outputs.appGwPublicIp
output PUBLIC_FQDN string = appgw.outputs.appGwFqdn
output API_INTERNAL_FQDN string = apps.outputs.apiFqdn
output API_IDENTITY_CLIENT_ID string = ids.outputs.apiIdClientId
output CICD_CLIENT_ID string = ids.outputs.cicdIdClientId
output CICD_TENANT_ID string = subscription().tenantId
output CICD_SUBSCRIPTION_ID string = subscription().subscriptionId
output ACR_LOGIN_SERVER string = acr.outputs.acrLoginServer
output ACR_NAME string = acr.outputs.acrName
output ACA_ENV_NAME string = env.outputs.envName
output API_APP_NAME string = apps.outputs.apiAppName
output WEB_MVC_APP_NAME string = apps.outputs.webMvcAppName
output WEB_MVC_INTERNAL_FQDN string = apps.outputs.webMvcFqdn
output MCP_APP_NAME string = apps.outputs.mcpAppName
output MCP_FQDN string = apps.outputs.mcpFqdn
output MIGRATOR_JOB_NAME string = jobs.outputs.jobName
output SQL_SERVER_FQDN string = sql.outputs.serverFqdn
output SQL_DB_NAME string = sql.outputs.dbName
output KEYVAULT_NAME string = kv.outputs.kvName
output APP_INSIGHTS_CONNECTION_STRING string = obs.outputs.appiConnectionString
output ALZ_CONNECTED bool = alzConnected
output DNS_MODE string = dns.outputs.dnsMode
output SPOKE_VNET_ID string = net.outputs.vnetId
output SPOKE_VNET_NAME string = net.outputs.vnetName
