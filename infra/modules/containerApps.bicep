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
var webImage = isBootstrap ? bootstrapImage : '${acrLoginServer}/stocky-web:${imageTag}'
// Server-rendered MVC replacement for the SPA. Deployed alongside the existing
// nginx `web` container app — see docs/non-spa-rearchitecture.md §9 for the
// manual cutover (flip App Gateway backend pool to ca-${prefix}-web-mvc and
// retire the nginx `web` Container App afterwards).
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

resource web 'Microsoft.App/containerApps@2024-10-02-preview' = {
  name: 'ca-${prefix}-web'
  location: location
  tags: union(tags, { 'azd-service-name': 'web' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: envId
    configuration: {
      ingress: {
        external: true
        targetPort: isBootstrap ? 80 : 8080
        transport: 'http'
        allowInsecure: true
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
          name: 'web'
          image: webImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'PORT', value: '8080' }
          ]
        }
      ]
      // Legacy SPA disabled — AGW now points pool-web at the MVC app.
      // ACA disallows maxReplicas=0, so use min=0 and a minimal cap; with no
      // traffic the cluster will scale to zero. Resource kept (not deleted)
      // so the FQDN stays stable in case we ever need to revert.
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

// ----------------------------------------------------------------------------
// Stocky.Web.Mvc — server-rendered MVC app that delegates to Api controllers
// in-process. Shares the same SQL identity + connection string as the API so
// EF Core DbContext writes go directly to SQL with no extra hop.
//
// Cutover (manual, per docs/non-spa-rearchitecture.md §9):
//   1) `azd deploy web-mvc` (or push image stocky-web-mvc:<tag>).
//   2) Smoke test https://<webMvcFqdn>/ once the cert chain is correct.
//   3) Update infra/modules/appGateway.bicep backend pool from `web` to
//      `web-mvc`, redeploy `appGateway` module.
//   4) Once verified, remove the `web` (nginx) resource above and the SPA in
//      src/Web/.
//
// TODO: create KV secrets `stocky-google-client-secret` (with the OAuth
//       client secret) before first deploy. Wire it via a `secrets` block +
//       `secretRef` like the api's `sql-cert-base64` if Google sign-in is
//       required in production.
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
output webAppName string = web.name
output apiFqdn string = api.properties.configuration.ingress.fqdn
output webFqdn string = web.properties.configuration.ingress.fqdn
output webMvcAppName string = webMvc.name
output webMvcFqdn string = webMvc.properties.configuration.ingress.fqdn
