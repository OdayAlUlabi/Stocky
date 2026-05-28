// Role assignments — least privilege per identity.
@description('Workload prefix (for guid stability).')
param prefix string
@description('Key Vault resource id.')
param kvId string
@description('ACR resource id.')
param acrId string
@description('API workload identity principal id.')
param apiPrincipalId string
@description('CI/CD identity principal id (GitHub OIDC).')
param cicdPrincipalId string
@description('Migrator identity principal id.')
param migratorPrincipalId string
@description('Self-hosted GitHub runner identity principal id.')
param runnerPrincipalId string
@description('App Gateway identity principal id (reads TLS cert from Key Vault).')
param agwPrincipalId string
@description('MCP server identity principal id (ACR pull + KV service key).')
param mcpPrincipalId string

var roleKvSecretsUser   = '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
var roleKvCertUser      = 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba' // Key Vault Certificate User
var roleAcrPull         = '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
var roleAcrPush         = '8311e382-0749-4cb8-b61a-304f252e45ec' // AcrPush

// Custom role: CI/CD can update container apps and start jobs — nothing else on the RG.
resource cicdCustomRoleDef 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(subscription().id, resourceGroup().id, prefix, 'cicd-aca-operator')
  properties: {
    roleName: 'Stocky CICD ACA Operator (${prefix})'
    description: 'Allows CI/CD to update Container Apps revisions, update Jobs, and start Job executions. Scoped to this resource group.'
    type: 'CustomRole'
    assignableScopes: [ resourceGroup().id ]
    permissions: [
      {
        actions: [
          'Microsoft.App/containerApps/read'
          'Microsoft.App/containerApps/write'
          'Microsoft.App/containerApps/listSecrets/action'
          'Microsoft.App/containerApps/revisions/read'
          'Microsoft.App/containerApps/revisions/activate/action'
          'Microsoft.App/jobs/read'
          'Microsoft.App/jobs/write'
          'Microsoft.App/jobs/start/action'
          'Microsoft.App/jobs/executions/read'
        ]
        notActions: []
        dataActions: []
        notDataActions: []
      }
    ]
  }
}

// api UAMI: Key Vault Secrets User on KV
resource apiKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, kvId, apiPrincipalId, 'kvSecretsUser')
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvSecretsUser)
  }
}

// api UAMI: AcrPull on ACR
resource apiAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, acrId, apiPrincipalId, 'acrPull')
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleAcrPull)
  }
}

// migrator UAMI: AcrPull on ACR
resource migAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, acrId, migratorPrincipalId, 'acrPull')
  properties: {
    principalId: migratorPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleAcrPull)
  }
}

// cicd UAMI: AcrPush on ACR
resource cicdAcrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, acrId, cicdPrincipalId, 'acrPush')
  properties: {
    principalId: cicdPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleAcrPush)
  }
}

// cicd UAMI: custom ACA Operator role on RG — update container apps and start jobs only.
resource cicdRgContrib 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, resourceGroup().id, cicdPrincipalId, 'acaOperator')
  properties: {
    principalId: cicdPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: cicdCustomRoleDef.id
  }
}

// runner UAMI: AcrPull on ACR (pull self-hosted runner image)
resource runnerAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, acrId, runnerPrincipalId, 'acrPull')
  properties: {
    principalId: runnerPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleAcrPull)
  }
}

// runner UAMI: Key Vault Secrets User on KV (read GitHub App private key + app id)
resource runnerKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, kvId, runnerPrincipalId, 'kvSecretsUser')
  properties: {
    principalId: runnerPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvSecretsUser)
  }
}

// agw UAMI: Key Vault Certificate User on KV (read TLS certificate for SSL offload)
resource agwKvCertRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, kvId, agwPrincipalId, 'kvCertUser')
  properties: {
    principalId: agwPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvCertUser)
  }
}

// agw UAMI: Key Vault Secrets User on KV (needed when TLS cert is stored as a KV secret, not KV certificate)
resource agwKvSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, kvId, agwPrincipalId, 'kvSecretsUser')
  properties: {
    principalId: agwPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvSecretsUser)
  }
}

// mcp UAMI: AcrPull on ACR
resource mcpAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, acrId, mcpPrincipalId, 'acrPull')
  properties: {
    principalId: mcpPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleAcrPull)
  }
}

// mcp UAMI: Key Vault Secrets User on KV (read mcp-service-key secret)
resource mcpKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, kvId, mcpPrincipalId, 'kvSecretsUser')
  properties: {
    principalId: mcpPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvSecretsUser)
  }
}
