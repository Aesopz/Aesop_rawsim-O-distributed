param(
    [string]$ResultsRoot = "Results\M1GTTop5SingleBatch",
    [string]$OutputHtml = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputHtml)) {
    $OutputHtml = Join-Path $ResultsRoot "m1gt_counterfactual_dashboard.html"
}

function Read-RankRun {
    param([int]$Rank, [string]$Root)

    $runDir = Join-Path $Root ("benchmark_ss_layout-small_sett-m1gt-r{0}-0" -f $Rank)
    if (!(Test-Path $runDir)) {
        $runDir = Get-ChildItem -Path $Root -Recurse -Directory |
            Where-Object { $_.Name -match ("benchmark_ss_layout-small_sett-m1gt-r{0}-" -f $Rank) } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if ([string]::IsNullOrWhiteSpace($runDir)) { throw "Missing run directory for rank $Rank under $Root" }
    $segmentsPath = Join-Path $runDir "m1g_path_estimate_actual_segments.csv"
    $summaryPath = Join-Path $runDir "m1g_path_estimate_actual_summary.csv"
    $manifestPath = Join-Path $runDir "m1gt_top5_candidates.csv"

    if (!(Test-Path $segmentsPath)) { throw "Missing $segmentsPath" }
    if (!(Test-Path $summaryPath)) { throw "Missing $summaryPath" }
    if (!(Test-Path $manifestPath)) { throw "Missing $manifestPath" }

    $segments = Import-Csv $segmentsPath | ForEach-Object {
        [pscustomobject]@{
            rank = $Rank
            decision_id = [int]$_.decision_id
            segment_id = [int]$_.segment_id
            segment_type = $_.segment_type
            bot_id = [int]$_.bot_id
            pod_id = [int]$_.pod_id
            station_id = [int]$_.station_id
            estimated_travel_time = [double]$_.estimated_travel_time
            estimated_distance = [double]$_.estimated_distance
            actual_start_time = if ([string]::IsNullOrWhiteSpace($_.actual_start_time)) { $null } else { [double]$_.actual_start_time }
            actual_end_time = if ([string]::IsNullOrWhiteSpace($_.actual_end_time)) { $null } else { [double]$_.actual_end_time }
            actual_duration = if ([string]::IsNullOrWhiteSpace($_.actual_duration)) { $null } else { [double]$_.actual_duration }
            actual_distance = if ([string]::IsNullOrWhiteSpace($_.actual_distance)) { $null } else { [double]$_.actual_distance }
            actual_wait_time = if ([string]::IsNullOrWhiteSpace($_.actual_wait_time)) { 0.0 } else { [double]$_.actual_wait_time }
            actual_turn_count = if ([string]::IsNullOrWhiteSpace($_.actual_turn_count)) { 0 } else { [int]$_.actual_turn_count }
            actual_end_kind = $_.actual_end_kind
            actual_path_nodes = $_.actual_path_nodes
            estimated_path_nodes = $_.estimated_path_nodes
            time_gap = if ([string]::IsNullOrWhiteSpace($_.time_gap)) { $null } else { [double]$_.time_gap }
            distance_gap = if ([string]::IsNullOrWhiteSpace($_.distance_gap)) { $null } else { [double]$_.distance_gap }
            wait_share = if ([string]::IsNullOrWhiteSpace($_.wait_share)) { 0.0 } else { [double]$_.wait_share }
        }
    }

    $summary = Import-Csv $summaryPath
    $all = $summary | Where-Object { $_.segment_type -eq "All" } | Select-Object -First 1
    $leg1 = $summary | Where-Object { $_.segment_type -eq "BotToPod" } | Select-Object -First 1
    $leg2 = $summary | Where-Object { $_.segment_type -eq "PodToStation" } | Select-Object -First 1
    $manifest = Import-Csv $manifestPath | Where-Object { [int]$_.candidate_rank -eq $Rank } | Select-Object -First 1

    [pscustomobject]@{
        rank = $Rank
        run_dir = $runDir
        manifest = $manifest
        summary = [pscustomobject]@{
            records = [int]$all.records
            completed_records = [int]$all.completed_records
            avg_estimated_travel_time = [double]$all.avg_estimated_travel_time
            avg_actual_duration = [double]$all.avg_actual_duration
            avg_time_gap = [double]$all.avg_time_gap
            avg_actual_wait_time = [double]$all.avg_actual_wait_time
            leg1_completed = [int]$leg1.completed_records
            leg2_completed = [int]$leg2.completed_records
        }
        segments = @($segments)
    }
}

$comparisonPath = Join-Path $ResultsRoot "m1gt_counterfactual_rank_comparison.csv"
if (!(Test-Path $comparisonPath)) { throw "Missing $comparisonPath" }

$comparison = Import-Csv $comparisonPath | ForEach-Object {
    [pscustomobject]@{
        candidate_rank = [int]$_.candidate_rank
        actual_rank = [int]$_.actual_rank
        milp_objective = [double]$_.milp_objective
        estimated_total_travel_time = [double]$_.estimated_total_travel_time
        actual_total_duration = [double]$_.actual_total_duration
        actual_total_wait_time = [double]$_.actual_total_wait_time
        leg1_actual_total_duration = [double]$_.leg1_actual_total_duration
        leg2_actual_total_duration = [double]$_.leg2_actual_total_duration
        records = [int]$_.records
        completed_records = [int]$_.completed_records
        leg2_completed_records = [int]$_.leg2_completed_records
    }
}

$runs = @(1..5 | ForEach-Object { Read-RankRun -Rank $_ -Root $ResultsRoot })

$waypointMap = @{}
$graphEdgeMap = @{}
foreach ($rank in 1..5) {
    $runDir = Join-Path $ResultsRoot ("benchmark_ss_layout-small_sett-m1gt-r{0}-0" -f $rank)
    if (!(Test-Path $runDir)) {
        $runDir = Get-ChildItem -Path $ResultsRoot -Recurse -Directory |
            Where-Object { $_.Name -match ("benchmark_ss_layout-small_sett-m1gt-r{0}-" -f $rank) } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    $waypointsPath = Join-Path $runDir "m1g_waypoints.csv"
    if (Test-Path $waypointsPath) {
        Import-Csv $waypointsPath | ForEach-Object {
            $id = [int]$_.waypoint_id
            if (!$waypointMap.ContainsKey($id)) {
                $waypointMap[$id] = [pscustomobject]@{
                    id = $id
                    x = [double]$_.x
                    y = [double]$_.y
                    is_storage = $_.is_storage -eq "1"
                    is_queue = $_.is_queue -eq "1"
                    is_input_station = $_.is_input_station -eq "1"
                    is_output_station = $_.is_output_station -eq "1"
                    is_elevator = $_.is_elevator -eq "1"
                }
            }
            if (![string]::IsNullOrWhiteSpace($_.paths)) {
                $_.paths.Split("|") | Where-Object { $_ -ne "" } | ForEach-Object {
                    $toId = [int]$_
                    $edgeKey = "$id|$toId"
                    if (!$graphEdgeMap.ContainsKey($edgeKey)) {
                        $graphEdgeMap[$edgeKey] = [pscustomobject]@{ from = $id; to = $toId }
                    }
                }
            }
        }
        continue
    }

    $connectionPath = Join-Path $runDir "connectionstatistics.csv"
    if (!(Test-Path $connectionPath)) { continue }
    Get-Content $connectionPath | Where-Object { $_ -and !$_.StartsWith("#") } | ForEach-Object {
        $parts = $_.Split(";")
        if ($parts.Length -lt 14) { return }
        $fromId = [int]$parts[0]
        $toId = [int]$parts[7]
        if (!$waypointMap.ContainsKey($fromId)) {
            $waypointMap[$fromId] = [pscustomobject]@{
                id = $fromId
                x = [double]$parts[1]
                y = [double]$parts[2]
                is_storage = [bool]::Parse($parts[3])
                is_input_station = [bool]::Parse($parts[4])
                is_output_station = [bool]::Parse($parts[5])
                is_elevator = [bool]::Parse($parts[6])
            }
        }
        if (!$waypointMap.ContainsKey($toId)) {
            $waypointMap[$toId] = [pscustomobject]@{
                id = $toId
                x = [double]$parts[8]
                y = [double]$parts[9]
                is_storage = [bool]::Parse($parts[10])
                is_input_station = [bool]::Parse($parts[11])
                is_output_station = [bool]::Parse($parts[12])
                is_elevator = [bool]::Parse($parts[13])
            }
        }
        $edgeKey = "$fromId|$toId"
        if (!$graphEdgeMap.ContainsKey($edgeKey)) {
            $graphEdgeMap[$edgeKey] = [pscustomobject]@{ from = $fromId; to = $toId }
        }
    }
}

$payload = [pscustomobject]@{
    generated_at = (Get-Date).ToString("s")
    results_root = (Resolve-Path $ResultsRoot).Path
    waypoints = @($waypointMap.Values | Sort-Object id)
    graph_edges = @($graphEdgeMap.Values)
    comparison = @($comparison)
    runs = @($runs)
}
$json = $payload | ConvertTo-Json -Depth 12 -Compress

$htmlTemplate = @'
<!doctype html>
<html lang="zh-Hant">
<head>
<meta charset="utf-8">
<title>M1G-t Top-5 Counterfactual Replay Dashboard</title>
<style>
:root {
  --bg: #f6f7f9;
  --panel: #ffffff;
  --ink: #20252b;
  --muted: #68717d;
  --line: #d9dee5;
  --leg1: #2f6f9f;
  --leg2: #2f8a68;
  --wait: #d88b2a;
  --rank1: #8b2f2f;
}
* { box-sizing: border-box; }
body {
  margin: 0;
  background: var(--bg);
  color: var(--ink);
  font-family: "Segoe UI", Arial, sans-serif;
}
header {
  padding: 22px 28px 14px;
  border-bottom: 1px solid var(--line);
  background: #fff;
}
h1 { margin: 0 0 6px; font-size: 24px; font-weight: 650; }
h2 { margin: 0 0 12px; font-size: 17px; font-weight: 650; }
.sub { color: var(--muted); font-size: 13px; }
main { padding: 18px 28px 28px; display: grid; gap: 18px; }
.grid { display: grid; grid-template-columns: 1.1fr .9fr; gap: 18px; align-items: start; }
.panel {
  background: var(--panel);
  border: 1px solid var(--line);
  border-radius: 8px;
  padding: 16px;
  overflow: hidden;
}
.cards { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; }
.card { background: #fff; border: 1px solid var(--line); border-radius: 8px; padding: 13px 14px; }
.label { color: var(--muted); font-size: 12px; margin-bottom: 6px; }
.value { font-size: 22px; font-weight: 650; }
.warn { color: var(--rank1); font-weight: 650; }
svg { width: 100%; height: auto; display: block; }
table { width: 100%; border-collapse: collapse; font-size: 12px; }
th, td { border-bottom: 1px solid var(--line); padding: 7px 8px; text-align: right; white-space: nowrap; }
th:first-child, td:first-child { text-align: left; }
th { color: var(--muted); font-weight: 600; background: #fbfcfd; }
.best { background: #eaf6f0; font-weight: 650; }
.bad { background: #faeeee; }
.legend { display: flex; gap: 16px; align-items: center; color: var(--muted); font-size: 12px; margin-top: 10px; }
.dot { width: 12px; height: 12px; border-radius: 2px; display: inline-block; margin-right: 5px; vertical-align: -2px; }
.small { font-size: 12px; color: var(--muted); }
.heat { display: grid; grid-template-columns: 190px repeat(5, 1fr); gap: 1px; background: var(--line); border: 1px solid var(--line); }
.heat > div { background: #fff; padding: 7px 8px; font-size: 12px; min-height: 30px; }
.heat .head { background: #fbfcfd; color: var(--muted); font-weight: 600; }
.heat .edge { text-align: left; font-family: Consolas, monospace; }
.nodebar { height: 12px; border-radius: 2px; background: #dce7ef; overflow: hidden; }
.nodebar > span { display:block; height:100%; background:#386f99; }
.mapwrap { display: grid; grid-template-columns: 1.2fr .8fr; gap: 14px; align-items: start; }
.rankchips { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 10px; }
.chip { border: 1px solid var(--line); border-radius: 6px; padding: 6px 8px; font-size: 12px; background: #fff; }
.chip input { vertical-align: -2px; }
.botchips { display: flex; flex-wrap: wrap; gap: 6px; margin: -4px 0 12px; }
.botchip { border: 1px solid var(--line); border-radius: 6px; padding: 5px 7px; font-size: 12px; background: #fff; }
.botchip input { vertical-align: -2px; }
.botdot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: 4px; vertical-align: -1px; }
@media (max-width: 1100px) {
  .grid, .cards, .mapwrap { grid-template-columns: 1fr; }
}
</style>
</head>
<body>
<header>
  <h1>M1G-t Top-5 Counterfactual Replay Dashboard</h1>
  <div class="sub" id="meta"></div>
</header>
<main>
  <section class="cards" id="cards"></section>
  <section class="grid">
    <div class="panel">
      <h2>Actual Cost Breakdown</h2>
      <svg id="costChart" viewBox="0 0 920 340" role="img"></svg>
      <div class="legend">
        <span><i class="dot" style="background:var(--leg1)"></i>Leg1 bot to pod</span>
        <span><i class="dot" style="background:var(--leg2)"></i>Leg2 pod to queue point</span>
        <span><i class="dot" style="background:var(--wait)"></i>Wait component</span>
      </div>
    </div>
    <div class="panel">
      <h2>Rank Evidence Table</h2>
      <div id="rankTable"></div>
    </div>
  </section>
  <section class="panel">
    <h2>Actual Path Map Overlay</h2>
    <div class="rankchips" id="mapControls"></div>
    <div class="botchips" id="mapBotControls"></div>
    <div class="mapwrap">
      <svg id="mapOverlay" viewBox="0 0 920 520" role="img"></svg>
      <div>
        <div id="mapStats"></div>
        <div class="small">灰線是五個 replay 中實際走過的 waypoint connection；彩色線是各 candidate 的 actual path。虛線為 BotToPod，實線為 PodToStation。因為 WCHA*n 可能改路，這裡畫的是實際執行軌跡，不是 MILP estimated path。</div>
      </div>
    </div>
  </section>
  <section class="panel">
    <h2>Replay Timeline</h2>
    <svg id="timeline" viewBox="0 0 1180 620" role="img"></svg>
    <div class="small">每列是一個 replay 內的一台 bot；橘色尾段是該 leg 的 waiting component，leg2 的終點固定為 StationQueueEntry。</div>
  </section>
  <section class="grid">
    <div class="panel">
      <h2>Leg Gap Heatmap</h2>
      <div id="legHeat"></div>
    </div>
    <div class="panel">
      <h2>Path Node Overlap</h2>
      <div id="edgeHeat"></div>
      <div class="small">這不是幾何地圖，而是 path node sequence 的共用邊統計；可用來先看哪個 replay 把 bot 壓到相同通道。</div>
    </div>
  </section>
</main>
<script>
const data = __DATA_JSON__;
const fmt = (v, d=1) => Number(v).toFixed(d);
const svgEl = (name, attrs={}) => {
  const el = document.createElementNS("http://www.w3.org/2000/svg", name);
  Object.entries(attrs).forEach(([k,v]) => el.setAttribute(k, v));
  return el;
};
function addText(svg, x, y, text, attrs={}) {
  const el = svgEl("text", {x, y, ...attrs});
  el.textContent = text;
  svg.appendChild(el);
  return el;
}
function colorHeat(v, max, hue=28) {
  const a = max <= 0 ? 0 : Math.min(1, v / max);
  return `hsl(${hue}, 76%, ${96 - a * 34}%)`;
}
function init() {
  document.getElementById("meta").textContent = `Generated ${data.generated_at} | ${data.results_root}`;
  renderCards();
  renderCostChart();
  renderRankTable();
  renderMapControls();
  renderMapOverlay();
  renderTimeline();
  renderLegHeat();
  renderEdgeHeat();
}
function bestBy(field, lower=true) {
  return [...data.comparison].sort((a,b) => lower ? a[field]-b[field] : b[field]-a[field])[0];
}
function renderCards() {
  const best = bestBy("actual_total_duration");
  const milp1 = data.comparison.find(r => r.candidate_rank === 1);
  const delta = milp1.actual_total_duration - best.actual_total_duration;
  const cards = [
    ["MILP Rank-1 Actual Rank", `#${milp1.actual_rank}`, milp1.actual_rank === 1 ? "" : "warn"],
    ["Actual Best Candidate", `Rank ${best.candidate_rank}`, ""],
    ["Rank-1 Penalty", `${fmt(delta, 2)} s`, "warn"],
    ["Rank-1 vs Best", `${fmt(delta / best.actual_total_duration * 100, 1)}%`, "warn"]
  ];
  document.getElementById("cards").innerHTML = cards.map(c =>
    `<div class="card"><div class="label">${c[0]}</div><div class="value ${c[2]}">${c[1]}</div></div>`).join("");
}
function renderCostChart() {
  const svg = document.getElementById("costChart");
  svg.innerHTML = "";
  const W=920,H=340, left=68, top=24, bottom=54, chartH=H-top-bottom, gap=30, barW=105;
  const max = Math.max(...data.comparison.map(r => r.actual_total_duration)) * 1.12;
  addText(svg, 12, 18, "seconds", {fill:"#68717d", "font-size":12});
  for (let i=0;i<=4;i++) {
    const y = top + chartH - chartH * i / 4;
    svg.appendChild(svgEl("line", {x1:left, y1:y, x2:W-24, y2:y, stroke:"#e5e9ef"}));
    addText(svg, 18, y+4, fmt(max*i/4,0), {fill:"#68717d", "font-size":11});
  }
  data.comparison.forEach((r, idx) => {
    const x = left + idx * (barW + gap) + 28;
    const leg1 = r.leg1_actual_total_duration;
    const leg2 = r.leg2_actual_total_duration;
    const wait = r.actual_total_wait_time;
    let y = top + chartH;
    [["leg1",leg1,"var(--leg1)"],["leg2",leg2,"var(--leg2)"],["wait",wait,"var(--wait)"]].forEach(p => {
      const h = chartH * p[1] / max;
      y -= h;
      svg.appendChild(svgEl("rect", {x, y, width:barW, height:h, fill:p[2], rx:2}));
    });
    if (r.actual_rank === 1) {
      svg.appendChild(svgEl("rect", {x:x-5, y:top+chartH-chartH*r.actual_total_duration/max-5, width:barW+10, height:chartH*r.actual_total_duration/max+10, fill:"none", stroke:"#26734d", "stroke-width":2, rx:5}));
    }
    addText(svg, x+barW/2, H-28, `MILP r${r.candidate_rank}`, {"text-anchor":"middle", fill:"#20252b", "font-size":12});
    addText(svg, x+barW/2, H-12, `actual #${r.actual_rank}`, {"text-anchor":"middle", fill:r.actual_rank===1?"#26734d":"#68717d", "font-size":12, "font-weight":r.actual_rank===1?700:400});
    addText(svg, x+barW/2, y-8, fmt(r.actual_total_duration,1), {"text-anchor":"middle", fill:"#20252b", "font-size":12});
  });
}
function renderRankTable() {
  const rows = [...data.comparison].sort((a,b)=>a.candidate_rank-b.candidate_rank);
  document.getElementById("rankTable").innerHTML = `<table>
    <thead><tr><th>candidate</th><th>MILP obj</th><th>actual rank</th><th>actual sec</th><th>wait sec</th><th>legs</th></tr></thead>
    <tbody>${rows.map(r => `<tr class="${r.actual_rank===1?"best":(r.candidate_rank===1&&r.actual_rank!==1?"bad":"")}">
      <td>rank ${r.candidate_rank}</td><td>${fmt(r.milp_objective,0)}</td><td>#${r.actual_rank}</td><td>${fmt(r.actual_total_duration,2)}</td><td>${fmt(r.actual_total_wait_time,2)}</td><td>${r.completed_records}</td>
    </tr>`).join("")}</tbody></table>`;
}
const rankColors = {1:"#b33a3a",2:"#2878b8",3:"#2d9a63",4:"#8b61c2",5:"#c9822c"};
const botPalette = ["#1f77b4","#d62728","#2ca02c","#9467bd","#ff7f0e","#17becf","#8c564b","#e377c2","#7f7f7f","#bcbd22","#005f73","#9b2226"];
const botColor = id => botPalette[Math.abs(Number(id)) % botPalette.length];
function activeRanks() {
  return [...document.querySelectorAll("[data-rank-toggle]")].filter(x => x.checked).map(x => Number(x.value));
}
function activeBots() {
  return [...document.querySelectorAll("[data-bot-toggle]")].filter(x => x.checked).map(x => Number(x.value));
}
function renderMapControls() {
  const box = document.getElementById("mapControls");
  box.innerHTML = [1,2,3,4,5].map(r => `<label class="chip"><input data-rank-toggle type="checkbox" value="${r}" checked> <span style="color:${rankColors[r]};font-weight:700">rank ${r}</span></label>`).join("");
  box.querySelectorAll("input").forEach(x => x.addEventListener("change", renderMapOverlay));

  const bots = [...new Set(data.runs.flatMap(run => run.segments.map(seg => seg.bot_id)))].sort((a,b)=>a-b);
  const botBox = document.getElementById("mapBotControls");
  botBox.innerHTML = bots.map(b => `<label class="botchip"><input data-bot-toggle type="checkbox" value="${b}" checked> <span class="botdot" style="background:${botColor(b)}"></span>B${b}</label>`).join("");
  botBox.querySelectorAll("input").forEach(x => x.addEventListener("change", renderMapOverlay));
}
function getWpMap() {
  const m = new Map();
  (data.waypoints || []).forEach(w => m.set(String(w.id), w));
  return m;
}
function renderMapOverlay() {
  const svg = document.getElementById("mapOverlay");
  svg.innerHTML = "";
  const wp = getWpMap();
  if (!wp.size) {
    addText(svg, 30, 40, "No waypoint coordinates found in connectionstatistics.csv", {fill:"#8b2f2f", "font-size":14});
    return;
  }
  const ranks = activeRanks();
  const bots = activeBots();
  const singleRankMode = ranks.length === 1;
  const values = [...wp.values()];
  const minX = Math.min(...values.map(w=>w.x)), maxX = Math.max(...values.map(w=>w.x));
  const minY = Math.min(...values.map(w=>w.y)), maxY = Math.max(...values.map(w=>w.y));
  const W=920,H=520,pad=34;
  const sx = x => pad + (x-minX) * (W-pad*2) / Math.max(1, maxX-minX);
  const sy = y => H - pad - (y-minY) * (H-pad*2) / Math.max(1, maxY-minY);
  const point = id => {
    const w = wp.get(String(id));
    return w ? `${sx(w.x)},${sy(w.y)}` : null;
  };

  svg.appendChild(svgEl("rect", {x:0, y:0, width:W, height:H, fill:"#fbfcfd"}));
  (data.graph_edges || []).forEach(e => {
    const a = wp.get(String(e.from)), b = wp.get(String(e.to));
    if (!a || !b) return;
    svg.appendChild(svgEl("line", {x1:sx(a.x), y1:sy(a.y), x2:sx(b.x), y2:sy(b.y), stroke:"#cfd6df", "stroke-width":1.1, opacity:.75}));
  });
  values.forEach(w => {
    const isStation = w.is_input_station || w.is_output_station;
    const fill = w.is_output_station ? "#20252b" : (w.is_input_station ? "#68717d" : (w.is_storage ? "#e8edf3" : "#ffffff"));
    if (isStation) {
      svg.appendChild(svgEl("rect", {x:sx(w.x)-4, y:sy(w.y)-4, width:8, height:8, fill, rx:1}));
    } else {
      svg.appendChild(svgEl("circle", {cx:sx(w.x), cy:sy(w.y), r:w.is_storage?2.1:1.5, fill, stroke:"#ccd3dc", "stroke-width":.7}));
    }
  });

  ranks.forEach(rank => {
    const run = data.runs.find(r => r.rank === rank);
    if (!run) return;
    run.segments.filter(seg => bots.includes(seg.bot_id)).forEach(seg => {
      const nodes = (seg.actual_path_nodes || "").split("|").filter(Boolean);
      const pts = nodes.map(point).filter(Boolean);
      if (pts.length < 2) return;
      const color = singleRankMode ? botColor(seg.bot_id) : rankColors[rank];
      const width = seg.segment_type === "PodToStation" ? (singleRankMode ? 4.6 : 4.0) : (singleRankMode ? 3.1 : 2.4);
      const attrs = {points:pts.join(" "), fill:"none", stroke:color, "stroke-width":width, "stroke-linejoin":"round", "stroke-linecap":"round", opacity:singleRankMode ? .86 : .52};
      if (seg.segment_type === "BotToPod") attrs["stroke-dasharray"] = "6 5";
      svg.appendChild(svgEl("polyline", attrs));
      const start = wp.get(String(nodes[0]));
      const end = wp.get(String(nodes[nodes.length-1]));
      if (start) svg.appendChild(svgEl("circle", {cx:sx(start.x), cy:sy(start.y), r:3.3, fill:color, opacity:.9}));
      if (end) svg.appendChild(svgEl("rect", {x:sx(end.x)-3.5, y:sy(end.y)-3.5, width:7, height:7, fill:color, opacity:.9}));
      if (singleRankMode) {
        const mid = wp.get(String(nodes[Math.floor(nodes.length / 2)]));
        if (mid) {
          const tx = sx(mid.x), ty = sy(mid.y);
          svg.appendChild(svgEl("rect", {x:tx-10, y:ty-9, width:22, height:14, fill:"#ffffff", opacity:.84, rx:3}));
          addText(svg, tx+1, ty+2, `B${seg.bot_id}`, {"text-anchor":"middle", fill:color, "font-size":10, "font-weight":700});
        }
      }
    });
  });

  addText(svg, 14, 22, "Actual path overlay by replay rank", {fill:"#20252b", "font-size":13, "font-weight":700});
  let lx = 14;
  if (singleRankMode) {
    const botLegend = bots.slice(0, 10);
    botLegend.forEach(bot => {
      svg.appendChild(svgEl("line", {x1:lx, y1:42, x2:lx+24, y2:42, stroke:botColor(bot), "stroke-width":4, "stroke-linecap":"round"}));
      addText(svg, lx+30, 46, `B${bot}`, {fill:"#20252b", "font-size":12});
      lx += 64;
    });
  } else {
    ranks.forEach(rank => {
      svg.appendChild(svgEl("line", {x1:lx, y1:42, x2:lx+28, y2:42, stroke:rankColors[rank], "stroke-width":4, "stroke-linecap":"round"}));
      addText(svg, lx+34, 46, `r${rank}`, {fill:"#20252b", "font-size":12});
      lx += 72;
    });
  }
  svg.appendChild(svgEl("line", {x1:W-190, y1:22, x2:W-158, y2:22, stroke:"#555", "stroke-width":3, "stroke-dasharray":"6 5"}));
  addText(svg, W-150, 26, "BotToPod", {fill:"#555", "font-size":12});
  svg.appendChild(svgEl("line", {x1:W-190, y1:42, x2:W-158, y2:42, stroke:"#555", "stroke-width":4}));
  addText(svg, W-150, 46, "PodToStation", {fill:"#555", "font-size":12});

  renderMapStats(ranks, bots);
}
function renderMapStats(ranks, bots) {
  const rows = ranks.map(rank => {
    const run = data.runs.find(r => r.rank === rank);
    const segs = run ? run.segments.filter(s => s.actual_duration !== null && bots.includes(s.bot_id)) : [];
    const uniqueNodes = new Set();
    let totalNodes = 0;
    segs.forEach(s => (s.actual_path_nodes || "").split("|").filter(Boolean).forEach(n => { uniqueNodes.add(n); totalNodes++; }));
    const c = data.comparison.find(x => x.candidate_rank === rank);
    return {rank, uniqueNodes:uniqueNodes.size, totalNodes, actual:c ? c.actual_total_duration : 0, wait:c ? c.actual_total_wait_time : 0};
  });
  document.getElementById("mapStats").innerHTML = `<table>
    <thead><tr><th>rank</th><th>actual sec</th><th>wait sec</th><th>unique nodes</th><th>path node visits</th></tr></thead>
    <tbody>${rows.map(r => `<tr><td><span style="color:${rankColors[r.rank]};font-weight:700">rank ${r.rank}</span></td><td>${fmt(r.actual,2)}</td><td>${fmt(r.wait,2)}</td><td>${r.uniqueNodes}</td><td>${r.totalNodes}</td></tr>`).join("")}</tbody>
  </table>`;
  if (ranks.length === 1) {
    const rank = ranks[0];
    const run = data.runs.find(r => r.rank === rank);
    const botRows = bots.map(bot => {
      const segs = run ? run.segments.filter(s => s.bot_id === bot && s.actual_duration !== null) : [];
      return {
        bot,
        duration: segs.reduce((a,s)=>a + (s.actual_duration || 0), 0),
        wait: segs.reduce((a,s)=>a + (s.actual_wait_time || 0), 0),
        legs: segs.length
      };
    }).filter(r => r.legs > 0);
    document.getElementById("mapStats").innerHTML += `<h2 style="margin-top:16px">Robot Detail in Rank ${rank}</h2><table>
      <thead><tr><th>bot</th><th>legs</th><th>duration sec</th><th>wait sec</th></tr></thead>
      <tbody>${botRows.map(r => `<tr><td><span class="botdot" style="background:${botColor(r.bot)}"></span>B${r.bot}</td><td>${r.legs}</td><td>${fmt(r.duration,2)}</td><td>${fmt(r.wait,2)}</td></tr>`).join("")}</tbody>
    </table>`;
  }
}
function renderTimeline() {
  const svg = document.getElementById("timeline");
  svg.innerHTML = "";
  const allSeg = data.runs.flatMap(r => r.segments);
  const maxT = Math.max(...allSeg.map(s => s.actual_end_time || 0));
  const W=1180, left=120, top=26, rowH=18, rankGap=20, right=24;
  const scale = t => left + (W-left-right) * t / maxT;
  addText(svg, left, 15, "0", {fill:"#68717d", "font-size":11});
  addText(svg, W-right, 15, `${fmt(maxT,1)}s`, {"text-anchor":"end", fill:"#68717d", "font-size":11});
  svg.appendChild(svgEl("line", {x1:left, y1:20, x2:W-right, y2:20, stroke:"#b8c0ca"}));
  let y = top + 18;
  data.runs.forEach(run => {
    const bots = [...new Set(run.segments.map(s => s.bot_id))].sort((a,b)=>a-b);
    addText(svg, 10, y+6, `candidate ${run.rank}`, {fill:"#20252b", "font-size":12, "font-weight":700});
    bots.forEach(bot => {
      const cy = y;
      addText(svg, 86, cy+4, `B${bot}`, {"text-anchor":"end", fill:"#68717d", "font-size":11});
      svg.appendChild(svgEl("line", {x1:left, y1:cy, x2:W-right, y2:cy, stroke:"#edf0f4"}));
      run.segments.filter(s => s.bot_id === bot && s.actual_start_time !== null && s.actual_end_time !== null).forEach(s => {
        const x = scale(s.actual_start_time), w = Math.max(2, scale(s.actual_end_time)-x);
        const fill = s.segment_type === "BotToPod" ? "var(--leg1)" : "var(--leg2)";
        svg.appendChild(svgEl("rect", {x, y:cy-7, width:w, height:14, fill, rx:3}));
        if (s.actual_wait_time > 0) {
          const ww = Math.min(w, (W-left-right) * s.actual_wait_time / maxT);
          svg.appendChild(svgEl("rect", {x:x+w-ww, y:cy-7, width:ww, height:14, fill:"var(--wait)", opacity:.88, rx:3}));
        }
        const label = s.segment_type === "BotToPod" ? `P${s.pod_id}` : `S${s.station_id}`;
        if (w > 24) addText(svg, x+w/2, cy+4, label, {"text-anchor":"middle", fill:"#fff", "font-size":10});
      });
      y += rowH;
    });
    y += rankGap;
  });
}
function renderLegHeat() {
  const segs = data.runs.flatMap(r => r.segments).filter(s => s.actual_duration !== null);
  const max = Math.max(...segs.map(s => Math.abs(s.time_gap || 0)));
  const keys = [...new Set(segs.map(s => `${s.segment_type}|B${s.bot_id}|P${s.pod_id}|S${s.station_id}`))].slice(0, 14);
  let html = `<div class="heat"><div class="head">leg</div>${[1,2,3,4,5].map(r=>`<div class="head">r${r} gap</div>`).join("")}`;
  keys.forEach(k => {
    html += `<div class="edge">${k.replaceAll("|"," ")}</div>`;
    [1,2,3,4,5].forEach(r => {
      const s = segs.find(x => x.rank === r && `${x.segment_type}|B${x.bot_id}|P${x.pod_id}|S${x.station_id}` === k);
      const v = s ? s.time_gap : null;
      html += `<div style="background:${v===null?"#fff":colorHeat(Math.abs(v),max,28)}">${v===null?"":fmt(v,2)}</div>`;
    });
  });
  html += `</div>`;
  document.getElementById("legHeat").innerHTML = html;
}
function edgesFor(nodesText) {
  if (!nodesText) return [];
  const nodes = nodesText.split("|").filter(Boolean);
  const out = [];
  for (let i=0;i<nodes.length-1;i++) out.push(`${nodes[i]}→${nodes[i+1]}`);
  return out;
}
function renderEdgeHeat() {
  const counts = new Map();
  data.runs.forEach(run => {
    run.segments.forEach(s => {
      edgesFor(s.actual_path_nodes).forEach(e => {
        const key = e;
        if (!counts.has(key)) counts.set(key, {edge:key,total:0, ranks:{1:0,2:0,3:0,4:0,5:0}});
        const row = counts.get(key);
        row.total += 1;
        row.ranks[run.rank] += 1;
      });
    });
  });
  const rows = [...counts.values()].sort((a,b)=>b.total-a.total).slice(0,12);
  const max = Math.max(...rows.flatMap(r => [1,2,3,4,5].map(k=>r.ranks[k])));
  let html = `<div class="heat"><div class="head">edge</div>${[1,2,3,4,5].map(r=>`<div class="head">r${r}</div>`).join("")}`;
  rows.forEach(row => {
    html += `<div class="edge">${row.edge}</div>`;
    [1,2,3,4,5].forEach(r => {
      const v = row.ranks[r];
      html += `<div style="background:${colorHeat(v,max,205)}"><div class="nodebar"><span style="width:${max?100*v/max:0}%"></span></div><div>${v}</div></div>`;
    });
  });
  html += `</div>`;
  document.getElementById("edgeHeat").innerHTML = html;
}
init();
</script>
</body>
</html>
'@

$html = $htmlTemplate.Replace("__DATA_JSON__", $json)

$outDir = Split-Path $OutputHtml -Parent
if (![string]::IsNullOrWhiteSpace($outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}
Set-Content -Path $OutputHtml -Value $html -Encoding UTF8
Write-Host "Wrote $OutputHtml"
