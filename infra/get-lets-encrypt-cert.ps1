<#
.SYNOPSIS
    Obtains a Let's Encrypt certificate for the Stocky App Gateway via HTTP-01.

.DESCRIPTION
    Uses certbot (installed automatically via winget if needed) with --manual-auth-hook
    to serve the ACME HTTP-01 challenge from the MCP container app.

    The MCP container app must already be deployed with the
    /.well-known/acme-challenge/{token} route (src/Mcp/Program.cs) and the
    AGW path rule /.well-known/acme-challenge/* → pool-mcp.

    Flow:
      1. certbot requests an HTTP-01 challenge from Let's Encrypt.
      2. The auth-hook sets Acme__ChallengeToken / Acme__ChallengeValue env vars on
         the ACA MCP container app and waits 120 s for the new revision to go live.
      3. Let's Encrypt requests:
           GET http://stocky.swedencentral.cloudapp.azure.com/.well-known/acme-challenge/<token>
         AGW redirects HTTP→HTTPS; Let's Encrypt follows the redirect.
         MCP server returns the key-authorization value. Challenge passes.
      4. Cert is exported as PFX and uploaded to KV (secret name: stocky-tls).
      5. AGW is stopped/started to reload the KV certificate immediately.

.PARAMETER Email
    Registration e-mail for Let's Encrypt (required first run).

.PARAMETER KvName
    Key Vault name. Defaults to KEYVAULT_NAME azd output.

.PARAMETER CertSecretName
    Key Vault secret that the AGW currently reads. Default: stocky-tls

.PARAMETER ResourceGroup
    Azure resource group. Default: rg-stocky-prod-sweden

.PARAMETER ContainerAppName
    ACA container app for the MCP server. Default: ca-stocky-prod-mcp

.PARAMETER AgwName
    App Gateway name. Default: agw-stocky-prod

.PARAMETER Domain
    Domain to certify. Default: stocky.swedencentral.cloudapp.azure.com

.PARAMETER Staging
    Use Let's Encrypt staging (free of rate limits). Test here first;
    staging certs are NOT trusted by browsers or Claude.ai.

.EXAMPLE
    # Staging test first:
    .\infra\get-lets-encrypt-cert.ps1 -Email you@example.com -Staging

    # Production cert:
    .\infra\get-lets-encrypt-cert.ps1 -Email you@example.com
#>
param(
    [Parameter(Mandatory)] [string] $Email,
    [string] $KvName           = '',
    [string] $CertSecretName   = 'stocky-tls',
    [string] $ResourceGroup    = 'rg-stocky-prod-sweden',
    [string] $ContainerAppName = 'ca-stocky-prod-mcp',
    [string] $AgwName          = 'agw-stocky-prod',
    [string] $Domain           = 'stocky.swedencentral.cloudapp.azure.com',
    [switch] $Staging
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Ensure certbot is installed ───────────────────────────────────────────────
$certbot = Get-Command certbot -ErrorAction SilentlyContinue
if (-not $certbot) {
    Write-Host "certbot not found. Installing via winget ..."
    winget install --id EFF.Certbot --accept-package-agreements --accept-source-agreements
    # Refresh PATH
    $env:PATH = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('Path', 'User')
    $certbot = Get-Command certbot -ErrorAction SilentlyContinue
    if (-not $certbot) {
        throw "certbot installation failed. Install manually from https://certbot.eff.org/instructions?os=windows and re-run."
    }
}
Write-Host "certbot: $($certbot.Source)"

# ── Resolve Key Vault name ────────────────────────────────────────────────────
if (-not $KvName) {
    $azdVals = azd env get-values --output json 2>$null | ConvertFrom-Json
    $KvName  = $azdVals.KEYVAULT_NAME
    if (-not $KvName) { throw "Could not determine KEYVAULT_NAME. Pass -KvName." }
}
Write-Host "Key Vault     : $KvName"
Write-Host "Container App : $ContainerAppName"
Write-Host "Domain        : $Domain"
Write-Host "Staging       : $Staging"

# ── Write auth-hook script ────────────────────────────────────────────────────
# certbot sets CERTBOT_TOKEN and CERTBOT_VALIDATION before calling this script.
$hookDir = Join-Path $env:TEMP "stocky-certbot-hooks"
New-Item -ItemType Directory -Force -Path $hookDir | Out-Null

$authHookPath = Join-Path $hookDir "auth-hook.ps1"
$authHookContent = @"
# Called by certbot before Let's Encrypt verifies the HTTP-01 challenge.
`$token = `$env:CERTBOT_TOKEN
`$value = `$env:CERTBOT_VALIDATION
Write-Host "[auth-hook] token=`$token"
Write-Host "[auth-hook] Setting ACA env vars ..."
az containerapp update ``
    --name      "$ContainerAppName" ``
    --resource-group "$ResourceGroup" ``
    --set-env-vars "Acme__ChallengeToken=`$token" "Acme__ChallengeValue=`$value" ``
    --output none 2>&1 | Out-Null
Write-Host "[auth-hook] Waiting 120 s for new ACA revision to become active ..."
Start-Sleep -Seconds 120
`$url = "https://$Domain/.well-known/acme-challenge/`$token"
try {
    `$r = Invoke-WebRequest `$url -TimeoutSec 15 -UseBasicParsing -SkipCertificateCheck
    if (`$r.Content -eq `$value) { Write-Host "[auth-hook] Challenge verified." }
    else { Write-Warning "[auth-hook] Body mismatch; Let's Encrypt will verify independently." }
} catch { Write-Warning "[auth-hook] Probe failed: `$_" }
"@
Set-Content -Path $authHookPath -Value $authHookContent -Encoding UTF8

$cleanupHookPath = Join-Path $hookDir "cleanup-hook.ps1"
$cleanupHookContent = @"
# Called by certbot after verification (success or failure) to clean up.
Write-Host "[cleanup-hook] Clearing ACA challenge env vars ..."
az containerapp update ``
    --name      "$ContainerAppName" ``
    --resource-group "$ResourceGroup" ``
    --set-env-vars "Acme__ChallengeToken= " "Acme__ChallengeValue= " ``
    --output none 2>&1 | Out-Null
"@
Set-Content -Path $cleanupHookPath -Value $cleanupHookContent -Encoding UTF8

# Wrapper batch files (certbot on Windows calls hooks as a command string)
$authHookBat  = Join-Path $hookDir "auth-hook.bat"
$cleanupHookBat = Join-Path $hookDir "cleanup-hook.bat"
Set-Content -Path $authHookBat   -Value "@pwsh -NonInteractive -File `"$authHookPath`"" -Encoding ASCII
Set-Content -Path $cleanupHookBat -Value "@pwsh -NonInteractive -File `"$cleanupHookPath`"" -Encoding ASCII

# ── Temporarily enable KV public network access ───────────────────────────────
Write-Host "`nEnabling Key Vault public network access ..."
az keyvault update --name $KvName --resource-group $ResourceGroup `
    --public-network-access Enabled | Out-Null
Write-Host "Waiting 15 s ..."
Start-Sleep -Seconds 15

# ── Run certbot ───────────────────────────────────────────────────────────────
$caServer = if ($Staging) { '--staging' } else { $null }

Write-Host "`nRunning certbot ..."
$certbotArgs = @(
    'certonly'
    '--manual'
    '--preferred-challenges', 'http'
    '--manual-auth-hook', $authHookBat
    '--manual-cleanup-hook', $cleanupHookBat
    '--non-interactive'
    '--agree-tos'
    '--email', $Email
    '--domain', $Domain
    '--force-renewal'
)
if ($Staging) { $certbotArgs += '--staging' }

& certbot @certbotArgs
if ($LASTEXITCODE -ne 0) {
    throw "certbot failed with exit code $LASTEXITCODE. See output above."
}

# ── Locate cert files ─────────────────────────────────────────────────────────
# On Windows, certbot stores certs in C:\Certbot\live\<domain>\
$certDir = "C:\Certbot\live\$Domain"
if (-not (Test-Path $certDir)) {
    # Try legacy path
    $certDir = "C:\Certbot\archive\$Domain"
}
if (-not (Test-Path $certDir)) {
    throw "Could not locate certbot output directory. Expected: C:\Certbot\live\$Domain"
}

$fullchainPem = Join-Path $certDir "fullchain.pem"
$privkeyPem   = Join-Path $certDir "privkey.pem"

if (-not (Test-Path $fullchainPem) -or -not (Test-Path $privkeyPem)) {
    throw "PEM files not found in $certDir"
}

Write-Host "Cert files found in $certDir"

# ── Build PFX from PEM files (.NET 5+ API) ───────────────────────────────────
Write-Host "Converting PEM → PFX ..."
Add-Type -AssemblyName System.Security
$x509 = [System.Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($fullchainPem, $privkeyPem)
$pfxBytes  = $x509.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx)
$pfxBase64 = [Convert]::ToBase64String($pfxBytes)

Write-Host "Certificate: Subject=$($x509.Subject)"
Write-Host "             NotAfter=$($x509.GetExpirationDateString())"

# ── Upload PFX to Key Vault (ARM management plane — no data-plane needed) ────
Write-Host "`nUploading PFX to Key Vault '$KvName' as secret '$CertSecretName' ..."
$subId  = az account show --query id -o tsv
$kvUri  = "https://management.azure.com/subscriptions/$subId/resourceGroups/$ResourceGroup/providers/Microsoft.KeyVault/vaults/$KvName/secrets/$CertSecretName`?api-version=2022-07-01"
$body   = @{
    properties = @{
        value       = $pfxBase64
        contentType = 'application/x-pkcs12'
        attributes  = @{ enabled = $true }
    }
} | ConvertTo-Json -Depth 5

$bodyFile = [System.IO.Path]::GetTempFileName() + '.json'
Set-Content -Path $bodyFile -Value $body -Encoding UTF8
az rest --method PUT --url $kvUri --body "@$bodyFile" | Out-Null
Remove-Item $bodyFile -Force

Write-Host "PFX uploaded to Key Vault."

# ── Restore KV network access ─────────────────────────────────────────────────
Write-Host "`nRestoring Key Vault private-only network access ..."
az keyvault update --name $KvName --resource-group $ResourceGroup `
    --public-network-access Disabled | Out-Null

# ── Restart AGW to reload KV cert immediately (else waits up to 4 h) ─────────
Write-Host "`nRestarting App Gateway to reload KV certificate ..."
az network application-gateway stop  --name $AgwName --resource-group $ResourceGroup
az network application-gateway start --name $AgwName --resource-group $ResourceGroup
Write-Host "App Gateway restarted."

Write-Host @"

============================================================
  SUCCESS — Publicly-trusted Let's Encrypt certificate live.
  NotAfter : $($x509.GetExpirationDateString())

  Add a calendar reminder to re-run this script before expiry.
  Let's Encrypt certificates expire after 90 days.
============================================================
"@
