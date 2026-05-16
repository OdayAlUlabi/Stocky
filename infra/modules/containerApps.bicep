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
@description('SQL server FQDN.')
param sqlServerFqdn string
@description('SQL database name.')
param sqlDbName string
@description('Entra tenant id.')
param entraTenantId string
@description('Entra API client id (audience).')
param entraApiClientId string
@description('Application Insights connection string.')
param appiConnectionString string
@description('App Gateway public FQDN used for CORS AllowedOrigins.')
param publicHostname string
@description('Image tag (sha) — placeholder allowed so CI updates it later.')
param imageTag string = 'placeholder'

var apiImage = '${acrLoginServer}/stocky-api:${imageTag}'
var webImage = '${acrLoginServer}/stocky-web:${imageTag}'

resource api 'Microsoft.App/containerApps@2024-10-02-preview' = {
  name: 'ca-${prefix}-api'
  location: location
  tags: union(tags, { 'azd-service-name': 'api' })
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
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
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
            #disable-next-line no-hardcoded-env-urls
            { name: 'AzureAd__Instance', value: 'https://login.microsoftonline.com/' }
            { name: 'AzureAd__TenantId', value: entraTenantId }
            { name: 'AzureAd__ClientId', value: entraApiClientId }
            { name: 'AzureAd__Audience', value: 'api://${entraApiClientId}' }
            { name: 'AZURE_CLIENT_ID', value: apiIdentityClientId }
            { name: 'ConnectionStrings__Sql', value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDbName};Authentication=Active Directory Managed Identity;User Id=${apiIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
            { name: 'AllowedOrigins__0', value: 'https://${publicHostname}' }
          ]
          probes: [
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
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
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
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

output apiAppName string = api.name
output webAppName string = web.name
output apiFqdn string = api.properties.configuration.ingress.fqdn
output webFqdn string = web.properties.configuration.ingress.fqdn
