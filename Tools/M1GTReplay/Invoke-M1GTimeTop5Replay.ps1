param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$OutputRoot = "Results\m1g_time_top5_replay_sweet",
    [string]$Layout = "Material\Instances\CoreBenchmark\benchmark_ss_layout.xlayo",
    [string]$Setting = "Material\Instances\CoreBenchmark\benchmark_3600_sett.xsett",
    [string]$Template = "Material\Instances\CoreBenchmark\m1gt_rank1.xconf",
    [int]$Seed = 42,
    [int]$TopK = 5,
    [int]$ValidationDecisionId = 1,
    [double]$LambdaOrder = 40,
    [double]$LambdaCapacity = 750,
    [string]$ForcedDecisionPolicyPath = "",
    [switch]$SkipBuild
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $Root $path
}

function Set-XmlElementText($parent, [string]$name, [string]$value) {
    $node = $parent.SelectSingleNode($name)
    if ($null -eq $node) {
        $node = $parent.OwnerDocument.CreateElement($name)
        $parent.AppendChild($node) | Out-Null
    }
    $node.InnerText = $value
}

function Set-ReplayConfig([string]$source, [string]$target, [int]$rank) {
    [xml]$doc = Get-Content -LiteralPath $source
    $name = "m1g-time-r$rank"
    $doc.ControlConfiguration.Name = $name
    $ob = $doc.ControlConfiguration.OrderBatchingConfig
    $ob.Name = $name
    $ob.TopK = $TopK.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $ob.ValidationDecisionId = $ValidationDecisionId.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $ob.ValidationCandidateRank = $rank.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $ob.StopAfterValidationBatch = "true"
    $ob.UseShortestTimeObjective = "true"
    $ob.TimePerDistanceScale = "1"
    $ob.BaseOrderReward = (-1.0 * $LambdaOrder).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $ob.BaseUnusedCapacityPenalty = $LambdaCapacity.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    Set-XmlElementText $ob "ForcedDecisionPolicyPath" $ForcedDecisionPolicyPath
    $doc.Save($target)
}

$outputRootAbs = Resolve-RepoPath $OutputRoot
$configDir = Join-Path $outputRootAbs "_generated_configs"
New-Item -ItemType Directory -Force -Path $configDir | Out-Null

$layoutAbs = Resolve-RepoPath $Layout
$settingAbs = Resolve-RepoPath $Setting
$templateAbs = Resolve-RepoPath $Template
$cliExe = Join-Path $Root "RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe"

if (!$SkipBuild) {
    dotnet build (Join-Path $Root "RAWSimO.CLI\RAWSimO.CLI.csproj") -p:Platform=x64 -v:minimal
}
if (!(Test-Path -LiteralPath $cliExe)) {
    throw "CLI executable not found: $cliExe"
}

$manifestRows = New-Object System.Collections.Generic.List[object]
foreach ($rank in 1..$TopK) {
    $cfg = Join-Path $configDir ("m1g-time-r{0}.xconf" -f $rank)
    Set-ReplayConfig $templateAbs $cfg $rank
    $rankOutput = Join-Path $outputRootAbs ("rank{0}" -f $rank)
    New-Item -ItemType Directory -Force -Path $rankOutput | Out-Null
    $tag = "Top5Replay-m1g-time-r$rank-seed$Seed"
    $manifestRows.Add([pscustomobject]@{
        rank = $rank
        seed = $Seed
        config_path = $cfg
        output_root = $rankOutput
        tag = $tag
        lambda_order = $LambdaOrder
        lambda_capacity = $LambdaCapacity
        validation_decision_id = $ValidationDecisionId
    }) | Out-Null
    Write-Host "[$rank/$TopK] replay rank $rank"
    & $cliExe $layoutAbs $settingAbs $cfg $rankOutput ([string]$Seed) $tag
    if ($LASTEXITCODE -ne 0) {
        throw "Replay failed for rank $rank"
    }
}

$manifestPath = Join-Path $outputRootAbs "m1g_time_top5_replay_manifest.csv"
$manifestRows | Export-Csv -NoTypeInformation -Path $manifestPath
Write-Host "Wrote manifest: $manifestPath"
