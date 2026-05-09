param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$OutputRoot = "Results\m1g_time_parameter_search",
    [string]$Layout = "Material\Instances\CoreBenchmark\benchmark_ss_layout.xlayo",
    [string]$Setting = "Material\Instances\CoreBenchmark\benchmark_3600_sett.xsett",
    [string]$TimeTemplate = "Material\Instances\CoreBenchmark\m1g_time.xconf",
    [string]$DistanceConfig = "Material\Instances\CoreBenchmark\m1g.xconf",
    [ValidateSet("Phase0","Phase1","Phase2","Phase3","Smoke")]
    [string]$Phase = "Smoke",
    [string]$CandidateCsv = "",
    [int]$MaxRuns = 0,
    [switch]$SkipBuild,
    [switch]$GenerateOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $Root $path
}

function Set-M1GTimeConfig([string]$source, [string]$target, [string]$name, [double]$lambdaOrder, [double]$lambdaCapacity) {
    [xml]$doc = Get-Content -LiteralPath $source
    $doc.ControlConfiguration.Name = $name
    $ob = $doc.ControlConfiguration.OrderBatchingConfig
    $ob.Name = $name
    $ob.UseShortestTimeObjective = "true"
    $ob.TimePerDistanceScale = "1"
    $ob.BaseOrderReward = (-1.0 * $lambdaOrder).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $ob.BaseUnusedCapacityPenalty = $lambdaCapacity.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $doc.Save($target)
}

function Add-Run($runs, [string]$phaseName, [string]$configId, [string]$configPath, [int[]]$seeds, $lambdaOrder, $lambdaCapacity) {
    foreach ($seed in $seeds) {
        $runs.Add([pscustomobject]@{
            phase = $phaseName
            config_id = $configId
            config_path = $configPath
            seed = $seed
            lambda_order = if ($null -ne $lambdaOrder) { $lambdaOrder } else { "" }
            lambda_capacity = if ($null -ne $lambdaCapacity) { $lambdaCapacity } else { "" }
        }) | Out-Null
    }
}

function New-NeighborValues([double]$value) {
    $values = New-Object System.Collections.Generic.List[double]
    $values.Add($value)
    $values.Add($value * 0.75)
    $values.Add($value * 1.5)
    return $values | Sort-Object -Unique
}

$outputRootAbs = Resolve-RepoPath $OutputRoot
$generatedConfigDir = Join-Path $outputRootAbs "_generated_configs"
New-Item -ItemType Directory -Force -Path $generatedConfigDir | Out-Null

$layoutAbs = Resolve-RepoPath $Layout
$settingAbs = Resolve-RepoPath $Setting
$timeTemplateAbs = Resolve-RepoPath $TimeTemplate
$distanceConfigAbs = Resolve-RepoPath $DistanceConfig
$cliExe = Join-Path $Root "RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe"

if (!$SkipBuild) {
    dotnet build (Join-Path $Root "RAWSimO.CLI\RAWSimO.CLI.csproj") -p:Platform=x64 -v:minimal
}
if (!(Test-Path -LiteralPath $cliExe)) {
    throw "CLI executable not found: $cliExe"
}

$runs = New-Object System.Collections.Generic.List[object]

