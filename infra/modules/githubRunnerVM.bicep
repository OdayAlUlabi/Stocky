// Self-hosted GitHub Actions runner on an Ubuntu Linux VM.
// Runs inside the spoke VNet (snet-runner) so it can reach private endpoints
// (Key Vault, ACR, SQL) without public exposure — required in ALZ mode.
//
// Auth to Azure  : User-assigned managed identity (AcrPull, KV Secrets User).
// Auth to GitHub : GitHub App installation token when githubAppId is set; PAT otherwise.
//                  The credential is read from Key Vault at provision time via IMDS.
//
// On first deploy the CustomScript extension downloads, configures and starts the
// runner as a systemd service. Re-deploys are idempotent (config.sh --replace).
//
// Outbound internet access (GitHub API, runner binary download, apt/Docker) is
// required during provisioning:
//   ALZ mode    → hub Azure Firewall + route table on snet-runner (forced tunnel).
//   Standalone  → add a NAT Gateway to snet-runner or enable publicIpEnabled.

@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object

@description('Subnet resource id for the runner NIC (snet-runner).')
param subnetId string

@description('Runner UAMI resource id.')
param runnerIdentityId string

@description('Key Vault base URI, e.g. https://kv-xxx.vault.azure.net/.')
param keyVaultUri string

@description('GitHub owner (org or user) the runner registers to.')
param githubOwner string
@description('GitHub repository name. Leave empty for org-scope runner.')
param githubRepo string = ''

@description('GitHub App id for runner registration tokens. Leave empty to use PAT mode.')
param githubAppId string = ''
@description('GitHub App installation id. Leave empty to use PAT mode.')
param githubInstallationId string = ''
@description('Key Vault secret name holding the GitHub App PEM private key.')
param githubAppPrivateKeySecretName string = 'github-app-private-key'

@description('Runner labels (comma-separated) appended to "self-hosted,linux".')
param runnerLabels string = 'stocky'

@description('VM SKU. Standard_B2s (2 vCPU / 4 GiB) is suitable for most CI workloads.')
param vmSize string = 'Standard_B2s'

@description('OS disk size in GiB. 128 GiB covers the runner binary, Docker images, and build cache.')
param osDiskSizeGiB int = 128

@description('VM admin username. Only used for emergency Bastion access.')
param adminUsername string = 'azureuser'

@description('VM admin password. Randomly generated when not supplied; the VM has no internet-facing SSH port.')
@secure()
@minLength(12)
param adminPassword string = newGuid()

@description('Forces the CustomScript extension to re-run on every deployment. Leave as default (utcNow) to always apply the latest setup.')
param forceUpdateTag string = utcNow()

// ---------------------------------------------------------------------------
// Derived variables
// ---------------------------------------------------------------------------
var useAppAuth = !empty(githubAppId) && !empty(githubInstallationId)
var useAppAuthStr = useAppAuth ? 'true' : 'false'
var credSecretName = useAppAuth ? githubAppPrivateKeySecretName : 'github-runner-pat'

var setupScript = loadTextContent('../scripts/github-runner-setup.sh')

// Build the commandToExecute: export config vars then base64-decode and run the script.
// All values are Bicep-interpolated at deployment time; no secrets appear in plain text
// because the setup script fetches the actual credential from Key Vault at runtime.
var commandToExecute = 'export GH_OWNER="${githubOwner}" GH_REPO="${githubRepo}" GH_LABELS="${runnerLabels}" KV_URI="${keyVaultUri}" USE_APP_AUTH="${useAppAuthStr}" APP_ID="${githubAppId}" INSTALL_ID="${githubInstallationId}" CRED_NAME="${credSecretName}" && echo "${base64(setupScript)}" | base64 -d | bash'

// ---------------------------------------------------------------------------
// NIC
// ---------------------------------------------------------------------------
resource nic 'Microsoft.Network/networkInterfaces@2024-01-01' = {
  name: 'nic-${prefix}-runner'
  location: location
  tags: tags
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig-runner'
        properties: {
          subnet: { id: subnetId }
          privateIPAllocationMethod: 'Dynamic'
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Virtual Machine
// ---------------------------------------------------------------------------
resource vm 'Microsoft.Compute/virtualMachines@2024-03-01' = {
  name: 'vm-${prefix}-runner'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${runnerIdentityId}': {}
    }
  }
  properties: {
    hardwareProfile: { vmSize: vmSize }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts-gen2'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        managedDisk: { storageAccountType: 'Premium_LRS' }
        diskSizeGB: osDiskSizeGiB
        deleteOption: 'Delete'
      }
    }
    osProfile: {
      computerName: 'gh-runner'
      adminUsername: adminUsername
      adminPassword: adminPassword
      linuxConfiguration: {
        disablePasswordAuthentication: false
        provisionVMAgent: true
        patchSettings: {
          patchMode: 'AutomaticByPlatform'
          assessmentMode: 'AutomaticByPlatform'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        { id: nic.id, properties: { deleteOption: 'Delete' } }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: { enabled: true }   // uses managed storage account
    }
  }
}

// ---------------------------------------------------------------------------
// Custom Script Extension — installs and registers the runner
// ---------------------------------------------------------------------------
resource runnerSetup 'Microsoft.Compute/virtualMachines/extensions@2024-03-01' = {
  parent: vm
  name: 'runner-setup'
  location: location
  tags: tags
  properties: {
    publisher: 'Microsoft.Azure.Extensions'
    type: 'CustomScript'
    typeHandlerVersion: '2.1'
    autoUpgradeMinorVersion: true
    forceUpdateTag: forceUpdateTag
    protectedSettings: {
      commandToExecute: commandToExecute
    }
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
output runnerVmName string = vm.name
output runnerVmId string = vm.id
output runnerPrivateIp string = nic.properties.ipConfigurations[0].properties.privateIPAddress
output runnerLabelsOut string = 'self-hosted,linux,${runnerLabels}'
