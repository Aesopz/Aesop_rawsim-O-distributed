param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$OutputRoot = "Results\m1g_time_vs_ppinclude_7200_20260510",
    [string]$Layout = "Material\Instances\CoreBenchmark\benchmark_ss_layout.xlayo",
    [string]$Setting = "Material\Instances\CoreBenchmark\benchmark_3600_sett.xsett",
    [string]$BaselineConfig = "Material\Instances\CoreBenchmark\m1g_time.xconf",
    [string]$Template = "Material\Instances\CoreBenchmark\m1gt_rank1.xconf",
    [int]$Seed = 42,
    [int]$TopK = 5,
    [int]$MaxDecisions = 824,
    [double]$LambdaOrder = 40,
    [double]$LambdaCapacity = 750,
    [switch]$SkipBuild
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $Root $path
}

function First-RunDir([string]$root) {
    $dirs = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object LastWriteTime -Descending)
    if ($dirs.Count -eq 0) { return "" }
    return $dirs[0].FullName
}

function Metric($rows, [string]$name) {
    $row = $rows | Where-Object { $_.metric -eq $name } | Select-Object -First 1
    if ($null -eq $row) { return "" }
    return $row.total
}

function D($value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { return 0.0 }
    return [double]::Parse([string]$value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function F($value, [int]$digits = 3) {
    return ([double]$value).ToString("F$digits", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Ensure-Baseline($baselineRoot, $cliExe, $layoutAbs, $settingAbs, $configAbs, [int]$seed) {
    $existing = First-RunDir $baselineRoot
    if (![string]::IsNullOrWhiteSpace($existing) -and (Test-Path -LiteralPath (Join-Path $existing "kpi_report.csv"))) {
        return $existing
    }

    New-Item -ItemType Directory -Force -Path $baselineRoot | Out-Null
    & $cliExe $layoutAbs $settingAbs $configAbs $baselineRoot ([string]$seed) "m1g-time-baseline-7200-seed$seed"
    if ($LASTEXITCODE -ne 0) { throw "Baseline run failed" }
    $run = First-RunDir $baselineRoot
    if ([string]::IsNullOrWhiteSpace($run)) { throw "Baseline produced no run directory" }
    return $run
}

function Write-Conclusion($baselineRun, $ppRun, $oracleRoot, $path) {
    $baseline = @(Import-Csv -LiteralPath (Join-Path $baselineRun "kpi_report.csv"))
    $pp = @(Import-Csv -LiteralPath (Join-Path $ppRun "kpi_report.csv"))
    $policyPath = Join-Path $oracleRoot "oracle_policy.csv"
    $tracePath = Join-Path $oracleRoot "oracle_decision_trace.csv"
    $policyCount = if (Test-Path -LiteralPath $policyPath) { @((Import-Csv -LiteralPath $policyPath)).Count } else { 0 }
    $trace = if (Test-Path -LiteralPath $tracePath) { @(Import-Csv -LiteralPath $tracePath) } else { @() }
    $flipCount = @($trace | Where-Object { [int]$_.chosen_rank -ne 1 }).Count

    $metrics = @(
        "orders_completed",
        "orders_per_hour",
        "system_order_pile_on",
        "order_distance_m",
        "total_distance_m",
        "station_arrivals_total",
        "output_station_arrivals",
        "wait_time_sec",
        "total_energy_with_support_kJ",
        "energy_total_with_support_per_order_kJ"
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# M1G-time vs M1G-time PP-Include 7200-Tick Comparison") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("## Runs") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("- Baseline run: ``$baselineRun``") | Out-Null
    $lines.Add("- PP-include final replay: ``$ppRun``") | Out-Null
    $lines.Add("- Oracle root: ``$oracleRoot``") | Out-Null
    $lines.Add("- Forced policy decisions: $policyCount") | Out-Null
    $lines.Add("- Oracle rank flips: $flipCount") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("## KPI") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| metric | m1g-time | m1g-time pp-include | delta | delta % |") | Out-Null
    $lines.Add("|---|---:|---:|---:|---:|") | Out-Null
    foreach ($metric in $metrics) {
        $b = D (Metric $baseline $metric)
        $p = D (Metric $pp $metric)
        $delta = $p - $b
        $pct = if ([Math]::Abs($b) -gt 0.0000001) { 100.0 * $delta / $b } else { 0.0 }
        $lines.Add("| $metric | $(F $b) | $(F $p) | $(F $delta) | $(F $pct 2)% |") | Out-Null
    }
    $lines.Add("") | Out-Null
    $lines.Add("## Interpretation") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("The PP-include line is the final 7200-tick replay driven by the rolling top-$TopK actual-cost policy. The oracle objective uses actual replay travel plus the original M1G-time order and station-capacity terms.") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("If forced policy decisions is lower than the baseline decision count, the run did not cover the full horizon and the comparison is partial.") | Out-Null
    $lines | Set-Content -Path $path -Encoding UTF8
}

$outputRootAbs = Resolve-RepoPath $OutputRoot
$baselineRoot = Join-Path $outputRootAbs "baseline_m1g_time"
$oracleRoot = Join-Path $outputRootAbs "ppinclude_rolling_oracle"
$layoutAbs = Resolve-RepoPath $Layout
$settingAbs = Resolve-RepoPath $Setting
$baselineConfigAbs = Resolve-RepoPath $BaselineConfig
$rollingScript = Join-Path $Root "Tools\M1GTReplay\Invoke-M1GTimeRollingOracle.ps1"
$cliExe = Join-Path $Root "RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe"
$conclusionPath = Join-Path $outputRootAbs "m1g_time_ppinclude_7200_conclusion.md"

New-Item -ItemType Directory -Force -Path $outputRootAbs | Out-Null
if (!$SkipBuild) {
    dotnet build (Join-Path $Root "RAWSimO.CLI\RAWSimO.CLI.csproj") -p:Platform=x64 -v:minimal
}
if (!(Test-Path -LiteralPath $cliExe)) { throw "CLI executable not found: $cliExe" }

$baselineRun = Ensure-Baseline $baselineRoot $cliExe $layoutAbs $settingAbs $baselineConfigAbs $Seed

& powershell -ExecutionPolicy Bypass -File $rollingScript `
    -Root $Root `
    -OutputRoot $oracleRoot `
    -Layout $Layout `
    -Setting $Setting `
    -Template $Template `
    -Seed $Seed `
    -TopK $TopK `
    -MaxDecisions $MaxDecisions `
    -LambdaOrder $LambdaOrder `
    -LambdaCapacity $LambdaCapacity `
    -RunFinalReplay `
    -SkipBuild
if ($LASTEXITCODE -ne 0) { throw "Rolling oracle failed" }

$finalRoot = Join-Path $oracleRoot "final_policy_replay"
$ppRun = First-RunDir $finalRoot
if ([string]::IsNullOrWhiteSpace($ppRun) -or !(Test-Path -LiteralPath (Join-Path $ppRun "kpi_report.csv"))) {
    throw "PP-include final replay KPI not found under $finalRoot"
}

Write-Conclusion $baselineRun $ppRun $oracleRoot $conclusionPath
Write-Host "Wrote conclusion: $conclusionPath"
