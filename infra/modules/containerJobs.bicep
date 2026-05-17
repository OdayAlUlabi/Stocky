// Manual-trigger ACA Job for EF migrations. Identity has SQL db_owner via Entra.
@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object
@description('ACA managed environment id.')
param envId string
@description('ACR login server.')
param acrLoginServer string
@description('Migrator UAMI resource id.')
param migratorIdentityId string
@description('Migrator UAMI client id.')
param migratorIdentityClientId string
@description('SQL server FQDN.')
param sqlServerFqdn string
@description('SQL database name.')
param sqlDbName string
@description('Image tag (sha).')
param imageTag string = 'placeholder'

var isBootstrap = imageTag == 'placeholder'
var bootstrapImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
var migratorImage = isBootstrap ? bootstrapImage : '${acrLoginServer}/stocky-api:${imageTag}'

resource migrator 'Microsoft.App/jobs@2024-10-02-preview' = {
  name: 'caj-${prefix}-migrator'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migratorIdentityId}': {}
    }
  }
  properties: {
    environmentId: envId
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: isBootstrap ? [] : [
        {
          server: acrLoginServer
          identity: migratorIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrator'
          image: migratorImage
          // The migrator runs the same API image with a flag so Program.cs
          // applies migrations and exits. During bootstrap the helloworld
          // image is used; command/args are omitted so its default entrypoint runs.
          command: isBootstrap ? null : [ 'dotnet', 'Stocky.Api.dll' ]
          args: isBootstrap ? null : [ '--migrate-only' ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'AZURE_CLIENT_ID', value: migratorIdentityClientId }
            { name: 'ConnectionStrings__Sql', value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDbName};Authentication=Active Directory Managed Identity;User Id=${migratorIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;' }
          ]
        }
      ]
    }
  }
}

output jobName string = migrator.name
