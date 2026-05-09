param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$OutputRoot = "Results\m1g_time_parameter_search",
    [string]$OutCsv = ""
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $Root $path
}

function Get-KpiValue($rows, [string]$metric, [string]$field = "total") {
    $row = $rows | Where-Object { $_.metric -eq $metric } | Select-Object -First 1
    if ($null -eq $row) { return [double]::NaN }
    $value = $row.$field
    if ([string]::IsNullOrWhiteSpace($value)) { return [double]::NaN }
    return [double]$value
}

$outputRootAbs = Resolve-RepoPath $OutputRoot
if ([string]::IsNullOrWhiteSpace($OutCsv)) {
    $OutCsv = Join-Path $outputRootAbs "run_summary.csv"
}
elseif (![System.IO.Path]::IsPathRooted($OutCsv)) {
    $OutCsv = Join-Path $Root $OutCsv
}

$rows = New-Object System.Collections.Generic.List[object]
foreach ($dir in Get-ChildItem -LiteralPath $outputRootAbs -Directory | Where-Object { $_.Name -ne "_generated_configs" }) {
    $kpiPath = Join-Path $dir.FullName "kpi_report.csv"
    if (!(Test-Path -LiteralPath $kpiPath)) { continue }
    $kpi = Import-Csv -LiteralPath $kpiPath
    $controllerPath = Join-Path $dir.FullName "controller.txt"
    $configId = if (Test-Path -LiteralPath $controllerPath) { (Get-Content -LiteralPath $controllerPath -Raw).Trim() } else { $dir.Name }
    $seedText = ($dir.Name -split "-")[-1]
    $seed = 0
    [void][int]::TryParse($seedText, [ref]$seed)

    $orders = Get-KpiValue $kpi "orders_completed"
    $ordersPerHour = Get-KpiValue $kpi "orders_per_hour"
    $distance = Get-KpiValue $kpi "total_distance_m"
    $orderDistance = Get-KpiValue $kpi "order_distance_m"
    $outputArrivals = Get-KpiValue $kpi "output_station_arrivals"
    $stationArrivals = Get-KpiValue $kpi "station_arrivals_total"
    $mechEnergy = Get-KpiValue $kpi "total_energy_mech_kJ"
    $waitTime = Get-KpiValue $kpi "wait_time_sec"

    $decisionPath = Join-Path $dir.FullName "m1g_objective_decision_trace.csv"
    $lambdaOrder = ""
    $lambdaCapacity = ""
    $avgUnusedCapacity = ""
    $avgEtaPerPod = ""
    $avgSelectedXps = ""
    if (Test-Path -LiteralPath $decisionPath) {
        $decision = Import-Csv -LiteralPath $decisionPath
        if ($decision.Count -gt 0) {
            $lambdaOrder = ($decision | Select-Object -First 1).lambda_order
            $lambdaCapacity = ($decision | Select-Object -First 1).lambda_capacity
            $avgUnusedCapacity = ($decision | Measure-Object -Property unused_capacity_sum -Average).Average
            $avgSelectedXps = ($decision | Measure-Object -Property selected_xps_count -Average).Average
            $etaSum = ($decision | Measure-Object -Property estimated_eta_sum -Sum).Sum
            $xpsSum = ($decision | Measure-Object -Property selected_xps_count -Sum).Sum
            $avgEtaPerPod = if ($xpsSum -gt 0) { $etaSum / $xpsSum } else { "" }
        }
    }

    $calibrationPath = Join-Path $dir.FullName "m1g_objective_calibration.csv"
    $fallbackSum = ""
    if (Test-Path -LiteralPath $calibrationPath) {
        $fallbackSum = (Import-Csv -LiteralPath $calibrationPath | Measure-Object -Property fallback_count -Sum).Sum
    }

    $duration = if ($ordersPerHour -gt 0) { 3600.0 * $orders / $ordersPerHour } else { 7200.0 }
    $rows.Add([pscustomobject]@{
        config_id = $configId
        seed = $seed
        orders_completed = $orders
        orders_per_hour = $ordersPerHour
        output_station_arrivals = $outputArrivals
        orders_per_output_arrival = if ($outputArrivals -gt 0) { $orders / $outputArrivals } else { "" }
        output_arrival_interval = if ($outputArrivals -gt 0) { $duration / $outputArrivals } else { "" }
        total_distance_m = $distance
        order_distance_m = $orderDistance
        station_arrivals_total = $stationArrivals
        total_energy_mech_kJ = $mechEnergy
        wait_time_sec = $waitTime
        lambda_order = $lambdaOrder
        lambda_capacity = $lambdaCapacity
        avg_unused_capacity = $avgUnusedCapacity
        avg_eta_per_selected_pod = $avgEtaPerPod
        avg_selected_xps = $avgSelectedXps
        fallback_count = $fallbackSum
        result_dir = $dir.FullName
    }) | Out-Null
}

$rows | Sort-Object config_id, seed | Export-Csv -NoTypeInformation -Path $OutCsv
Write-Host "Wrote run summary: $OutCsv"
