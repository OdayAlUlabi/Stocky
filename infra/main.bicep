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

@description('Entra ID Tenant Id used by the API for token validation.')
param entraTenantId string

@description('Entra ID API App registration Client Id (audience).')
param entraApiClientId string

@description('SQL admin login (used only at provision time; runtime uses Managed Identity).')
@secure()
param sqlAdminLogin string

@secure()
@description('SQL admin password.')
param sqlAdminPassword string

@description('Object Id of the user/group to grant SQL admin (Entra) at provision time.')
param sqlEntraAdminObjectId string = ''

@description('Display name of the SQL Entra admin principal.')
param sqlEntraAdminLogin string = ''

var resourceToken = uniqueString(subscription().id, resourceGroup().id, environmentName)
var prefix = 'stocky-${environmentName}'

// ------------------ Observability ------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-law-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-appi-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ------------------ Key Vault ------------------
resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
  name: 'kv-${take(replace(resourceToken, '-', ''), 20)}'
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    publicNetworkAccess: 'Enabled'
    softDeleteRetentionInDays: 7
    enabledForTemplateDeployment: true
  }
}

// ------------------ Azure SQL ------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${prefix}-sql-${resourceToken}'
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlEntraAdmin 'Microsoft.Sql/servers/administrators@2023-08-01-preview' = if (!empty(sqlEntraAdminObjectId)) {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: sqlEntraAdminLogin
    sid: sqlEntraAdminObjectId
    tenantId: subscription().tenantId
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'stocky'
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5_1'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
  }
}

// ------------------ Hosting Plan ------------------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${prefix}-plan-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

// ------------------ Managed Identities ------------------
resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-api-id-${resourceToken}'
  location: location
  tags: tags
}

// ------------------ API App Service ------------------
resource apiApp 'Microsoft.Web/sites@2023-12-01' = {
  name: '${prefix}-api-${resourceToken}'
  location: location
  tags: union(tags, { 'azd-service-name': 'api' })
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/health'
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
        { name: 'AzureAd__Instance', value: 'https://login.microsoftonline.com/' }
        { name: 'AzureAd__TenantId', value: entraTenantId }
        { name: 'AzureAd__ClientId', value: entraApiClientId }
        { name: 'AzureAd__Audience', value: 'api://${entraApiClientId}' }
        { name: 'ConnectionStrings__Sql', value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDb.name};Authentication=Active Directory Managed Identity;User Id=${apiIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
        { name: 'AllowedOrigins__0', value: 'https://${webApp.properties.defaultHostName}' }
        { name: 'AZURE_CLIENT_ID', value: apiIdentity.properties.clientId }
        { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
      ]
    }
  }
}

// ------------------ Web App Service (React static) ------------------
resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: '${prefix}-web-${resourceToken}'
  location: location
  tags: union(tags, { 'azd-service-name': 'web' })
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'NODE|22-lts'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appCommandLine: 'pm2 serve /home/site/wwwroot --no-daemon --spa'
      appSettings: [
        { name: 'WEBSITE_NODE_DEFAULT_VERSION', value: '~22' }
        { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'false' }
      ]
    }
  }
}

// ------------------ Role Assignments ------------------
// Key Vault Secrets User for API identity
resource apiKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, apiIdentity.id, 'kv-secrets-user')
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    // Key Vault Secrets User
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-9d6c-5a8c-3c1d8c2d4adb')
  }
}

// ------------------ Outputs ------------------
output AZURE_LOCATION string = location
output API_HOSTNAME string = apiApp.properties.defaultHostName
output WEB_HOSTNAME string = webApp.properties.defaultHostName
output API_IDENTITY_CLIENT_ID string = apiIdentity.properties.clientId
output SQL_SERVER_FQDN string = sqlServer.properties.fullyQualifiedDomainName
output SQL_DB_NAME string = sqlDb.name
output APP_INSIGHTS_CONNECTION_STRING string = appInsights.properties.ConnectionString
output KEYVAULT_NAME string = keyVault.name
