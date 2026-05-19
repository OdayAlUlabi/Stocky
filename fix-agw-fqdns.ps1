$sub = "0e4ce08c-f474-47e3-ad32-1df4f48822ea"
$rg  = "rg-stocky-prod-sweden"
$agw = "agw-stocky-prod"
$apiFqdn = "ca-stocky-prod-api.delightfulflower-1900046d.swedencentral.azurecontainerapps.io"
$webFqdn = "ca-stocky-prod-web.delightfulflower-1900046d.swedencentral.azurecontainerapps.io"

# ── Step 1: enable HTTP on the web container app so ACA's envoy never
# generates an HTTP→HTTPS redirect that leaks the internal ACA hostname. ──────
Write-Host "Enabling allowInsecure on web container app..."
az containerapp ingress update `
    --name ca-stocky-prod-web `
    --resource-group $rg `
    --subscription $sub `
    --allow-insecure `
    --transport http
Write-Host "Done."

Write-Host "Fetching App Gateway config..."
$json = az network application-gateway show --name $agw --resource-group $rg --subscription $sub -o json
$cfg = $json | ConvertFrom-Json

$poolApi = $cfg.backendAddressPools | Where-Object { $_.name -eq "pool-api" }
$poolWeb = $cfg.backendAddressPools | Where-Object { $_.name -eq "pool-web" }
$probeApi = $cfg.probes | Where-Object { $_.name -eq "probe-api" }
$probeWeb = $cfg.probes | Where-Object { $_.name -eq "probe-web" }

Write-Host "Before: pool-api  = $($poolApi.properties.backendAddresses[0].fqdn)"
Write-Host "Before: probe-api = $($probeApi.properties.host)"

$poolApi.properties.backendAddresses[0].fqdn  = $apiFqdn
$poolWeb.properties.backendAddresses[0].fqdn  = $webFqdn
$probeApi.properties.host = $apiFqdn
$probeWeb.properties.host = $webFqdn

# Fix web backend: switch from HTTPS/443 to HTTP/80 so ACA's envoy never
# generates an HTTP→HTTPS redirect that leaks the internal ACA hostname.
$settingsWeb = $cfg.backendHttpSettingsCollection | Where-Object { $_.name -eq "https-web" }
if ($settingsWeb) {
    $settingsWeb.properties.port     = 80
    $settingsWeb.properties.protocol = "Http"
    Write-Host "Set https-web backend: port=80 protocol=Http"
}
# probe-web must match the backend protocol
$probeWeb.properties.protocol = "Http"

Write-Host "After: pool-api  = $($poolApi.properties.backendAddresses[0].fqdn)"
Write-Host "After: probe-api = $($probeApi.properties.host)"

$body = $cfg | ConvertTo-Json -Depth 30 -Compress
$url = "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.Network/applicationGateways/$($agw)?api-version=2024-01-01"

Write-Host "Sending PUT to update App Gateway (this takes a few minutes)..."
$result = az rest --method PUT --url $url --body $body 2>&1
$resultStr = $result | Out-String
if ($resultStr -match '"provisioningState":\s*"([^"]+)"') {
    Write-Host "provisioningState: $($Matches[1])"
} elseif ($resultStr -match '"code":\s*"([^"]+)"') {
    Write-Host "Error code: $($Matches[1])"
    Write-Host $resultStr | Select-Object -First 10
} else {
    Write-Host "Result: $($resultStr.Substring(0, [Math]::Min(500, $resultStr.Length)))"
}
