param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$OutputRoot = "Results\m1g_time_rolling_oracle",
    [string]$Layout = "Material\Instances\CoreBenchmark\benchmark_ss_layout.xlayo",
    [string]$Setting = "Material\Instances\CoreBenchmark\benchmark_3600_sett.xsett",
    [string]$Template = "Material\Instances\CoreBenchmark\m1gt_rank1.xconf",
    [int]$Seed = 42,
    [int]$TopK = 5,
    [int]$MaxDecisions = 20,
    [double]$LambdaOrder = 40,
    [double]$LambdaCapacity = 750,
    [switch]$RunFinalReplay,
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

function Write-PolicyCsv($policyRows, [string]$path) {
    $dir = Split-Path -Parent $path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    if ($policyRows.Count -eq 0) {
        "decision_id,candidate_rank" | Set-Content -Path $path -Encoding ASCII
    }
    else {
        $policyRows | Sort-Object decision_id | Export-Csv -NoTypeInformation -Path $path
    }
}

function Set-ReplayConfig([string]$source, [string]$target, [string]$name, [int]$validationDecisionId, [int]$rank, [string]$policyPath, [bool]$stopAfterBatch) {
    [xml]$doc = Get-Content -LiteralPath $source
    $doc.ControlConfiguration.Name = $name
    $ob = $doc.ControlConfiguration.OrderBatchingConfig
    $ob.Name = $name
    $ob.TopK = $TopK.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $ob.ValidationDecisionId = $validationDecisionId.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $ob.ValidationCandidateRank = $rank.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $ob.StopAfterValidationBatch = if ($stopAfterBatch) { "true" } else { "false" }
    $ob.UseShortestTimeObjective = "true"
    $ob.TimePerDistanceScale = "1"
    $ob.BaseOrderReward = (-1.0 * $LambdaOrder).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $ob.BaseUnusedCapacityPenalty = $LambdaCapacity.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    Set-XmlElementText $ob "ForcedDecisionPolicyPath" $policyPath
    $doc.Save($target)
}

function First-RunDir([string]$root) {
    $dirs = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object LastWriteTime -Descending)
    if ($dirs.Count -eq 0) { return "" }
    return $dirs[0].FullName
}

