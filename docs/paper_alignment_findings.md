# Paper Alignment Findings: M1G / HADGS / SEQU

Scope: 10-robot small-layout benchmark for the OJOOPT paper, aiming to verify that the implementation semantics match the paper before interpreting KPI differences.

## Target Paper Pattern

Paper Table 8 for the small layout at `|R| = 10`, `|O| = 100` reports:

| Method | TP | RD | OD | PO |
|---|---:|---:|---:|---:|
| M1G | 314.5 | 14122 | 22.45 | 3.30 |
| HADGS | 313.48 | 14295 | 22.95 | 3.24 |
| SEQU | 270.92 | 17406 | 32.13 | 1.82 |

Expected qualitative pattern: M1G is slightly better than HADGS, and both are much better than SEQU.

## Verified Code / Config Alignments

- M1G objective uses paper weights: distance `1`, assignable-order reward `-40`, unused-capacity penalty `1000`.
- M1G objective rewards the paper's assignable-order variable (`yos`, corresponding to `hat{x}`), while actual allocation still uses `yaos` (`x`).
- M1G `xps` variables are created for both newly selected pods and already inbound pods, but the objective distance term is filtered through `Instance.ResourceManager.UnusedPods.Contains(u.pod)`. In the current code, this means the pod-to-station and robot-to-pod distance cost is charged only for new unused pods, not for inherited inbound `Pb` pods.
- M1G variable and constraint semantics match the paper's two-layer order-assignment model:
  - `yos` maps to `hat{x}_{o,w}` and is constrained by Eq. (2), Eq. (3), and Eq. (5).
  - `yaos` maps to `x_{o,w}` and is constrained by Eq. (3), Eq. (4), and Eq. (12).
  - The paper explicitly states after Eq. (3) that orders that can be assigned may not necessarily be assigned in the current decision, while orders actually assigned must be assignable. Therefore `yaos <= yos` is intentional, not a missing equality.
  - Eq. (5) also uses `hat{x}`, so using `yos` in the supply-capacity constraint is paper-aligned.
- The paper has an internal ambiguity in M1G objective Eq. (14): the displayed formula charges pod-to-station distance by `z_{p,w}`, while the explanatory paragraph later describes `f2(t)` with `d_{p,w} * delta_{o,p,w}`. The available author source and the current code both use `z/xps` for the pod-to-station distance term. A prior `dops/delta` objective ablation worsened KPI, so there is no current evidence that the code should be changed to `delta`.
- M1G and HADGS robot-to-pod / pod-to-station estimates use shortest-path distance when waypoint information is available, with coordinate fallback only for transient waypoint-null states.
- HADGS LSMM uses the paper-aligned sampling structure: `l1 = 10` pod-set evaluations and `l2 = 3` local searches.
- HADGS pod-set sampling now constructs full `E(o)` combinations before sampling, matching Algorithm 4 more closely.
- HADGS normal OA scorer direction was re-checked. `BestCandidateSelector(true)` would mean maximize, but `HeuristicsPOAandPPS()` calls `_bestCandidateSelectNormal.Recycle2()` before each normal order-selection pass, and `Recycle2()` flips the selector to minimization. Therefore the current normal OA path minimizes the inbound-pod distance scorer; it is not selecting the farthest order. One caveat remains: `BestCandidateSelector.ReassessMin()` only evaluates the primary scorer, so configured tie-breakers are effectively ignored in this minimization path.
- KPI `order` semantics are output-side picking orders, not input-side replenishment bundles:
  - `StatOverallOrdersHandled` increments only in `NotifyOrderCompleted(Order, OutputStation)`.
  - `StatOverallBundlesHandled` increments separately in `NotifyBundleStored(InputStation, ...)` and is not used as the KPI order denominator.
  - `kpi_report.csv` uses `StatOverallOrdersHandled` for `orders_completed`, `orders_per_hour`, `order_distance_m`, and `system_order_pile_on`.
  - `stationstatistics.csv` output-station `PileOn` is item pile-on (`StatItemPileOn`), not paper-style order pile-on; Table 8 comparison should use `kpi_report.csv` `system_order_pile_on`.
- Table 7 controller policy has been aligned in the three benchmark controller files. The surrounding decision policies are held constant, and only the order-assignment / pod-selection / task-allocation decision block differs:

