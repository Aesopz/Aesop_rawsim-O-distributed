param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$RunSummaryCsv = "Results\m1g_time_parameter_search\run_summary.csv",
    [string]$AggregateCsv = "",
    [string]$CandidateCsv = "",
    [int]$MaxCandidates = 8
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $Root $path
}

function Mean($values) {
    $nums = @($values | Where-Object { $_ -ne "" -and $_ -ne $null } | ForEach-Object { [double]$_ })
    if ($nums.Count -eq 0) { return [double]::NaN }
    return ($nums | Measure-Object -Average).Average
}

function Is-Dominated($row, $others) {
    foreach ($other in $others) {
        if ($other.config_id -eq $row.config_id) { continue }
        $noWorse =
            ([double]$other.mean_tp -ge [double]$row.mean_tp) -and
            ([double]$other.mean_po -ge [double]$row.mean_po) -and
            ([double]$other.mean_od -le [double]$row.mean_od) -and
            ([double]$other.mean_rd -le [double]$row.mean_rd)
        $strictBetter =
            ([double]$other.mean_tp -gt [double]$row.mean_tp) -or
            ([double]$other.mean_po -gt [double]$row.mean_po) -or
            ([double]$other.mean_od -lt [double]$row.mean_od) -or
            ([double]$other.mean_rd -lt [double]$row.mean_rd)
        if ($noWorse -and $strictBetter) { return $true }
    }
    return $false
}

$runSummaryPath = Resolve-RepoPath $RunSummaryCsv
if ([string]::IsNullOrWhiteSpace($AggregateCsv)) {
    $AggregateCsv = Join-Path (Split-Path $runSummaryPath -Parent) "config_aggregate.csv"
}
elseif (![System.IO.Path]::IsPathRooted($AggregateCsv)) {
    $AggregateCsv = Join-Path $Root $AggregateCsv
}
if ([string]::IsNullOrWhiteSpace($CandidateCsv)) {
    $CandidateCsv = Join-Path (Split-Path $runSummaryPath -Parent) "pareto_candidates.csv"
}
elseif (![System.IO.Path]::IsPathRooted($CandidateCsv)) {
    $CandidateCsv = Join-Path $Root $CandidateCsv
}

$rows = Import-Csv -LiteralPath $runSummaryPath
$aggregates = New-Object System.Collections.Generic.List[object]
foreach ($group in ($rows | Group-Object config_id)) {
    $g = @($group.Group)
    $lambdaOrder = ($g | Where-Object { $_.lambda_order -ne "" } | Select-Object -First 1).lambda_order
    $lambdaCapacity = ($g | Where-Object { $_.lambda_capacity -ne "" } | Select-Object -First 1).lambda_capacity
    $aggregates.Add([pscustomobject]@{
        config_id = $group.Name
        runs = $g.Count
        lambda_order = $lambdaOrder
        lambda_capacity = $lambdaCapacity
        mean_tp = Mean ($g | ForEach-Object { $_.orders_per_hour })
        mean_orders = Mean ($g | ForEach-Object { $_.orders_completed })
        mean_po = Mean ($g | ForEach-Object { $_.orders_per_output_arrival })
        mean_od = Mean ($g | ForEach-Object { $_.order_distance_m })
        mean_rd = Mean ($g | ForEach-Object { $_.total_distance_m })
        mean_output_arrival_interval = Mean ($g | ForEach-Object { $_.output_arrival_interval })
        mean_unused_capacity = Mean ($g | ForEach-Object { $_.avg_unused_capacity })
        mean_eta_per_selected_pod = Mean ($g | ForEach-Object { $_.avg_eta_per_selected_pod })
        fallback_sum = ($g | Where-Object { $_.fallback_count -ne "" } | Measure-Object -Property fallback_count -Sum).Sum
    }) | Out-Null
}

$aggregateRows = @($aggregates | Sort-Object -Property @{Expression="mean_tp";Descending=$true}, @{Expression="mean_od";Descending=$false})
$aggregateRows | Export-Csv -NoTypeInformation -Path $AggregateCsv

$tunedRows = @($aggregateRows | Where-Object {
    $_.lambda_order -ne "" -and $_.lambda_capacity -ne "" -and
    -not ($_.config_id -match "paper-scaled")
})
$pareto = @($tunedRows | Where-Object { -not (Is-Dominated $_ $tunedRows) })
$selected = @($pareto | Sort-Object -Property @{Expression="mean_tp";Descending=$true}, @{Expression="mean_po";Descending=$true}, @{Expression="mean_od";Descending=$false}, @{Expression="mean_rd";Descending=$false} | Select-Object -First $MaxCandidates)
$selected | Export-Csv -NoTypeInformation -Path $CandidateCsv

Write-Host "Wrote aggregate: $AggregateCsv"
Write-Host "Wrote Pareto candidates: $CandidateCsv"
