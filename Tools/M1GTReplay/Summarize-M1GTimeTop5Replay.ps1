param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$OutputRoot = "Results\m1g_time_top5_replay_sweet",
    [string]$OutCsv = "",
    [string]$OutMd = ""
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $Root $path
}

function D($value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { return 0.0 }
    return [double]::Parse([string]$value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function F($value, [int]$digits = 3) {
    if ($null -eq $value) { return "" }
    return ([double]$value).ToString("F$digits", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Field($row, [string]$name, $default = "") {
    if ($null -eq $row) { return $default }
    $prop = $row.PSObject.Properties[$name]
    if ($null -eq $prop) { return $default }
    return $prop.Value
}

function Split-Symbols([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return @() }
    return @($value.Split('|') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Expected-TransportKeys($selected) {
    $podToStation = @{}
    foreach ($xps in (Split-Symbols (Field $selected "executable_xps"))) {
        $parts = $xps.Split('_')
        if ($parts.Length -ge 3) { $podToStation[$parts[1]] = $parts[2] }
    }

    $keys = @{}
    foreach ($yrp in (Split-Symbols (Field $selected "executable_yrp"))) {
        $parts = $yrp.Split('_')
        if ($parts.Length -lt 3) { continue }
        $bot = $parts[1]
        $pod = $parts[2]
        if ($podToStation.ContainsKey($pod)) {
            $keys["$bot|$pod|$($podToStation[$pod])"] = $true
        }
    }
    return $keys
}

function First-RunDir([string]$rankRoot) {
    $dirs = @(Get-ChildItem -LiteralPath $rankRoot -Directory | Sort-Object LastWriteTime -Descending)
    if ($dirs.Count -eq 0) { throw "No run directory under $rankRoot" }
    return $dirs[0].FullName
}

$outputRootAbs = Resolve-RepoPath $OutputRoot
if ([string]::IsNullOrWhiteSpace($OutCsv)) {
    $OutCsv = Join-Path $outputRootAbs "m1g_time_top5_replay_objective_comparison.csv"
}
if ([string]::IsNullOrWhiteSpace($OutMd)) {
    $OutMd = Join-Path $outputRootAbs "m1g_time_top5_replay_conclusion.md"
}

$rows = New-Object System.Collections.Generic.List[object]
$snapshots = New-Object System.Collections.Generic.List[object]

foreach ($rankDir in (Get-ChildItem -LiteralPath $outputRootAbs -Directory | Where-Object { $_.Name -match '^rank\d+$' } | Sort-Object Name)) {
    $runDir = First-RunDir $rankDir.FullName
    $selectedPath = Join-Path $runDir "m1gt_selected_executable_candidate.csv"
    $segmentsPath = Join-Path $runDir "m1g_path_estimate_actual_segments.csv"
    $manifestPath = Join-Path $runDir "m1gt_top5_candidates.csv"
    $snapshotPath = Join-Path $runDir "m1gt_decision_snapshot_fingerprint.csv"
    if (!(Test-Path -LiteralPath $selectedPath) -or !(Test-Path -LiteralPath $segmentsPath) -or !(Test-Path -LiteralPath $manifestPath)) {
        throw "Missing replay artifacts in $runDir"
    }

    $selected = Import-Csv -LiteralPath $selectedPath | Select-Object -Last 1
    $decisionId = [int](Field $selected "decision_id")
    $decisionTime = D (Field $selected "decision_time")
    $rank = [int]$selected.candidate_rank
    $manifest = Import-Csv -LiteralPath $manifestPath | Where-Object { [int]$_.decision_id -eq $decisionId -and [int]$_.candidate_rank -eq $rank } | Select-Object -First 1
    if ($null -eq $manifest) {
        throw "Missing manifest row for decision $decisionId rank $rank in $manifestPath"
    }
    $expectedKeys = Expected-TransportKeys $selected
    $segments = @(Import-Csv -LiteralPath $segmentsPath | Where-Object {
        if ($_.matched_to_milp -ne "1" -or $_.actual_duration -eq "" -or $_.actual_start_time -eq "") { return $false }
        if ((D $_.actual_start_time) -lt ($decisionTime - 0.000001)) { return $false }
        $key = "$($_.bot_id)|$($_.pod_id)|$($_.station_id)"
        return $expectedKeys.ContainsKey($key)
    })
    $leg1 = @($segments | Where-Object { $_.segment_type -eq "BotToPod" })
    $leg2 = @($segments | Where-Object { $_.segment_type -eq "PodToStation" })

    $actualTravel = ($segments | ForEach-Object { D $_.actual_duration } | Measure-Object -Sum).Sum
    $actualWait = ($segments | ForEach-Object { D $_.actual_wait_time } | Measure-Object -Sum).Sum
    $actualDistance = ($segments | ForEach-Object { D $_.actual_distance } | Measure-Object -Sum).Sum
    $estimatedTravelCompleted = ($segments | ForEach-Object { D $_.estimated_travel_time } | Measure-Object -Sum).Sum
    $leg1Actual = ($leg1 | ForEach-Object { D $_.actual_duration } | Measure-Object -Sum).Sum
    $leg2Actual = ($leg2 | ForEach-Object { D $_.actual_duration } | Measure-Object -Sum).Sum
    $leg1Wait = ($leg1 | ForEach-Object { D $_.actual_wait_time } | Measure-Object -Sum).Sum
    $leg2Wait = ($leg2 | ForEach-Object { D $_.actual_wait_time } | Measure-Object -Sum).Sum

    $milpObjective = D $manifest.milp_objective
    $manifestEstimatedTravel = if ((Field $manifest "estimated_travel_objective") -ne "") { D (Field $manifest "estimated_travel_objective") } else { (D $manifest.estimated_leg1_objective) + (D $manifest.estimated_leg2_objective) }
    $executableEstimatedTravel = (D (Field $selected "executable_estimated_leg1_objective")) + (D (Field $selected "executable_estimated_leg2_objective"))
    $originalNonTravel = $milpObjective - $manifestEstimatedTravel
    $executableNonTravel = $milpObjective - $executableEstimatedTravel
    $orderTerm = if ((Field $selected "objective_order_term") -ne "") { D (Field $selected "objective_order_term") } else { D (Field $manifest "objective_order_term") }
    $capacityTerm = if ((Field $selected "objective_capacity_term") -ne "") { D (Field $selected "objective_capacity_term") } else { D (Field $manifest "objective_capacity_term") }
    $actualExecutableDirect = $actualTravel + $orderTerm + $capacityTerm

    $rows.Add([pscustomobject]@{
        decision_id = $decisionId
        decision_time = $decisionTime
        candidate_rank = $rank
        run_dir = $runDir
        milp_objective = $milpObjective
        manifest_estimated_travel_objective = $manifestEstimatedTravel
        executable_estimated_travel_objective = $executableEstimatedTravel
        original_nontravel_objective = $originalNonTravel
        executable_nontravel_objective = $executableNonTravel
        actual_travel_objective = $actualTravel
        actual_objective_original_nontravel = $originalNonTravel + $actualTravel
        actual_objective_executable_nontravel = $executableNonTravel + $actualTravel
        actual_objective_executable_direct = $actualExecutableDirect
        objective_order_term = $orderTerm
        objective_capacity_term = $capacityTerm
        completed_records = $segments.Count
        original_xps_count = [int]$selected.original_xps_count
        original_yrp_count = [int]$selected.original_yrp_count
        executable_xps_count = [int]$selected.executable_xps_count
        executable_yrp_count = [int]$selected.executable_yrp_count
        actual_total_duration = $actualTravel
        estimated_completed_travel_time = $estimatedTravelCompleted
        actual_total_wait_time = $actualWait
        actual_total_distance = $actualDistance
        leg1_actual_total_duration = $leg1Actual
        leg2_actual_total_duration = $leg2Actual
        leg1_actual_wait_time = $leg1Wait
        leg2_actual_wait_time = $leg2Wait
        executable_xps = $selected.executable_xps
        executable_yrp = $selected.executable_yrp
    }) | Out-Null

    if (Test-Path -LiteralPath $snapshotPath) {
        Import-Csv -LiteralPath $snapshotPath | ForEach-Object {
            $snapshots.Add([pscustomobject]@{
                candidate_rank = $rank
                decision_id = $_.decision_id
                decision_time = $_.decision_time
                snapshot_sha256 = $_.snapshot_sha256
            }) | Out-Null
        }
    }
}

$withRanks = $rows | Sort-Object candidate_rank
$actualOriginalOrder = $withRanks | Sort-Object actual_objective_original_nontravel, candidate_rank
$actualExecutableOrder = $withRanks | Sort-Object actual_objective_executable_nontravel, candidate_rank
$actualExecutableDirectOrder = $withRanks | Sort-Object actual_objective_executable_direct, candidate_rank
$actualTravelOrder = $withRanks | Sort-Object actual_travel_objective, candidate_rank

foreach ($row in $withRanks) {
    $row | Add-Member -NotePropertyName actual_rank_original_nontravel -NotePropertyValue (($actualOriginalOrder.IndexOf($row)) + 1)
    $row | Add-Member -NotePropertyName actual_rank_executable_nontravel -NotePropertyValue (($actualExecutableOrder.IndexOf($row)) + 1)
    $row | Add-Member -NotePropertyName actual_rank_executable_direct -NotePropertyValue (($actualExecutableDirectOrder.IndexOf($row)) + 1)
    $row | Add-Member -NotePropertyName actual_rank_travel_only -NotePropertyValue (($actualTravelOrder.IndexOf($row)) + 1)
}

$withRanks | Export-Csv -NoTypeInformation -Path $OutCsv

$snapshotDistinct = @($snapshots | Select-Object -ExpandProperty snapshot_sha256 -Unique).Count
$bestOriginal = $actualOriginalOrder | Select-Object -First 1
$bestExecutable = $actualExecutableOrder | Select-Object -First 1
$bestExecutableDirect = $actualExecutableDirectOrder | Select-Object -First 1
$bestTravel = $actualTravelOrder | Select-Object -First 1

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# M1G-time Top-5 Replay Objective Comparison") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("## Artifacts") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("- Comparison CSV: ``$OutCsv``") | Out-Null
$lines.Add("- Output root: ``$outputRootAbs``") | Out-Null
$lines.Add("- Snapshot hashes observed: $snapshotDistinct") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("## Ranking Result") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("| candidate | MILP obj | actual executable objective | rank | actual travel only | travel rank | completed legs | wait | distance |") | Out-Null
$lines.Add("|---:|---:|---:|---:|---:|---:|---:|---:|---:|") | Out-Null
foreach ($row in $withRanks) {
    $lines.Add("| $($row.candidate_rank) | $(F $row.milp_objective) | $(F $row.actual_objective_executable_direct) | $($row.actual_rank_executable_direct) | $(F $row.actual_travel_objective) | $($row.actual_rank_travel_only) | $($row.completed_records) | $(F $row.actual_total_wait_time) | $(F $row.actual_total_distance) |") | Out-Null
}
$lines.Add("") | Out-Null
$lines.Add("## Interpretation") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("- MILP rank-1 remains actual rank $($withRanks[0].actual_rank_original_nontravel) under original-nontravel objective replacement.") | Out-Null
$lines.Add("- Best actual objective using original nontravel: rank $($bestOriginal.candidate_rank), value $(F $bestOriginal.actual_objective_original_nontravel).") | Out-Null
$lines.Add("- Best actual objective using executable nontravel: rank $($bestExecutable.candidate_rank), value $(F $bestExecutable.actual_objective_executable_nontravel).") | Out-Null
$lines.Add("- Best actual objective using direct executable formula: rank $($bestExecutableDirect.candidate_rank), value $(F $bestExecutableDirect.actual_objective_executable_direct).") | Out-Null
$lines.Add("- Best travel-only replay: rank $($bestTravel.candidate_rank), value $(F $bestTravel.actual_travel_objective) seconds.") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("``actual_objective_executable_direct = actual_replay_travel + objective_order_term + objective_capacity_term``. This is the primary oracle ranking used by the rolling experiment.") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("The summary filters actual path records to the validation decision's executable bot/pod/station keys, so prior forced-policy decisions do not pollute the current decision cost.") | Out-Null

$lines | Set-Content -Path $OutMd -Encoding UTF8
Write-Host "Wrote comparison: $OutCsv"
Write-Host "Wrote conclusion: $OutMd"