if ($Phase -eq "Smoke") {
    $cfg = Join-Path $generatedConfigDir "m1g-time-o80-c250.xconf"
    Set-M1GTimeConfig $timeTemplateAbs $cfg "m1g-time-o80-c250" 80 250
    Add-Run $runs "Smoke" "m1g-time-o80-c250" $cfg @(42) 80 250
}
elseif ($Phase -eq "Phase0") {
    Add-Run $runs "Phase0" "m1g-distance" $distanceConfigAbs @(42,43,44,45,46) $null $null
    Add-Run $runs "Phase0" "m1g-time-paper-scaled" $timeTemplateAbs @(42,43,44,45,46) $null $null
}
elseif ($Phase -eq "Phase1") {
    foreach ($order in @(40,80,160,320,640)) {
        foreach ($cap in @(100,250,500,1000)) {
            $id = "m1g-time-p1-o$order-c$cap"
            $cfg = Join-Path $generatedConfigDir "$id.xconf"
            Set-M1GTimeConfig $timeTemplateAbs $cfg $id $order $cap
            Add-Run $runs "Phase1" $id $cfg @(42,43,44) $order $cap
        }
    }
}
elseif ($Phase -eq "Phase2") {
    if ([string]::IsNullOrWhiteSpace($CandidateCsv) -or !(Test-Path -LiteralPath (Resolve-RepoPath $CandidateCsv))) {
        throw "Phase2 requires -CandidateCsv from Select-M1GTimeParetoCandidates.ps1"
    }
    $candidateRows = Import-Csv -LiteralPath (Resolve-RepoPath $CandidateCsv) | Select-Object -First 8
    $seen = @{}
    foreach ($row in $candidateRows) {
        foreach ($order in (New-NeighborValues ([double]$row.lambda_order))) {
            foreach ($cap in (New-NeighborValues ([double]$row.lambda_capacity))) {
                $orderRounded = [math]::Round($order, 6)
                $capRounded = [math]::Round($cap, 6)
                $key = "$orderRounded|$capRounded"
                if ($seen.ContainsKey($key)) { continue }
                $seen[$key] = $true
                if ($seen.Count -gt 12) { continue }
                $id = "m1g-time-p2-o$($orderRounded)-c$($capRounded)".Replace(".","p")
                $cfg = Join-Path $generatedConfigDir "$id.xconf"
                Set-M1GTimeConfig $timeTemplateAbs $cfg $id $orderRounded $capRounded
                Add-Run $runs "Phase2" $id $cfg @(42,43,44,45,46) $orderRounded $capRounded
            }
        }
    }
}
elseif ($Phase -eq "Phase3") {
    if ([string]::IsNullOrWhiteSpace($CandidateCsv) -or !(Test-Path -LiteralPath (Resolve-RepoPath $CandidateCsv))) {
        throw "Phase3 requires -CandidateCsv with the final candidate rows"
    }
    Add-Run $runs "Phase3" "m1g-distance" $distanceConfigAbs @(47,48,49,50,51) $null $null
    Add-Run $runs "Phase3" "m1g-time-paper-scaled" $timeTemplateAbs @(47,48,49,50,51) $null $null
    foreach ($row in (Import-Csv -LiteralPath (Resolve-RepoPath $CandidateCsv) | Select-Object -First 2)) {
        $order = [double]$row.lambda_order
        $cap = [double]$row.lambda_capacity
        $id = "m1g-time-p3-o$($order)-c$($cap)".Replace(".","p")
        $cfg = Join-Path $generatedConfigDir "$id.xconf"
        Set-M1GTimeConfig $timeTemplateAbs $cfg $id $order $cap
        Add-Run $runs "Phase3" $id $cfg @(47,48,49,50,51) $order $cap
    }
}

if ($MaxRuns -gt 0) {
    $limitedRuns = New-Object System.Collections.Generic.List[object]
    foreach ($run in ($runs | Select-Object -First $MaxRuns)) {
        $limitedRuns.Add($run) | Out-Null
    }
    $runs = $limitedRuns
}

$manifest = Join-Path $outputRootAbs "manifest_$Phase.csv"
$runs | Export-Csv -NoTypeInformation -Path $manifest
Write-Host "Wrote manifest: $manifest"
if ($GenerateOnly) {
    Write-Host "GenerateOnly set; no simulations executed."
    return
}

$index = 0
foreach ($run in $runs) {
    $index++
    $tag = "$($run.phase)-$($run.config_id)-seed$($run.seed)"
    Write-Host "[$index/$($runs.Count)] $tag"
    & $cliExe $layoutAbs $settingAbs $run.config_path $outputRootAbs ([string]$run.seed) $tag
    if ($LASTEXITCODE -ne 0) {
        throw "Run failed: $tag"
    }
}