| Table 7 item | SEQU config | M1G config | HADGS config |
|---|---|---|---|
| OA / PS / TA decision | `PodMatchingOrderBatchingConfiguration` + Demand pod scorers + Balanced TA | `M1GConfiguration` | `HADGSConfiguration` |
| PR | `NearestPodStorageConfiguration` | same | same |
| PP | `WHCAnStarPathPlanningConfiguration` | same | same |
| ROA / RPS / RTA | `SamePodReplenishmentBatchingConfiguration`, `EmptiestItemStorageConfiguration`, `FirstStationRule = LeastBusy` | same | same |
| batching tie-breakers | `TieBreaker = Random`, `FastLane = false`, `FastLaneTieBreaker = Random` | same | same |

- HADGS stale inbound-pod cleanup is guarded against pods that remain registered inbound but are no longer present in `_usedPods`. This is a robustness fix; the seed-42 smoke KPI did not change after this guard.
- Fast-lane is disabled in all three Table 7-style benchmark controllers (`FastLane=false` in M1G, HADGS, and SEQU). Therefore the missing fast-lane second assignment pass is not expected to affect the current Table 8 reproduction attempt.

## Validation Runs

Build command:

```powershell
dotnet build RAWSimO.CLI\RAWSimO.CLI.csproj -c Release -p:Platform=x64
```

Latest validation after removing the temporary M1G post-solve counters passed with `5 warnings, 0 errors`. The warnings are existing unused-field warnings plus the already-unused `M1GManager.IsOd` field; no build error was introduced.

### Policy-Aligned O100, Current Gamma Mu-100

Result folder:

```text
Results\BenchmarkTable7PolicyAlignedO100Seeds42_46
```

Five-seed average:

| Method | TP | RD | OD | PO | energy/order |
|---|---:|---:|---:|---:|---:|
| M1G | 321.6 | 15574.70 | 24.22 | 3.06 | 9.27 |
| HADGS | 326.3 | 14379.85 | 22.04 | 3.38 | 9.23 |

This preserves the broad M1G/HADGS advantage over SEQU seen in seed 42, but does not reproduce the paper pattern where M1G is slightly better than HADGS.

Latest HADGS stale-inbound guard smoke run:

```text
Results\BenchmarkHADGSInboundGuardO100Seed42
```

Seed 42 completed successfully and matched the prior policy-aligned HADGS KPI: TP `328`, RD `14074.85`, OD `21.46`, PO `3.47`, energy/order `9.05`.

Temporary M1G `dops` / `Ziops` diagnostic run:

```text
Results\BenchmarkM1GDiagO100Seed42
```

Seed 42 KPI matched the previous policy-aligned M1G run: TP `323`, RD `15655.82`, OD `24.24`, PO `3.09`, energy/order `9.30`.

The final diagnostic lines in `output.log` show:

```text
[M1G-DIAG] solveCalls=800 solutions=800 yos=2543 yaos=647 xps=2579 yrp=210 dops=213 unusedDopsPods=0 ziopsEntries=918 ziopsUnits=1408 allocatedOrders=646
[M1G-DIAG] solveCalls=810 solutions=810 yos=2574 yaos=656 xps=2604 yrp=213 dops=216 unusedDopsPods=0 ziopsEntries=930 ziopsUnits=1423 allocatedOrders=655
```

Interpretation: the suspected M1G issue where selected `dops` pods are dropped by the greedy `Ziops` post-processing is not supported in this run. `unusedDopsPods=0` throughout the tail of the run, and the seed-42 KPI stayed unchanged. The small gap between cumulative `allocatedOrders` and completed orders is expected because some station assignments can remain active or unfinished at simulation end.

### O100 With Exponential Lambda 0.5 SKU Popularity

Result folder:

```text
Results\BenchmarkO100Exp05PolicyAlignedSeed42
```

Seed 42:

| Method | TP | RD | OD | PO | energy/order |
|---|---:|---:|---:|---:|---:|
| M1G | 313.0 | 18787.06 | 30.01 | 2.40 | 9.94 |
| HADGS | 317.5 | 19135.94 | 30.14 | 2.24 | 11.44 |
| SEQU | 275.0 | 21497.90 | 39.09 | 1.68 | 15.09 |

Switching only to the Appendix A.1 exponential-lambda SKU popularity does not recover the Table 8 pattern. It changes the distance scale substantially and still leaves HADGS ahead on TP.

Full seeds 42-46 validation:

```text
Results\BenchmarkO100Exp05PolicyAlignedSeeds42_46
```

Five-seed average:

