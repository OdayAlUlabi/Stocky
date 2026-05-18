// Self-hosted GitHub Actions runner as an ACA Event-Driven Job.
// Runs inside the spoke VNet so it can reach private endpoints (ACR data plane,
// SQL, Key Vault) — required when the workload sits behind an ALZ private network.
//
// Scale: 0 -> maxReplicas via the KEDA github-runner scaler, which polls the
// GitHub API for queued workflow_jobs that target the configured runner labels.
// Each replica registers as an ephemeral runner, runs one job, then exits.
//
// Auth model:
//   - To AZURE  : User-assigned managed identity (AcrPull on ACR, KV Secrets User on KV).
//   - To GITHUB : GitHub App installation token (when githubAppId is set), or
//                 Personal Access Token (when githubAppId is empty, PAT mode).
//                 The credential is stored in Key Vault and surfaced via a secret-ref.
//                 GitHub App mode:  KV secret = githubAppPrivateKeySecretName (PEM key)
//                 PAT mode:         KV secret = "github-runner-pat" (classic PAT)
@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object
@description('ACA managed environment id.')
param envId string
@description('ACR login server (the runner image is pulled from here).')
param acrLoginServer string
@description('Runner UAMI resource id.')
param runnerIdentityId string
@description('Runner UAMI client id.')
param runnerIdentityClientId string
@description('Key Vault URI (https://kv-...vault.azure.net).')
param keyVaultUri string

@description('GitHub owner/repo or owner (org) the runner registers to.')
param githubOwner string
@description('GitHub repository name (leave empty to register at org scope).')
param githubRepo string = ''

@description('GitHub App id used to obtain installation tokens. Leave empty to use PAT mode.')
param githubAppId string = ''

@description('GitHub App installation id for the target org/repo. Leave empty to use PAT mode.')
param githubInstallationId string = ''

@description('Key Vault secret name that holds the GitHub App PEM private key.')
param githubAppPrivateKeySecretName string = 'github-app-private-key'

@description('Runner image (default: ephemeral runner image to be pushed to ACR).')
param runnerImage string = '${acrLoginServer}/stocky-gh-runner:latest'

@description('When true, swaps the runner image for a public MCR sample so first-provision succeeds before the real runner image has been pushed to ACR. Set to false once the runner image has been built and pushed.')
param bootstrapRunnerImage bool = true

var effectiveRunnerImage = bootstrapRunnerImage ? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest' : runnerImage

// Auth mode: GitHub App when both App credentials are provided; PAT otherwise.
var useAppAuth = !empty(githubAppId) && !empty(githubInstallationId)
// KV secret name for the active credential.
var credSecretName = useAppAuth ? githubAppPrivateKeySecretName : 'github-runner-pat'

@description('Runner labels appended to "self-hosted, linux". Jobs target these via runs-on.')
param runnerLabels string = 'stocky'

@description('Max parallel runner replicas.')
@minValue(1)
@maxValue(50)
param maxReplicas int = 5

resource runner 'Microsoft.App/jobs@2024-10-02-preview' = {
  name: 'caj-${prefix}-gh-runner'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${runnerIdentityId}': {}
    }
  }
  properties: {
    environmentId: envId
    configuration: {
      // Event-driven trigger. KEDA polls GitHub for queued jobs matching our labels.
      triggerType: 'Event'
      replicaTimeout: 3600
      replicaRetryLimit: 1
      eventTriggerConfig: {
        parallelism: maxReplicas
        replicaCompletionCount: 1
        scale: {
          minExecutions: 0
          maxExecutions: maxReplicas
          pollingInterval: 30
          rules: [
            {
              name: 'github-runner'
              type: 'github-runner'
              metadata: useAppAuth ? {
                owner: githubOwner
                runnerScope: empty(githubRepo) ? 'org' : 'repo'
                repos: empty(githubRepo) ? '' : githubRepo
                labels: runnerLabels
                applicationID: githubAppId
                installationID: githubInstallationId
                targetWorkflowQueueLength: '1'
              } : {
                owner: githubOwner
                runnerScope: empty(githubRepo) ? 'org' : 'repo'
                repos: empty(githubRepo) ? '' : githubRepo
                labels: runnerLabels
                targetWorkflowQueueLength: '1'
              }
              auth: [
                {
                  secretRef: credSecretName
                  triggerParameter: useAppAuth ? 'appKey' : 'personalAccessToken'
                }
              ]
            }
          ]
        }
      }
      // KV-backed secret for the active auth credential.
      secrets: [
        {
          name: credSecretName
          keyVaultUrl: '${keyVaultUri}secrets/${credSecretName}'
          identity: runnerIdentityId
        }
      ]
      registries: bootstrapRunnerImage ? [] : [
        {
          server: acrLoginServer
          identity: runnerIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'runner'
          image: effectiveRunnerImage
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: useAppAuth ? [
            // GitHub App mode env vars (myoung34/github-runner semantics).
            { name: 'ACCESS_TOKEN_SECRET_REF', value: credSecretName }
            { name: 'APP_ID', value: githubAppId }
            { name: 'APP_INSTALLATION_ID', value: githubInstallationId }
            { name: 'APP_PRIVATE_KEY', secretRef: credSecretName }
            { name: 'REPO_OWNER', value: githubOwner }
            { name: 'REPO_NAME', value: githubRepo }
            { name: 'RUNNER_SCOPE', value: empty(githubRepo) ? 'org' : 'repo' }
            { name: 'ORG_NAME', value: githubOwner }
            { name: 'LABELS', value: runnerLabels }
            { name: 'EPHEMERAL', value: 'true' }
            { name: 'DISABLE_AUTO_UPDATE', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: runnerIdentityClientId }
          ] : [
            // PAT mode env vars — ACCESS_TOKEN is the GitHub PAT from KV.
            { name: 'ACCESS_TOKEN', secretRef: credSecretName }
            { name: 'REPO_OWNER', value: githubOwner }
            { name: 'REPO_NAME', value: githubRepo }
            { name: 'RUNNER_SCOPE', value: empty(githubRepo) ? 'org' : 'repo' }
            { name: 'ORG_NAME', value: githubOwner }
            { name: 'LABELS', value: runnerLabels }
            { name: 'EPHEMERAL', value: 'true' }
            { name: 'DISABLE_AUTO_UPDATE', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: runnerIdentityClientId }
          ]
        }
      ]
    }
  }
}

output runnerJobName string = runner.name
output runnerLabelsOut string = runnerLabels
