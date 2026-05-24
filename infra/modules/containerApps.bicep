// api + web container apps. Both pull from private ACR via the api UAMI.
@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object
@description('ACA managed environment id.')
param envId string
@description('ACR login server (e.g. stockyacr.azurecr.io).')
param acrLoginServer string
@description('User-assigned identity resource id for ACR pull + workload identity.')
param apiIdentityId string
@description('User-assigned identity client id (for SQL connection string).')
param apiIdentityClientId string
@description('Dedicated SQL service account identity resource id.')
param apiSqlIdentityId string
@description('Dedicated SQL service account identity client id (used for AZURE_CLIENT_ID and SQL connection string).')
param apiSqlIdentityClientId string
@description('SQL server FQDN.')
param sqlServerFqdn string
@description('SQL database name.')
param sqlDbName string
@description('Google OAuth client id for JWT Bearer token validation.')
param googleClientId string
@description('Application Insights connection string.')
param appiConnectionString string
@description('App Gateway public FQDN used for CORS AllowedOrigins.')
param publicHostname string
@description('Entra service principal client id for SQL auth (Path A — no IMDS).')
param sqlSpClientId string
@description('Key Vault URI for fetching the SQL SP certificate.')
param kvUri string
@description('Image tag (sha) — placeholder allowed so CI updates it later.')
param imageTag string = 'placeholder'

// When the image tag is still the placeholder (i.e. before first `azd deploy`
// has pushed real images to the private ACR), use a public MCR sample image
// so the container apps can be created successfully. `azd deploy` will swap
// in the real ACR image on the next revision.
var isBootstrap = imageTag == 'placeholder'
var bootstrapImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
var apiImage = isBootstrap ? bootstrapImage : '${acrLoginServer}/stocky-api:${imageTag}'
// Server-rendered MVC app — replaced the legacy React SPA.
var webMvcImage = isBootstrap ? bootstrapImage : '${acrLoginServer}/stocky-web-mvc:${imageTag}'

resource api 'Microsoft.App/containerApps@2024-10-02-preview' = {
  name: 'ca-${prefix}-api'
  location: location
  tags: union(tags, { 'azd-service-name': 'api' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityId}': {}
      '${apiSqlIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: envId
    configuration: {
      secrets: [
        {
          // ACA platform fetches this from KV via private endpoint — the container
          // never calls IMDS for KV auth. Value is the base64-encoded PFX of the SQL SP cert.
          name: 'sql-cert-base64'
          keyVaultUrl: '${kvUri}secrets/stocky-api-sql-cert'
          identity: apiIdentityId
        }
        {
          name: 'alpaca-api-key-id'
          keyVaultUrl: '${kvUri}secrets/alpaca-api-key-id'
          identity: apiIdentityId
        }
        {
          name: 'alpaca-secret-key'
          keyVaultUrl: '${kvUri}secrets/alpaca-secret-key'
          identity: apiIdentityId
        }
      ]
      ingress: {
        external: true
        targetPort: isBootstrap ? 80 : 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: isBootstrap ? [] : [
        {
          server: acrLoginServer
          identity: apiIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appiConnectionString }
            { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
            { name: 'Google__ClientId', value: googleClientId }
            { name: 'AZURE_CLIENT_ID', value: apiIdentityClientId }
            // Path A: SP cert injected by ACA platform from KV — no IMDS from container.
            { name: 'Sql__CertBase64', secretRef: 'sql-cert-base64' }
            { name: 'Sql__SpClientId', value: sqlSpClientId }
            { name: 'Sql__TenantId', value: tenant().tenantId }
            { name: 'KeyVaultUri', value: kvUri }
            // Dedicated SQL service account — separate from the general workload identity (apiId).
            // Program.cs prefers Sql__ManagedIdentityClientId over AZURE_CLIENT_ID for SQL tokens.
            { name: 'Sql__ManagedIdentityClientId', value: apiSqlIdentityClientId }
            { name: 'ConnectionStrings__Sql', value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDbName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
            { name: 'AllowedOrigins__0', value: 'https://${publicHostname}' }
            // Alpaca market data — credentials injected from KV (see secrets above).
            { name: 'MarketData__Alpaca__ApiKeyId', secretRef: 'alpaca-api-key-id' }
            { name: 'MarketData__Alpaca__ApiSecret', secretRef: 'alpaca-secret-key' }
            // PORT is read by the bootstrap helloworld image; the real ASP.NET
            // image ignores it (it honours ASPNETCORE_URLS instead).
            { name: 'PORT', value: '8080' }
          ]
          probes: isBootstrap ? [] : [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              periodSeconds: 30
              initialDelaySeconds: 15
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 8080 }
              periodSeconds: 10
              initialDelaySeconds: 30
              failureThreshold: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

// ----------------------------------------------------------------------------
// Stocky.Web.Mvc — server-rendered MVC app. Delegates to Api controllers
// in-process so EF Core DbContext writes go directly to SQL with no extra hop.
// ----------------------------------------------------------------------------
resource webMvc 'Microsoft.App/containerApps@2024-10-02-preview' = {
  name: 'ca-${prefix}-web-mvc'
  location: location
  tags: union(tags, { 'azd-service-name': 'web-mvc' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityId}': {}
      '${apiSqlIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: envId
    configuration: {
      secrets: [
        {
          name: 'sql-cert-base64'
          keyVaultUrl: '${kvUri}secrets/stocky-api-sql-cert'
          identity: apiIdentityId
        }
        {
          name: 'alpaca-api-key-id'
          keyVaultUrl: '${kvUri}secrets/alpaca-api-key-id'
          identity: apiIdentityId
        }
        {
          name: 'alpaca-secret-key'
          keyVaultUrl: '${kvUri}secrets/alpaca-secret-key'
          identity: apiIdentityId
        }
      ]
      ingress: {
        external: true
        targetPort: isBootstrap ? 80 : 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: isBootstrap ? [] : [
        {
          server: acrLoginServer
          identity: apiIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'web-mvc'
          image: webMvcImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appiConnectionString }
            { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
            { name: 'Authentication__Google__ClientId', value: googleClientId }
            // TODO: add Authentication__Google__ClientSecret via KV secretRef once
            //       stocky-google-client-secret KV secret is created.
            { name: 'AZURE_CLIENT_ID', value: apiIdentityClientId }
            { name: 'Sql__CertBase64', secretRef: 'sql-cert-base64' }
            { name: 'Sql__SpClientId', value: sqlSpClientId }
            { name: 'Sql__TenantId', value: tenant().tenantId }
            { name: 'KeyVaultUri', value: kvUri }
            { name: 'Sql__ManagedIdentityClientId', value: apiSqlIdentityClientId }
            { name: 'ConnectionStrings__Sql', value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDbName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
            { name: 'AllowedOrigins__0', value: 'https://${publicHostname}' }
            { name: 'MarketData__Alpaca__ApiKeyId', secretRef: 'alpaca-api-key-id' }
            { name: 'MarketData__Alpaca__ApiSecret', secretRef: 'alpaca-secret-key' }
            { name: 'PORT', value: '8080' }
          ]
          probes: isBootstrap ? [] : [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              periodSeconds: 30
              initialDelaySeconds: 15
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 8080 }
              periodSeconds: 10
              initialDelaySeconds: 30
              failureThreshold: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

output apiAppName string = api.name
output apiFqdn string = api.properties.configuration.ingress.fqdn
output webMvcAppName string = webMvc.name
output webMvcFqdn string = webMvc.properties.configuration.ingress.fqdn