| Method | TP | RD | OD | PO | energy/order |
|---|---:|---:|---:|---:|---:|
| M1G | 311.1 | 18939.62 | 30.44 | 2.30 | 10.07 |
| HADGS | 321.4 | 19023.24 | 29.61 | 2.33 | 11.13 |
| SEQU | 275.1 | 21650.77 | 39.37 | 1.68 | 15.05 |

This confirms that aligning the SKU popularity distribution to Appendix A.1 is not enough to reproduce Table 8. M1G has slightly lower RD than HADGS in this generated stream, but HADGS remains clearly higher in TP and slightly better in OD/PO. Both still outperform SEQU.

### Non-Reproducible Appendix Bundle Seed 42 Artifact

An older result folder exists:

```text
Results\BenchmarkAppendixBundleO100Seed42
```

Seed 42 KPI from that folder:

| Method | TP | RD | OD | PO | energy/order |
|---|---:|---:|---:|---:|---:|
| M1G | 325.5 | 14792.93 | 22.72 | 3.14 | 8.99 |
| HADGS | 326.5 | 14196.35 | 21.74 | 3.25 | 8.54 |
| SEQU | 278.0 | 18178.02 | 32.69 | 2.16 | 13.80 |

This artifact has a distance scale closer to Table 8 than the current Gamma and generated exponential runs, but it still does not reproduce the paper pattern because HADGS remains ahead of M1G on TP, OD, PO, and energy/order.

Important limitation: this folder references `Material\Instances\CoreBenchmark\benchmark_O100_appendix_sett.xsett`, but that setting file is not currently present in the workspace, and the output folder only preserves `setting.txt` with the setting name, not the original XML. Therefore this artifact is not currently reproducible and should not be treated as a validation baseline unless the missing `.xsett` is recovered.

## Remaining Reproducibility Blocker

The author source tree currently available at:

```text
C:\Users\Aesop\AppData\Local\Temp\EE-RAWSim-O-paper-src-master
```

does not contain an exact Table 8 artifact for 10 robots, 100 pods, 2 picking stations, 2 replenishment stations, and `|O| = 100`.

Observed source artifacts:

- `Material\Instances\CoreBenchmark` contains old / unrelated `.xinst` files such as:
  - `jenPPinstance.xinst`: 6 bots, 165 pods, 1 output station, 1 input station.
  - `1-7-25-70-699.xinst`: 70 bots, 699 pods, 25 output stations, 7 input stations.
  - `1-3-3-15-64.xinst`: 15 bots, 64 pods, 3 output stations, 3 input stations.
- Author `SettingGenerator.GenerateRotterdamMark2Set()` creates:
  - `SimulationDuration = 86400`
  - `OrderCount = 200`
  - order lines max `4`
  - units per line max `3`
  - SKU generator `Mu-100.xgenc`, `Mu-500.xgenc`, or `Mu-1000.xgenc`
  - bot counts via bots-per-output-station values `2, 4, 6, 8`, not the exact 10-robot Table 8 point.
- Current `Mu-100.xgenc` and author `Mu-100.xgenc` use Gamma SKU popularity in the generator description, not the Appendix A.1 exponential `lambda = 0.5` distribution.
- In `ItemManager`, SimpleItem order generation filters SKU choices by currently available inventory before sampling, so the realized order stream depends on dynamic stock state and is not just a static distribution from Appendix A.1.

External artifact check:

- The available source clone points to `https://github.com/LBJiao/EE-RAWSim-O.git`.
- Remote refs contain only `master` and `main`; no tags or releases were found.
- `origin/main` does not contain an alternate experiment dataset; relative to `origin/master`, it only adds `README.md`.
- GitHub search / repository inspection did not reveal a supplemental Table 8 dataset, order stream, exact seed set, or exact 10-robot small-layout instance.
- ScienceDirect / DOI search for supplementary data and the ResearchGate entry did not reveal a separate dataset or exact experiment artifact.

## Demand Generation Audit

The order-generation mismatch is now a concrete remaining suspect, not just a guess.

Parameter-table audit for the current main O100 run (`benchmark_ss_layout.xlayo` + `benchmark_O100_sett.xsett` + `benchmark_controller_*.xconf`):

