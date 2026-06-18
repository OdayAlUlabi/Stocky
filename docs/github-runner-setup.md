# GitHub Runner Provisioning Re-enablement Guide

## Current Status
- **Runner VM**: `vm-stocky-prod-runner` deployed in `rg-stocky-prod-sweden`
- **Custom Script Extension**: `Failed` (v2.1)
- **Root Cause**: Runner setup extension failed during initial deployment (likely missing GitHub credentials in Key Vault)

## Prerequisites: GitHub Runner Auth Setup

The GitHub runner requires **one** of two authentication methods configured in Key Vault before provisioning:

### Option 1: GitHub App Installation (Recommended for Enterprise)

1. **Create a GitHub App** in your GitHub organization:
   - Go to Settings > Developer settings > GitHub Apps
   - Create a new app with:
     - **Permissions**: `actions:write`, `checks:write`, `contents:write`, `workflows:write`
     - **Where can this GitHub App be installed?**: Only on this account
   - **Install the app** on your Stocky repository

2. **Extract and store credentials**:
   ```bash
   # Note: GitHub App ID (visible in app page)
   # Note: Installation ID (Settings > Apps > Installations > find the installation)
   # Download: Private key (PEM format) from app settings
   ```

3. **Store in Key Vault**:
   ```pwsh
   $kvName = "kv-stocky-prod-swedencentral"
   $keyPath = "C:\path\to\github-app-key.pem"
   $keyContent = Get-Content $keyPath -Raw
   
   # Store the PEM private key
   az keyvault secret set `
     --vault-name $kvName `
     --name "github-app-private-key" `
     --value $keyContent
   ```

### Option 2: GitHub Personal Access Token (PAT)

1. **Create a PAT** in GitHub:
   - User Settings > Developer settings > Personal access tokens > Classic
   - Scopes needed: `repo`, `workflow`, `admin:repo_hook`

2. **Store in Key Vault**:
   ```pwsh
   az keyvault secret set `
     --vault-name "kv-stocky-prod-swedencentral" `
     --name "github-runner-pat" `
     --value "ghp_xxxxx..."
   ```

## Re-enable Runner Provisioning

After configuring credentials above, re-deploy the runner with `azd up`:

```pwsh
# Update azure.yaml with GitHub App credentials
# (The runner is conditionally deployed when deployGitHubRunner=true and credentials are provided)

cd C:\repos\Stocky

# Option 1: Using GitHub App (recommended)
azd env set githubAppId "123456"
azd env set githubInstallationId "789012"
azd env set deployGitHubRunner "true"

# Option 2: Using PAT (simpler, just ensure secret "github-runner-pat" exists in KV)
azd env set deployGitHubRunner "true"

# Deploy infrastructure
azd up
```

## Troubleshoot if Extension Still Fails

If `runner-setup` extension fails again after re-deployment:

```pwsh
# 1. Check extension status
az vm extension show `
  -g rg-stocky-prod-sweden `
  -n runner-setup `
  --vm-name vm-stocky-prod-runner `
  -o json

# 2. Query VM for setup logs
az vm run-command invoke `
  -g rg-stocky-prod-sweden `
  -n vm-stocky-prod-runner `
  --command-id RunShellScript `
  --scripts "cat /var/log/waagent.log | tail -30" `
  --query "value[0].message" -o tsv

# 3. Check if credentials are readable by the runner
az vm run-command invoke `
  -g rg-stocky-prod-sweden `
  -n vm-stocky-prod-runner `
  --command-id RunShellScript `
  --scripts "curl -H 'Metadata:true' 'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2021-02-01&resource=https%3A%2F%2Fvault.azure.net' | grep -q access_token && echo 'IMDS OK' || echo 'IMDS FAILED'" `
  --query "value[0].message" -o tsv
```

## Expected Outcome

Once the runner is successfully registered:

1. Runner appears in GitHub repository: Settings > Actions > Runners
2. Runner status shows "Idle" (ready to accept jobs)
3. CI/CD workflows can target the runner with `runs-on: [self-hosted, linux, stocky]`
4. Builds execute on the private VM in the spoke VNet with access to private endpoints (SQL, ACR, KV)

## Additional References

- **Runner VM Details**: 
  - SKU: `Standard_B2s` (2 vCPU, 4 GiB RAM)
  - Location: `swedencentral`
  - Subnet: Private spoke VNet (`snet-runner`)
  - Labels: `self-hosted,linux,stocky`

- **Network**:
  - Outbound internet access: Required during provisioning (via Azure Firewall NAT if in hub-spoke)
  - Inbound: None (private IP only, Bastion for emergency access)
  - DNS: Private endpoint access to Key Vault via private DNS zone

- **Security**:
  - Identity: User-assigned managed identity with `AcrPull`, `Key Vault Secrets User` roles
  - Credential storage: Azure Key Vault (secrets never in code/logs)
  - Auth flow: IMDS → KeyVault → GitHub API
