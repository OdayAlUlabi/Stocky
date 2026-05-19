<#
.SYNOPSIS
    Creates a self-signed TLS certificate in Key Vault and wires it into the
    App Gateway via azd provision.

.DESCRIPTION
    Run this once before (or after) the first `azd provision`.
    After it completes, `azd provision` will enable the HTTPS (443) listener on
    the App Gateway and redirect HTTP to HTTPS automatically.

    For production you should replace the self-signed cert with a CA-signed one
    (e.g. from Let's Encrypt) by uploading the PFX to Key Vault with the same
    name and re-running `azd provision`.

.PARAMETER KvName
    Key Vault name.  Defaults to the KEYVAULT_NAME azd output.

.PARAMETER CertName
    Certificate name inside Key Vault.  Default: stocky-tls

.PARAMETER Provision
    When set, runs `azd provision` automatically after the cert is created.

.EXAMPLE
    # Minimal – just create the cert and print the secret URI.
    .\infra\create-tls-cert.ps1

    # Create cert AND trigger a provision to activate HTTPS.
    .\infra\create-tls-cert.ps1 -Provision
#>
param(
    [string] $KvName     = '',
    [string] $CertName   = 'stocky-tls',
    [string] $ResourceGroup = '',
    [switch] $Provision
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve Key Vault name ────────────────────────────────────────────────────
if (-not $KvName) {
    Write-Host 'Resolving Key Vault name from azd env ...'
    $KvName = (azd env get-values --output json 2>$null | ConvertFrom-Json).KEYVAULT_NAME
    if (-not $KvName) {
        Write-Error 'Could not determine KEYVAULT_NAME. Either pass -KvName or run `azd provision` first.'
    }
}
if (-not $ResourceGroup) {
    $ResourceGroup = (azd env get-values --output json 2>$null | ConvertFrom-Json).AZURE_RESOURCE_GROUP
    if (-not $ResourceGroup) { $ResourceGroup = 'rg-stocky-prod-sweden' }
}
Write-Host "Key Vault : $KvName"
Write-Host "Cert name : $CertName"
Write-Host "Resource Group: $ResourceGroup"

# ── Temporarily enable KV public network access (data plane unreachable otherwise) ─
Write-Host "`nTemporarily enabling Key Vault public network access ..."
az keyvault update --name $KvName --resource-group $ResourceGroup --public-network-access Enabled | Out-Null
Write-Host "Waiting 15 s for network change to propagate ..."
Start-Sleep -Seconds 15

try {

# ── Check if certificate already exists ──────────────────────────────────────
Write-Host "`nChecking for existing certificate ..."
$existing = az keyvault certificate show --vault-name $KvName --name $CertName --query "id" -o tsv 2>$null
if ($existing) {
    Write-Host "Certificate '$CertName' already exists — skipping creation."
} else {
    Write-Host "Creating self-signed certificate '$CertName' (may take ~30 s) ..."
    # Write policy to a temp file to avoid PowerShell/az-CLI JSON quoting issues.
    $policyFile = [System.IO.Path]::GetTempFileName() + '.json'
    @"
{
  "issuerParameters": { "name": "Self" },
  "keyProperties": { "exportable": true, "keyType": "RSA", "keySize": 2048, "reuseKey": false },
  "secretProperties": { "contentType": "application/x-pkcs12" },
  "x509CertificateProperties": {
    "subject": "CN=stocky.swedencentral.cloudapp.azure.com",
    "subjectAlternativeNames": { "dnsNames": ["stocky.swedencentral.cloudapp.azure.com"] },
    "validityInMonths": 12,
    "keyUsage": ["cRLSign","dataEncipherment","digitalSignature","keyEncipherment","keyCertSign"],
    "ekus": ["1.3.6.1.5.5.7.3.1","1.3.6.1.5.5.7.3.2"]
  },
  "lifetimeActions": [
    { "trigger": { "daysBeforeExpiry": 30 }, "action": { "actionType": "AutoRenew" } }
  ]
}
"@ | Set-Content -Encoding UTF8 -Path $policyFile

    try {
        az keyvault certificate create `
            --vault-name $KvName `
            --name $CertName `
            --policy "@$policyFile" | Out-Null
    } finally {
        Remove-Item $policyFile -ErrorAction SilentlyContinue
    }

    # Wait for the certificate to reach 'completed' state
    $deadline = (Get-Date).AddSeconds(120)
    do {
        Start-Sleep -Seconds 5
        $state = az keyvault certificate show --vault-name $KvName --name $CertName `
                     --query "policy.issuerParameters.certificateType || attributes.enabled" -o tsv 2>$null
        $status = az keyvault certificate show --vault-name $KvName --name $CertName `
                      --query "attributes.enabled" -o tsv 2>$null
        Write-Host "  ... waiting (enabled=$status)"
    } while ($status -ne 'true' -and (Get-Date) -lt $deadline)
    Write-Host "Certificate created successfully."
}

# ── Retrieve the secret URI (versionless, always points to latest) ────────────
$secretId = az keyvault certificate show `
    --vault-name $KvName `
    --name $CertName `
    --query "sid" -o tsv

# Strip the version segment so App Gateway always uses the current cert.
$secretUri = ($secretId -replace '/[^/]+$', '')
Write-Host "`nSecret URI (versionless): $secretUri"

# ── Persist into azd environment ─────────────────────────────────────────────
Write-Host "`nSetting TLS_CERT_SECRET_URI in azd env ..."
azd env set TLS_CERT_SECRET_URI $secretUri

} finally {
    # ── Restore KV public network access to Disabled ─────────────────────────
    Write-Host "`nRestoring Key Vault public network access to Disabled ..."
    az keyvault update --name $KvName --resource-group $ResourceGroup --public-network-access Disabled | Out-Null
    Write-Host "Key Vault network access restored."
}

Write-Host @"

Done.  The TLS_CERT_SECRET_URI env var is now set.

Next step
---------
Run `azd provision` to activate the HTTPS listener on the App Gateway:

    azd provision --no-prompt
"@

if ($Provision) {
    Write-Host "`nRunning azd provision ..."
    azd provision --no-prompt
}