| Paper parameter | Current test artifact | Status |
|---|---|---|
| small-scale quantity of pods `100` (Table 5) | run artifact and `benchmark_ss_layout.xlayo` produce 100 pods | aligned |
| small-scale storage locations `120` (Table 5) | `benchmark_ss_layout.xlayo` has `PodAmount=0.84`; run artifact footprint shows 100 pods and 120 storage locations | aligned |
| small-scale picking workstations `2` (Table 5 / Table 6 `|W|=2`) | `NPickStationWest=2` | aligned |
| small-scale replenishment workstations `2` (Table 5) | `NReplenishmentStationEast=2` | aligned |
| picking workstation capacity `6` (Table 5) | `OStationCapacity=6` | aligned |
| length of queuing zone `9` (Table 5) | run footprint shows queue-length setting `9`; generated layout has the expected small-layout station queues | aligned |
| small-scale `|O|=100`, `|R|=10`, `delta=1` Table 8 point | `OrderCount=100`, `BotCount=10`; OJOOPT trigger threshold is implemented/configured as `delta=1` | aligned |
| initial inventory `70%` | `benchmark_O100_sett.xsett` `InitialInventory=0.7` | aligned |
| pod storage capacity `100 slots` for 100-SKU case | `benchmark_ss_layout.xlayo` `PodCapacity=100` | aligned |
| SKU size / item weight uniform `2..8` | runtime uses `Mu-100.xgenc` `ItemDescriptionWeights`; generator description says uniform `2..8` | aligned in runtime, despite `.xsett` fallback fields being `5..5` |
| SKU type count `100` | `Mu-100.xgenc` has `ItemDescriptionCount=100`; run artifact has 100 pods | aligned |
| SKU popularity exponential, `lambda=0.5` | current main `benchmark_O100_sett.xsett` uses `Mu-100.xgenc`, whose generator description says `ProbWeightDistributionType=Gamma`; experimental `benchmark_O100_exp05_sett.xsett` uses `Mu-100-exp-lambda-0.5.xgenc` | not aligned in the main O100 run |
| order lines truncated normal `mu=1`, `sigma=1`, `min=1`, `max=5` | `OrderPositionCountMean=1`, `StdDev=1`, `Min=1`, `Max=5` | aligned |
| items per order line truncated normal `mu=1`, `sigma=1`, `min=1`, `max=5` | `PositionCountMean=1`, `StdDev=1`, `Min=1`, `Max=5` | aligned |
| replenishment backlog `200` | `BundleCount=200` | aligned |
| replenishment order quantity uniform `4..12` | runtime uses `.xgenc` `ItemDescriptionBundleSizes`; `Mu-100.xgenc` generator description says uniform `4..12` | aligned in runtime, despite `.xsett` fallback fields being `6..6` |
| inventory thresholds `r1=65%`, `r2=85%` | `InventoryLevelBundleRestartThreshold=0.65`, `InventoryLevelBundleStopThreshold=0.85` | aligned |
| pick time `3s` | `ItemPickTime=3` | aligned |
| hand item at picking workstation `10s` | `ItemTransferTime=10` | aligned |
| put item on pod `10s` | `ItemBundleTransferTime=10` | aligned |
| replenishment workstation capacity `1000 slots` | `IStationCapacity=1000` | aligned |
| acceleration / deceleration `1 m/s^2` | `MaxAcceleration=1`, `MaxDeceleration=1` | aligned |
| maximum velocity `1.5 m/s` | `MaxVelocity=1.5` | aligned |
| turn time `2.5s` | `TurnSpeed=2.5` | aligned |
| lift/lower pod `2.2s` | `PodTransferTime=2.2` | aligned |

Run-artifact sanity check from `BenchmarkTable7PolicyAlignedO100Seed42`: 10 bots, 100 pods, 2 input stations, and 2 output stations were present. The current experiment uses `.xlayo` generation, not a fixed `.xinst`, for this small layout.

Relevant runtime behavior:

- `InstanceIO` resolves `InventoryConfiguration.SimpleItemConfiguration.GeneratorConfigFile` to the `.xgenc` resource before the item manager starts.
- `ItemManager` reads the `.xgenc` into `_simpleItemGeneratorConfig`.
- For each SKU, `ItemManager` uses `.xgenc` `ItemDescriptionWeights` when present; only if they are absent does it use `InventoryConfiguration.ItemWeightMin/Max`.
- For each SKU, `ItemManager` uses `.xgenc` `ItemDescriptionBundleSizes` when present; only if they are absent does it use `InventoryConfiguration.BundleSizeMin/Max`.
- Real order generation samples the number of order lines from `OrderPositionCount*` and units per line from `PositionCount*`, then samples SKUs from the currently available inventory. This inventory filter means two configs with the same static SKU weights can still diverge if replenishment / stock state differs.

Source-code parity check:

