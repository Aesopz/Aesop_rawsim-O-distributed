param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$OutputRoot = "Results\m1g_time_parameter_search",
    [string]$RunSummaryCsv = "",
    [string]$AggregateCsv = "",
    [string]$OutMd = "",
    [string]$Title = "M1G-time Parameter Search Conclusion"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $Root $path
}

function Fmt($value, [int]$digits = 3) {
    if ($null -eq $value -or $value -eq "") { return "" }
    $d = [double]$value
    return [math]::Round($d, $digits).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

function PercentDelta($candidate, $baseline, [int]$digits = 2) {
    if ($null -eq $candidate -or $null -eq $baseline -or $baseline -eq "" -or [double]$baseline -eq 0.0) { return "" }
    return (Fmt (100.0 * (([double]$candidate - [double]$baseline) / [double]$baseline)) $digits) + "%"
}

function BetterOrEqual($candidate, $baseline, [string]$direction) {
    if ($null -eq $candidate -or $null -eq $baseline -or $candidate -eq "" -or $baseline -eq "") { return $false }
    if ($direction -eq "max") { return [double]$candidate -ge [double]$baseline }
    return [double]$candidate -le [double]$baseline
}

$outputRootAbs = Resolve-RepoPath $OutputRoot
if ([string]::IsNullOrWhiteSpace($RunSummaryCsv)) { $RunSummaryCsv = Join-Path $outputRootAbs "run_summary.csv" } else { $RunSummaryCsv = Resolve-RepoPath $RunSummaryCsv }
if ([string]::IsNullOrWhiteSpace($AggregateCsv)) { $AggregateCsv = Join-Path $outputRootAbs "config_aggregate.csv" } else { $AggregateCsv = Resolve-RepoPath $AggregateCsv }
if ([string]::IsNullOrWhiteSpace($OutMd)) { $OutMd = Join-Path $outputRootAbs "m1g_time_parameter_search_conclusion.md" } else { $OutMd = Resolve-RepoPath $OutMd }

if (!(Test-Path -LiteralPath $RunSummaryCsv)) { throw "Missing run summary: $RunSummaryCsv" }
if (!(Test-Path -LiteralPath $AggregateCsv)) { throw "Missing aggregate summary: $AggregateCsv" }

$runRows = @(Import-Csv -LiteralPath $RunSummaryCsv)
$aggRows = @(Import-Csv -LiteralPath $AggregateCsv)
$baseline = $aggRows | Where-Object { $_.config_id -eq "m1g-distance" -or $_.config_id -eq "m1g" } | Sort-Object config_id | Select-Object -First 1
$paperScaled = $aggRows | Where-Object { $_.config_id -eq "m1g-time-paper-scaled" } | Select-Object -First 1
if ($null -eq $paperScaled) {
    $paperScaled = $aggRows | Where-Object { $_.config_id -eq "m1g-time" } | Select-Object -First 1
}
$tunedRows = @($aggRows | Where-Object {
    $_.lambda_order -ne "" -and $_.lambda_capacity -ne "" -and
    $_.config_id -ne "m1g" -and $_.config_id -ne "m1g-distance" -and
    $_.config_id -ne "m1g-time" -and $_.config_id -ne "m1g-time-paper-scaled"
})

if ($null -eq $baseline) {
    $baseline = $aggRows | Sort-Object -Property @{Expression="mean_tp";Descending=$true}, @{Expression="mean_od";Descending=$false} | Select-Object -First 1
}

$eligible = @($tunedRows | Where-Object {
    (BetterOrEqual $_.mean_tp $baseline.mean_tp "max") -and
    ([double]$_.mean_po -ge 0.99 * [double]$baseline.mean_po) -and
    (BetterOrEqual $_.mean_od $baseline.mean_od "min") -and
    (BetterOrEqual $_.mean_rd $baseline.mean_rd "min") -and
    ($_.fallback_sum -eq "" -or [double]$_.fallback_sum -eq 0.0)
})

if ($eligible.Count -gt 0) {
    $best = $eligible | Sort-Object -Property @{Expression="mean_od";Descending=$false}, @{Expression="mean_rd";Descending=$false}, @{Expression="mean_tp";Descending=$true} | Select-Object -First 1
    $selectionRule = "Strict Pareto filter: TP >= baseline, PO >= 99% baseline, OD <= baseline, RD <= baseline, fallback = 0."
}
else {
    $best = $tunedRows | Sort-Object -Property @{Expression="mean_tp";Descending=$true}, @{Expression="mean_po";Descending=$true}, @{Expression="mean_od";Descending=$false}, @{Expression="mean_rd";Descending=$false} | Select-Object -First 1
    $selectionRule = "Fallback ranking: no tuned config satisfied the strict filter, so choose highest TP, then highest PO, then lowest OD/RD."
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# $Title") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("## Experiment Coverage") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("- Run summary: ``$RunSummaryCsv``") | Out-Null
$lines.Add("- Aggregate summary: ``$AggregateCsv``") | Out-Null
$lines.Add("- Total completed runs parsed: $($runRows.Count)") | Out-Null
$lines.Add("- Total configs parsed: $($aggRows.Count)") | Out-Null
$lines.Add("- Tuned configs parsed: $($tunedRows.Count)") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("## Selection Rule") | Out-Null
$lines.Add("") | Out-Null
$lines.Add($selectionRule) | Out-Null
$lines.Add("") | Out-Null

if ($null -ne $baseline) {
    $lines.Add("## Baseline") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| config | runs | TP | PO | OD | RD | fallback |") | Out-Null
    $lines.Add("|---|---:|---:|---:|---:|---:|---:|") | Out-Null
    $lines.Add("| $($baseline.config_id) | $($baseline.runs) | $(Fmt $baseline.mean_tp) | $(Fmt $baseline.mean_po) | $(Fmt $baseline.mean_od) | $(Fmt $baseline.mean_rd 1) | $(Fmt $baseline.fallback_sum 0) |") | Out-Null
    if ($null -ne $paperScaled) {
        $lines.Add("| $($paperScaled.config_id) | $($paperScaled.runs) | $(Fmt $paperScaled.mean_tp) | $(Fmt $paperScaled.mean_po) | $(Fmt $paperScaled.mean_od) | $(Fmt $paperScaled.mean_rd 1) | $(Fmt $paperScaled.fallback_sum 0) |") | Out-Null
    }
    $lines.Add("") | Out-Null
}

if ($null -ne $best) {
    $lines.Add("## Best Candidate") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| config | lambda_order | lambda_capacity | runs | TP | PO | OD | RD | fallback |") | Out-Null
    $lines.Add("|---|---:|---:|---:|---:|---:|---:|---:|---:|") | Out-Null
    $lines.Add("| $($best.config_id) | $($best.lambda_order) | $($best.lambda_capacity) | $($best.runs) | $(Fmt $best.mean_tp) | $(Fmt $best.mean_po) | $(Fmt $best.mean_od) | $(Fmt $best.mean_rd 1) | $(Fmt $best.fallback_sum 0) |") | Out-Null
    $lines.Add("") | Out-Null
    if ($null -ne $baseline) {
        $lines.Add("Delta vs baseline: TP $(PercentDelta $best.mean_tp $baseline.mean_tp), PO $(PercentDelta $best.mean_po $baseline.mean_po), OD $(PercentDelta $best.mean_od $baseline.mean_od), RD $(PercentDelta $best.mean_rd $baseline.mean_rd).") | Out-Null
        $lines.Add("") | Out-Null
    }
}

$topRows = @($tunedRows | Sort-Object -Property @{Expression="mean_tp";Descending=$true}, @{Expression="mean_po";Descending=$true}, @{Expression="mean_od";Descending=$false} | Select-Object -First 10)
if ($topRows.Count -gt 0) {
    $lines.Add("## Top Tuned Configs") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| config | lambda_order | lambda_capacity | runs | TP | PO | OD | RD | unused cap | eta/pod |") | Out-Null
    $lines.Add("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|") | Out-Null
    foreach ($row in $topRows) {
        $lines.Add("| $($row.config_id) | $($row.lambda_order) | $($row.lambda_capacity) | $($row.runs) | $(Fmt $row.mean_tp) | $(Fmt $row.mean_po) | $(Fmt $row.mean_od) | $(Fmt $row.mean_rd 1) | $(Fmt $row.mean_unused_capacity) | $(Fmt $row.mean_eta_per_selected_pod) |") | Out-Null
    }
    $lines.Add("") | Out-Null
}

$lines.Add("## Interpretation Checklist") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("- If TP is flat but PO drops and output arrivals rise, ETA is probably over-prioritized relative to order density.") | Out-Null
$lines.Add("- If TP drops while unused capacity rises, lambda_capacity is too weak.") | Out-Null
$lines.Add("- If TP rises but OD/RD also rise, the config is buying throughput with extra movement.") | Out-Null
$lines.Add("- A strong candidate should improve or preserve TP/PO while reducing OD/RD, with fallback_count near zero.") | Out-Null
$lines.Add("") | Out-Null

Set-Content -LiteralPath $OutMd -Value $lines -Encoding UTF8
Write-Host "Wrote conclusion: $OutMd"
