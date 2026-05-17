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

@description('Entra ID tenant id used by the API.')
param entraTenantId string

@description('Entra API app registration client id (audience).')
param entraApiClientId string

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
@description('Deploy an in-VNet self-hosted GitHub Actions runner (KEDA ACA Job). Strongly recommended in ALZ mode so CI can reach private endpoints.')
param deployGitHubRunner bool = true

@description('GitHub owner (org or user) the runner registers to.')
param githubOwner string = 'OdayAlUlabi'

@description('GitHub repository name. Leave empty to register at org scope.')
param githubRunnerRepo string = 'Stocky'

@description('GitHub App id for runner registration tokens. Required when deployGitHubRunner=true.')
param githubAppId string = ''

@description('GitHub App installation id. Required when deployGitHubRunner=true.')
param githubInstallationId string = ''

@description('Key Vault secret name that holds the GitHub App PEM private key. Populated out-of-band before first runner execution.')
param githubAppPrivateKeySecretName string = 'github-app-private-key'

@description('Self-hosted runner labels (comma-separated) appended to "self-hosted, linux".')
param githubRunnerLabels string = 'stocky'

@description('Max parallel self-hosted runner replicas.')
@minValue(1)
@maxValue(50)
param githubRunnerMaxReplicas int = 5

var resourceToken = uniqueString(subscription().id, resourceGroup().id, environmentName)
var prefix = 'stocky-${environmentName}'
var alzConnected = !empty(hubVnetResourceId)
var canDeployRunner = deployGitHubRunner && !empty(githubAppId) && !empty(githubInstallationId)

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
    vnetId: net.outputs.vnetId
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
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDbName: sql.outputs.dbName
    entraTenantId: entraTenantId
    entraApiClientId: entraApiClientId
    appiConnectionString: obs.outputs.appiConnectionString
    publicHostname: 'placeholder.local' // CI updates AllowedOrigins to the AppGw FQDN after first deploy
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
  }
  dependsOn: [ rbac ]
}

// ---------- Self-hosted GitHub Actions runner (in-spoke, ALZ-friendly) ----------
module runner 'modules/githubRunner.bicep' = if (canDeployRunner) {
  name: 'runner'
  params: {
    prefix: prefix
    location: location
    tags: tags
    envId: env.outputs.envId
    acrLoginServer: acr.outputs.acrLoginServer
    runnerIdentityId: ids.outputs.runnerIdId
    runnerIdentityClientId: ids.outputs.runnerIdClientId
    keyVaultUri: kv.outputs.kvUri
    githubOwner: githubOwner
    githubRepo: githubRunnerRepo
    githubAppId: githubAppId
    githubInstallationId: githubInstallationId
    githubAppPrivateKeySecretName: githubAppPrivateKeySecretName
    runnerLabels: githubRunnerLabels
    maxReplicas: githubRunnerMaxReplicas
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
    webBackendFqdn: apps.outputs.webFqdn
  }
}

// ---------- Outputs ----------
output GH_RUNNER_DEPLOYED bool = canDeployRunner
output GH_RUNNER_JOB_NAME string = canDeployRunner ? runner!.outputs.runnerJobName : ''
output GH_RUNNER_LABELS string = canDeployRunner ? runner!.outputs.runnerLabelsOut : ''
output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = resourceGroup().name
output PUBLIC_IP string = appgw.outputs.appGwPublicIp
output API_INTERNAL_FQDN string = apps.outputs.apiFqdn
output WEB_INTERNAL_FQDN string = apps.outputs.webFqdn
output API_IDENTITY_CLIENT_ID string = ids.outputs.apiIdClientId
output CICD_CLIENT_ID string = ids.outputs.cicdIdClientId
output CICD_TENANT_ID string = subscription().tenantId
output CICD_SUBSCRIPTION_ID string = subscription().subscriptionId
output ACR_LOGIN_SERVER string = acr.outputs.acrLoginServer
output ACR_NAME string = acr.outputs.acrName
output ACA_ENV_NAME string = env.outputs.envName
output API_APP_NAME string = apps.outputs.apiAppName
output WEB_APP_NAME string = apps.outputs.webAppName
output MIGRATOR_JOB_NAME string = jobs.outputs.jobName
output SQL_SERVER_FQDN string = sql.outputs.serverFqdn
output SQL_DB_NAME string = sql.outputs.dbName
output KEYVAULT_NAME string = kv.outputs.kvName
output APP_INSIGHTS_CONNECTION_STRING string = obs.outputs.appiConnectionString
output ALZ_CONNECTED bool = alzConnected
output DNS_MODE string = dns.outputs.dnsMode
output SPOKE_VNET_ID string = net.outputs.vnetId
output SPOKE_VNET_NAME string = net.outputs.vnetName
