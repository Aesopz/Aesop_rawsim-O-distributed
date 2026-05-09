param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$OutputRoot = "Results\m1g_time_parameter_search",
    [switch]$SkipBuild,
    [switch]$GenerateOnly,
    [int]$MaxRunsPerPhase = 0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $Root $path
}

$toolDir = $PSScriptRoot
$outputRootAbs = Resolve-RepoPath $OutputRoot
New-Item -ItemType Directory -Force -Path $outputRootAbs | Out-Null

$phase1Candidates = Join-Path $outputRootAbs "phase1_pareto_candidates.csv"
$phase2Candidates = Join-Path $outputRootAbs "phase2_pareto_candidates.csv"
$summaryCsv = Join-Path $outputRootAbs "run_summary.csv"
$aggregateCsv = Join-Path $outputRootAbs "config_aggregate.csv"
$conclusionMd = Join-Path $outputRootAbs "m1g_time_parameter_search_conclusion.md"

if (!$SkipBuild -and !$GenerateOnly) {
    dotnet build (Join-Path $Root "RAWSimO.CLI\RAWSimO.CLI.csproj") -p:Platform=x64 -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

$common = @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $toolDir "Invoke-M1GTimeParameterSearch.ps1"), "-Root", $Root, "-OutputRoot", $OutputRoot)
$common += "-SkipBuild"
if ($GenerateOnly) { $common += "-GenerateOnly" }
if ($MaxRunsPerPhase -gt 0) { $common += @("-MaxRuns", $MaxRunsPerPhase) }

Write-Host "=== Phase0 anchors ==="
& powershell @common -Phase Phase0
if ($LASTEXITCODE -ne 0) { throw "Phase0 failed" }

Write-Host "=== Phase1 coarse grid ==="
& powershell @common -Phase Phase1
if ($LASTEXITCODE -ne 0) { throw "Phase1 failed" }

if (!$GenerateOnly) {
    Write-Host "=== Summarize after Phase1 ==="
    & powershell -ExecutionPolicy Bypass -File (Join-Path $toolDir "Summarize-M1GTimeParameterSearch.ps1") -Root $Root -OutputRoot $OutputRoot -OutCsv $summaryCsv
    if ($LASTEXITCODE -ne 0) { throw "Phase1 summarize failed" }
    & powershell -ExecutionPolicy Bypass -File (Join-Path $toolDir "Select-M1GTimeParetoCandidates.ps1") -Root $Root -RunSummaryCsv $summaryCsv -AggregateCsv $aggregateCsv -CandidateCsv $phase1Candidates
    if ($LASTEXITCODE -ne 0) { throw "Phase1 candidate selection failed" }
}

Write-Host "=== Phase2 local refinement ==="
if ($GenerateOnly -and !(Test-Path -LiteralPath $phase1Candidates)) {
    Write-Host "GenerateOnly cannot derive Phase2 candidates without completed Phase1 data; stopping after Phase1 manifests."
    return
}
& powershell @common -Phase Phase2 -CandidateCsv $phase1Candidates
if ($LASTEXITCODE -ne 0) { throw "Phase2 failed" }

if (!$GenerateOnly) {
    Write-Host "=== Summarize after Phase2 ==="
    & powershell -ExecutionPolicy Bypass -File (Join-Path $toolDir "Summarize-M1GTimeParameterSearch.ps1") -Root $Root -OutputRoot $OutputRoot -OutCsv $summaryCsv
    if ($LASTEXITCODE -ne 0) { throw "Phase2 summarize failed" }
    & powershell -ExecutionPolicy Bypass -File (Join-Path $toolDir "Select-M1GTimeParetoCandidates.ps1") -Root $Root -RunSummaryCsv $summaryCsv -AggregateCsv $aggregateCsv -CandidateCsv $phase2Candidates
    if ($LASTEXITCODE -ne 0) { throw "Phase2 candidate selection failed" }
}

Write-Host "=== Phase3 holdout confirmation ==="
if ($GenerateOnly -and !(Test-Path -LiteralPath $phase2Candidates)) {
    Write-Host "GenerateOnly cannot derive Phase3 candidates without completed Phase2 data; stopping after Phase2 manifests."
    return
}
& powershell @common -Phase Phase3 -CandidateCsv $phase2Candidates
if ($LASTEXITCODE -ne 0) { throw "Phase3 failed" }

if (!$GenerateOnly) {
    Write-Host "=== Final summarize and conclusion ==="
    & powershell -ExecutionPolicy Bypass -File (Join-Path $toolDir "Summarize-M1GTimeParameterSearch.ps1") -Root $Root -OutputRoot $OutputRoot -OutCsv $summaryCsv
    if ($LASTEXITCODE -ne 0) { throw "Final summarize failed" }
    & powershell -ExecutionPolicy Bypass -File (Join-Path $toolDir "Select-M1GTimeParetoCandidates.ps1") -Root $Root -RunSummaryCsv $summaryCsv -AggregateCsv $aggregateCsv -CandidateCsv $phase2Candidates
    if ($LASTEXITCODE -ne 0) { throw "Final candidate selection failed" }
    & powershell -ExecutionPolicy Bypass -File (Join-Path $toolDir "Write-M1GTimeExperimentConclusion.ps1") -Root $Root -OutputRoot $OutputRoot -RunSummaryCsv $summaryCsv -AggregateCsv $aggregateCsv -OutMd $conclusionMd
    if ($LASTEXITCODE -ne 0) { throw "Conclusion generation failed" }
}
