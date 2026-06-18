param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $false)]
    [int]$LookbackHours = 24,

    [Parameter(Mandatory = $false)]
    [string]$Prefix = 'stocky-prod'
)

$ErrorActionPreference = 'Stop'

function Get-Window {
    $start = (Get-Date).ToUniversalTime().AddHours(-$LookbackHours).ToString('yyyy-MM-ddTHH:mm:ssZ')
    $end = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    return @{ Start = $start; End = $end }
}

function Get-MetricSeriesValues {
    param(
        [string]$ResourceId,
        [string]$Metric,
        [ValidateSet('Total', 'Average', 'Maximum', 'Minimum', 'Count')]
        [string]$Aggregation,
        [string]$Field,
        [string]$Start,
        [string]$End
    )

    $query = "value[0].timeseries[0].data[].$Field"
    $raw = az monitor metrics list `
        --resource $ResourceId `
        --metric $Metric `
        --aggregation $Aggregation `
        --interval PT5M `
        --start-time $Start `
        --end-time $End `
        --query $query `
        -o tsv

    return @($raw | Where-Object { $_ -ne '' })
}

function Get-ContainerAppSummary {
    param(
        [string]$Name,
        [string]$ResourceId,
        [string]$Start,
        [string]$End
    )

    $reqVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'Requests' -Aggregation 'Total' -Field 'total' -Start $Start -End $End
    $rtVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'ResponseTime' -Aggregation 'Average' -Field 'average' -Start $Start -End $End
    $cpuVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'CpuPercentage' -Aggregation 'Maximum' -Field 'maximum' -Start $Start -End $End
    $memVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'MemoryPercentage' -Aggregation 'Maximum' -Field 'maximum' -Start $Start -End $End
    $repVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'Replicas' -Aggregation 'Maximum' -Field 'maximum' -Start $Start -End $End
    $rstVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'RestartCount' -Aggregation 'Total' -Field 'total' -Start $Start -End $End

    [pscustomobject]@{
        Resource = $Name
        Requests = if ($reqVals.Count) { [math]::Round((($reqVals | Measure-Object -Sum).Sum), 0) } else { 0 }
        AvgResponseTime = if ($rtVals.Count) { [math]::Round((($rtVals | Measure-Object -Average).Average), 2) } else { $null }
        MaxCpuPct = if ($cpuVals.Count) { [math]::Round((($cpuVals | Measure-Object -Maximum).Maximum), 2) } else { $null }
        MaxMemPct = if ($memVals.Count) { [math]::Round((($memVals | Measure-Object -Maximum).Maximum), 2) } else { $null }
        MaxReplicas = if ($repVals.Count) { [int](($repVals | Measure-Object -Maximum).Maximum) } else { 0 }
        RestartCount = if ($rstVals.Count) { [math]::Round((($rstVals | Measure-Object -Sum).Sum), 0) } else { 0 }
    }
}

function Get-AppGatewaySummary {
    param(
        [string]$ResourceId,
        [string]$Start,
        [string]$End
    )

    $reqVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'TotalRequests' -Aggregation 'Total' -Field 'total' -Start $Start -End $End
    $failVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'FailedRequests' -Aggregation 'Total' -Field 'total' -Start $Start -End $End
    $connVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'CurrentConnections' -Aggregation 'Maximum' -Field 'maximum' -Start $Start -End $End
    $unhealthyVals = Get-MetricSeriesValues -ResourceId $ResourceId -Metric 'UnhealthyHostCount' -Aggregation 'Maximum' -Field 'maximum' -Start $Start -End $End

    [pscustomobject]@{
        TotalRequests = if ($reqVals.Count) { [math]::Round((($reqVals | Measure-Object -Sum).Sum), 0) } else { 0 }
        FailedRequests = if ($failVals.Count) { [math]::Round((($failVals | Measure-Object -Sum).Sum), 0) } else { 0 }
        PeakCurrentConnections = if ($connVals.Count) { [math]::Round((($connVals | Measure-Object -Maximum).Maximum), 2) } else { $null }
        PeakUnhealthyHosts = if ($unhealthyVals.Count) { [math]::Round((($unhealthyVals | Measure-Object -Maximum).Maximum), 2) } else { $null }
    }
}

$window = Get-Window

$apiId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.App/containerApps/ca-$Prefix-api"
$webId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.App/containerApps/ca-$Prefix-web-mvc"
$mcpId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.App/containerApps/ca-$Prefix-mcp"
$agwId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Network/applicationGateways/agw-$Prefix"

Write-Host "Load review window: $($window.Start) .. $($window.End) (UTC)"
Write-Host ''

$appRows = @(
    Get-ContainerAppSummary -Name 'api' -ResourceId $apiId -Start $window.Start -End $window.End
    Get-ContainerAppSummary -Name 'web-mvc' -ResourceId $webId -Start $window.Start -End $window.End
    Get-ContainerAppSummary -Name 'mcp' -ResourceId $mcpId -Start $window.Start -End $window.End
)

Write-Host 'Container Apps:'
$appRows | Format-Table -AutoSize
Write-Host ''

$agw = Get-AppGatewaySummary -ResourceId $agwId -Start $window.Start -End $window.End
Write-Host 'Application Gateway:'
$agw | Format-List
