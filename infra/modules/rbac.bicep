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

var roleKvSecretsUser   = '4633458b-17de-9d6c-5a8c-3c1d8c2d4adb' // Key Vault Secrets User
var roleAcrPull         = '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
var roleAcrPush         = '8311e382-0749-4cb8-b61a-304f252e45ec' // AcrPush
var roleContribAcaApps  = 'b24988ac-6180-42a0-ab88-20f7382dd24c' // Contributor (resource-group scope for CI to update apps; tighten later via custom role)

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

// cicd UAMI: Contributor on RG so it can update container apps + start jobs.
// TODO: replace with a custom role limited to Microsoft.App/*/write,read,listSecrets/action
resource cicdRgContrib 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(prefix, resourceGroup().id, cicdPrincipalId, 'contrib')
  properties: {
    principalId: cicdPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleContribAcaApps)
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
