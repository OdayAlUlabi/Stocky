param(
    [string]$sub = "0e4ce08c-f474-47e3-ad32-1df4f48822ea",
    [string]$rg  = "rg-stocky-prod-sweden"
)

$ErrorActionPreference = "Stop"

$wafUrl = "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.Network/ApplicationGatewayWebApplicationFirewallPolicies/waf-stocky-prod?api-version=2024-01-01"
$agwUrl = "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.Network/applicationGateways/agw-stocky-prod?api-version=2024-01-01"

# ── 1. Update WAF AllowOAuthPaths to include PRM path ─────────────────────────
Write-Host "`n=== STEP 1: Update WAF ==="
$p = az rest --method GET --url $wafUrl | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Failed to GET WAF policy" }

$rule = $p.properties.customRules | Where-Object { $_.name -eq 'AllowOAuthPaths' }
if (-not $rule) { throw "AllowOAuthPaths rule not found" }

$mc     = $rule.matchConditions[0]
$values = [System.Collections.Generic.List[string]]($mc.matchValues)
Write-Host "Current matchValues: $($values -join ', ')"

$newPath = "/.well-known/oauth-protected-resource"
if ($values -notcontains $newPath) {
    $values.Insert(0, $newPath)
    # PSCustomObject properties are read-only after JSON parse; rebuild array inline
    $mc.PSObject.Properties['matchValues'].Value = $values.ToArray()
    Write-Host "Added: $newPath"
} else {
    Write-Host "Already present: $newPath"
}

$json = $p | ConvertTo-Json -Depth 20 -Compress
$json | Out-File -Encoding utf8 "$env:TEMP\waf-prm.json"
$result = az rest --method PUT --url $wafUrl --body "@$env:TEMP\waf-prm.json" --query 'properties.customRules[0].matchConditions[0].matchValues' -o json
if ($LASTEXITCODE -ne 0) { throw "Failed to PUT WAF policy" }
Write-Host "WAF updated. New matchValues:"
$result | ConvertFrom-Json | ForEach-Object { Write-Host "  $_" }

# ── 2. Update AGW mcp-oauth path rule to include PRM path ─────────────────────
Write-Host "`n=== STEP 2: Update AGW path map ==="
$agw = az rest --method GET --url $agwUrl | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Failed to GET AGW" }

# Find the mcp-oauth path rule across all urlPathMaps
$found = $false
foreach ($pm in $agw.properties.urlPathMaps) {
    foreach ($pr in $pm.properties.pathRules) {
        if ($pr.name -eq 'mcp-oauth') {
            $paths = [System.Collections.Generic.List[string]]($pr.properties.paths)
            Write-Host "Current mcp-oauth paths: $($paths -join ', ')"
            if ($paths -notcontains $newPath) {
                $paths.Add($newPath)
                $pr.properties.PSObject.Properties['paths'].Value = $paths.ToArray()
                Write-Host "Added $newPath to mcp-oauth"
            } else {
                Write-Host "Already present in mcp-oauth"
            }
            $found = $true
            break
        }
    }
    if ($found) { break }
}

if (-not $found) {
    Write-Host "WARNING: mcp-oauth path rule not found. Check AGW config."
} else {
    $agwJson = $agw | ConvertTo-Json -Depth 30 -Compress
    $agwJson | Out-File -Encoding utf8 "$env:TEMP\agw-prm.json"
    az rest --method PUT --url $agwUrl --body "@$env:TEMP\agw-prm.json" --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to PUT AGW" }
    Write-Host "AGW updated."
}

Write-Host "`n=== DONE ==="