function D($value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { return 0.0 }
    return [double]::Parse([string]$value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Set-ContentWithRetry([string]$path, $lines) {
    $dir = Split-Path -Parent $path
    if (![string]::IsNullOrWhiteSpace($dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    $tmp = "$path.tmp"
    $lastError = $null
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            $lines | Set-Content -Path $tmp -Encoding UTF8
            Move-Item -LiteralPath $tmp -Destination $path -Force
            return
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds (200 * $attempt)
        }
    }
    if (Test-Path -LiteralPath $tmp) {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
    throw $lastError
}

function Write-Conclusion($traceRows, [string]$path, [string]$outputRootAbs) {
    $done = @()
    if ($null -ne $traceRows) {
        foreach ($row in $traceRows) {
            $done += $row
        }
    }
    $flipCount = @($done | Where-Object { [int]$_.chosen_rank -ne 1 }).Count
    $decisionCount = $done.Count
    $regretSum = ($done | ForEach-Object { D $_.rank1_regret } | Measure-Object -Sum).Sum
    $travelGainSum = ($done | ForEach-Object { D $_.rank1_actual_travel_gain } | Measure-Object -Sum).Sum
    $flipRate = if ($decisionCount -gt 0) {
        (100.0 * $flipCount / $decisionCount).ToString("F2", [System.Globalization.CultureInfo]::InvariantCulture)
    }
    else {
        "0.00"
    }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# M1G-time Rolling Top-5 Actual-Cost Oracle") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("## Summary") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("- Output root: ``$outputRootAbs``") | Out-Null
    $lines.Add("- Decisions completed: $decisionCount") | Out-Null
    $lines.Add("- Flip count: $flipCount") | Out-Null
    $lines.Add("- Flip rate: $flipRate%") | Out-Null
    $lines.Add("- Cumulative rank-1 regret: " + $regretSum.ToString("F3", [System.Globalization.CultureInfo]::InvariantCulture)) | Out-Null
    $lines.Add("- Cumulative travel gain: " + $travelGainSum.ToString("F3", [System.Globalization.CultureInfo]::InvariantCulture) + " sec") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("## Decisions") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| decision | chosen rank | rank1 actual obj | best actual obj | regret | rank1 travel | best travel | snapshot hashes |") | Out-Null
    $lines.Add("|---:|---:|---:|---:|---:|---:|---:|---:|") | Out-Null
    foreach ($row in $done) {
        $lines.Add("| $($row.decision_id) | $($row.chosen_rank) | $($row.rank1_actual_objective) | $($row.best_actual_objective) | $($row.rank1_regret) | $($row.rank1_actual_travel) | $($row.best_actual_travel) | $($row.snapshot_hash_count) |") | Out-Null
    }
    Set-ContentWithRetry $path $lines
}

$outputRootAbs = Resolve-RepoPath $OutputRoot
$layoutAbs = Resolve-RepoPath $Layout
$settingAbs = Resolve-RepoPath $Setting
$templateAbs = Resolve-RepoPath $Template
$configRoot = Join-Path $outputRootAbs "_generated_configs"
$summaryScript = Join-Path $Root "Tools\M1GTReplay\Summarize-M1GTimeTop5Replay.ps1"
$cliExe = Join-Path $Root "RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe"
New-Item -ItemType Directory -Force -Path $outputRootAbs | Out-Null
New-Item -ItemType Directory -Force -Path $configRoot | Out-Null

if (!$SkipBuild) {
    dotnet build (Join-Path $Root "RAWSimO.CLI\RAWSimO.CLI.csproj") -p:Platform=x64 -v:minimal
}
if (!(Test-Path -LiteralPath $cliExe)) { throw "CLI executable not found: $cliExe" }
if (!(Test-Path -LiteralPath $summaryScript)) { throw "Summary script not found: $summaryScript" }

$policy = New-Object System.Collections.Generic.List[object]
$trace = New-Object System.Collections.Generic.List[object]
$policyPath = Join-Path $outputRootAbs "oracle_policy.csv"
$tracePath = Join-Path $outputRootAbs "oracle_decision_trace.csv"
$conclusionPath = Join-Path $outputRootAbs "oracle_conclusion.md"
$startDecision = 1
if (Test-Path -LiteralPath $policyPath) {
    $existingPolicy = @(Import-Csv -LiteralPath $policyPath | Where-Object { $_.decision_id -ne "" -and $_.candidate_rank -ne "" })
    foreach ($row in $existingPolicy | Sort-Object {[int]$_.decision_id}) {
        $policy.Add([pscustomobject]@{
            decision_id = [int]$row.decision_id
            candidate_rank = [int]$row.candidate_rank
        }) | Out-Null
    }
    if ($policy.Count -gt 0) {
        $startDecision = (($policy | ForEach-Object { [int]$_.decision_id } | Measure-Object -Maximum).Maximum) + 1
    }
}
if (Test-Path -LiteralPath $tracePath) {
    $existingTrace = @(Import-Csv -LiteralPath $tracePath)
    foreach ($row in $existingTrace | Sort-Object {[int]$_.decision_id}) {
        $trace.Add($row) | Out-Null
    }
}
if ($startDecision -gt 1) {
    Write-Host "Resuming rolling oracle from decision $startDecision with $($policy.Count) forced policy rows."
}

for ($decision = $startDecision; $decision -le $MaxDecisions; $decision++) {
    Write-PolicyCsv $policy $policyPath
    $decisionRoot = Join-Path $outputRootAbs ("decision{0}" -f $decision)
    $decisionConfigRoot = Join-Path $configRoot ("decision{0}" -f $decision)
    New-Item -ItemType Directory -Force -Path $decisionRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $decisionConfigRoot | Out-Null

    $noMoreDecisions = $false
    foreach ($rank in 1..$TopK) {
        $cfg = Join-Path $decisionConfigRoot ("m1g-time-oracle-d{0}-r{1}.xconf" -f $decision, $rank)
        $name = "m1g-time-oracle-d$decision-r$rank"
        Set-ReplayConfig $templateAbs $cfg $name $decision $rank $policyPath $true
        $rankRoot = Join-Path $decisionRoot ("rank{0}" -f $rank)
        New-Item -ItemType Directory -Force -Path $rankRoot | Out-Null
        $tag = "RollingOracle-d$decision-r$rank-seed$Seed"
        Write-Host "[decision $decision/$MaxDecisions rank $rank/$TopK] $tag"
        & $cliExe $layoutAbs $settingAbs $cfg $rankRoot ([string]$Seed) $tag
        if ($LASTEXITCODE -ne 0) { throw "Replay failed for decision $decision rank $rank" }

        $runDir = First-RunDir $rankRoot
        if ([string]::IsNullOrWhiteSpace($runDir) -or !(Test-Path -LiteralPath (Join-Path $runDir "m1gt_selected_executable_candidate.csv"))) {
            $noMoreDecisions = $true
            break
        }
    }

    if ($noMoreDecisions) {
        Write-Host "No validation decision $decision reached; stopping rolling oracle."
        break
    }

    $decisionCsv = Join-Path $decisionRoot "decision_objective_comparison.csv"
    $decisionMd = Join-Path $decisionRoot "decision_conclusion.md"
    & powershell -ExecutionPolicy Bypass -File $summaryScript -Root $Root -OutputRoot $decisionRoot -OutCsv $decisionCsv -OutMd $decisionMd
    if ($LASTEXITCODE -ne 0) { throw "Decision summary failed for decision $decision" }

    $comparison = @(Import-Csv -LiteralPath $decisionCsv)
    $rank1 = $comparison | Where-Object { [int]$_.candidate_rank -eq 1 } | Select-Object -First 1
    $best = $comparison | Sort-Object {[double]$_.actual_objective_executable_direct}, {[int]$_.candidate_rank} | Select-Object -First 1
    if ($null -eq $rank1 -or $null -eq $best) { throw "Missing comparison rows for decision $decision" }

    $snapshotHashes = @(Get-ChildItem -LiteralPath $decisionRoot -Recurse -Filter m1gt_decision_snapshot_fingerprint.csv |
        ForEach-Object { Import-Csv -LiteralPath $_.FullName } |
        Select-Object -ExpandProperty snapshot_sha256 -Unique)
    if ($snapshotHashes.Count -ne 1) {
        throw "Decision $decision has $($snapshotHashes.Count) distinct snapshot hashes; refusing to extend oracle policy."
    }

    $chosenRank = [int]$best.candidate_rank
    $policy.Add([pscustomobject]@{ decision_id = $decision; candidate_rank = $chosenRank }) | Out-Null
    Write-PolicyCsv $policy $policyPath

    $regret = (D $rank1.actual_objective_executable_direct) - (D $best.actual_objective_executable_direct)
    $travelGain = (D $rank1.actual_travel_objective) - (D $best.actual_travel_objective)
    $trace.Add([pscustomobject]@{
        decision_id = $decision
        decision_time = $best.decision_time
        chosen_rank = $chosenRank
        rank1_actual_objective = (D $rank1.actual_objective_executable_direct).ToString("F6", [System.Globalization.CultureInfo]::InvariantCulture)
        best_actual_objective = (D $best.actual_objective_executable_direct).ToString("F6", [System.Globalization.CultureInfo]::InvariantCulture)
        rank1_regret = $regret.ToString("F6", [System.Globalization.CultureInfo]::InvariantCulture)
        rank1_actual_travel = (D $rank1.actual_travel_objective).ToString("F6", [System.Globalization.CultureInfo]::InvariantCulture)
        best_actual_travel = (D $best.actual_travel_objective).ToString("F6", [System.Globalization.CultureInfo]::InvariantCulture)
        rank1_actual_travel_gain = $travelGain.ToString("F6", [System.Globalization.CultureInfo]::InvariantCulture)
        snapshot_hash_count = $snapshotHashes.Count
        decision_root = $decisionRoot
    }) | Out-Null

    $trace | Export-Csv -NoTypeInformation -Path $tracePath
    Write-Conclusion $trace $conclusionPath $outputRootAbs
    Write-Host "Decision $decision chose rank $chosenRank; regret=$($regret.ToString('F3', [System.Globalization.CultureInfo]::InvariantCulture))"
}

if ($RunFinalReplay -and $policy.Count -gt 0) {
    $finalRoot = Join-Path $outputRootAbs "final_policy_replay"
    $finalCfg = Join-Path $configRoot "m1g-time-oracle-final.xconf"
    Set-ReplayConfig $templateAbs $finalCfg "m1g-time-oracle-final" 999999 1 $policyPath $false
    New-Item -ItemType Directory -Force -Path $finalRoot | Out-Null
    & $cliExe $layoutAbs $settingAbs $finalCfg $finalRoot ([string]$Seed) "RollingOracle-final-seed$Seed"
    if ($LASTEXITCODE -ne 0) { throw "Final policy replay failed" }
}

Write-Host "Wrote policy: $policyPath"
Write-Host "Wrote trace: $tracePath"
Write-Host "Wrote conclusion: $conclusionPath"
