// Three least-privilege user-assigned managed identities:
//   apiId      — runtime workload (KV, SQL, ACR pull)
//   cicdId     — GitHub Actions OIDC (ACR push, container app update, job start)
//   migratorId — ACA migrator job (ACR pull + SQL db_owner)
@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object
@description('GitHub repo in owner/repo form, e.g. OdayAlUlabi/Stocky.')
param githubRepo string
@description('GitHub branches/environments to federate.')
param githubSubjects array = [
  'repo:OdayAlUlabi/Stocky:ref:refs/heads/main'
  'repo:OdayAlUlabi/Stocky:environment:prod'
]

resource apiId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-api'
  location: location
  tags: tags
}

resource cicdId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-cicd'
  location: location
  tags: tags
}

resource migratorId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-migrator'
  location: location
  tags: tags
}

resource runnerId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-runner'
  location: location
  tags: tags
}

// Federated credentials: trust GitHub Actions OIDC issuer.
// @batchSize(1) serializes FIC creation — Azure's UAMI service rejects
// concurrent FIC writes for the same managed identity (ConcurrentFederatedIdentityCredentialsWritesForSingleManagedIdentity).
@batchSize(1)
resource fic 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = [for (subject, i) in githubSubjects: {
  parent: cicdId
  name: 'gh-${i}'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: subject
    audiences: [ 'api://AzureADTokenExchange' ]
  }
}]

output apiIdId string = apiId.id
output apiIdPrincipalId string = apiId.properties.principalId
output apiIdClientId string = apiId.properties.clientId

output cicdIdId string = cicdId.id
output cicdIdPrincipalId string = cicdId.properties.principalId
output cicdIdClientId string = cicdId.properties.clientId

output migratorIdId string = migratorId.id
output migratorIdPrincipalId string = migratorId.properties.principalId
output migratorIdClientId string = migratorId.properties.clientId

output runnerIdId string = runnerId.id
output runnerIdPrincipalId string = runnerId.properties.principalId
output runnerIdClientId string = runnerId.properties.clientId

output githubRepoOut string = githubRepo