- `RAWSimO.Core\Management\ItemManager.cs` matches the available author source version exactly.
- `RAWSimO.Core\Generator\OrderGenerator.cs` matches the available author source version exactly.
- `RAWSimO.Core\IO\InstanceIO.cs` matches the available author source version exactly.

This means the remaining order-generation mismatch is not currently explained by a changed generator implementation in this checkout.

Implication:

- Editing only `.xsett` fields such as `ItemWeightMin`, `ItemWeightMax`, `BundleSizeMin`, and `BundleSizeMax` is not enough when the selected `.xgenc` already supplies per-SKU weights and bundle sizes.
- `benchmark_O100_sett.xsett` uses `Mu-100.xgenc`, whose description says `ProbWeightDistributionType=Gamma`.
- `benchmark_O100_exp05_sett.xsett` uses the experimental `Mu-100-exp-lambda-0.5.xgenc`, but seed-42 validation showed that simply switching to this generated exponential resource changes the distance scale and still does not reproduce Table 8.
- The paper's Appendix A.1 parameters are therefore insufficient to uniquely reconstruct the Table 8 order stream unless the exact `.xgenc`, instance, and seed set used by the authors are available.

## Completion Audit

Objective: make the 10-robot small-layout M1G/HADGS/SEQU benchmark align with the paper, with implementation correctness more important than blindly forcing KPI values.

Checklist:

| Requirement | Evidence | Status |
|---|---|---|
| M1G paper objective semantics checked | `yos` maps to paper `hat{x}`, `yaos` maps to actual assignment `x`; objective rewards `yos`; actual allocation uses `yaos` | done |
| M1G `yos` / `yaos` constraint semantics checked | Eq. (2), Eq. (3), Eq. (4), Eq. (5), and Eq. (12) map to current `yos`, `yaos`, `xps`, `dops`, and `us` structure; `yaos <= yos` is paper-aligned | done |
| M1G pod-distance objective variable checked | Paper Eq. (14) display and author source use `z/xps`; paper explanatory paragraph has an ambiguous `delta` wording, but no evidence supports changing the current objective to `dops/delta` | done |
| M1G/HADGS distance semantics checked | shortest-path distances are used when waypoints exist, with coordinate fallback | done |
| HADGS LSMM parameters checked | local search count and sampled pod-set count are paper-aligned | done |
| HADGS pod-set generation checked | full `E(o)` combinations are generated before sampling | done |
| HADGS normal OA scorer direction checked | `Recycle2()` flips the normal selector to minimization before OA selection, so the primary distance scorer is not maximized; only min-mode tie-breaker use remains weak | done |
| Fast-lane relevance checked | `FastLane=false` in M1G, HADGS, and SEQU benchmark controllers, so missing second pass is not active in Table 8-style runs | done |
| M1G `dops` / `Ziops` post-processing checked | temporary seed-42 diagnostics showed `unusedDopsPods=0` and unchanged KPI; no evidence that greedy post-processing drops selected support pods in this run | done |
| Table 7 policy config checked | three controller files hold PR/PP/ROA/RPS/RTA and downstream PS/TA policy constant, with only SEQU/M1G/HADGS core decision differing | done |
| Order-generation code parity checked | `ItemManager.cs`, `OrderGenerator.cs`, and `InstanceIO.cs` match the available author source exactly | done |
| KPI validation run | build succeeds and controlled seed / 5-seed runs completed | done |
| Paper-like KPI pattern achieved | current 5-seed O100 result has HADGS ahead of M1G, not M1G slightly ahead | not achieved |
| Exact Table 8 artifact located | no matching 10-robot / 100-pod / 2+2 station / O100 instance-setting-order artifact found in available paper source, GitHub branches, tags, or releases | not achieved |
| Appendix A.1 demand stream reconstructed | generated exponential `lambda=0.5` variant was tested for seeds 42-46 and still does not reproduce Table 8 | not achieved |

Audit result: implementation alignment is substantially improved and no remaining confirmed M1G/HADGS semantic bug is currently identified, but the overall goal is not complete because the exact paper KPI pattern is not reproduced and the exact Table 8 demand artifact remains missing.

## Current Conclusion

The remaining mismatch should not be treated as proof that M1G/HADGS implementation is wrong. The strongest current evidence is that the exact Table 8 demand stream / experiment artifact / seed set is missing or not represented by the available author source and paper text.

Do not further tune M1G or HADGS objective terms to force the KPI pattern unless a concrete paper-to-code semantic mismatch is found.
