 dotnet run --project .\RAWSimO.Visualization\RAWSimO.Visualization.csproj 運行visualizer
 dotnet run --project .\RAWSimO.GymServer\ -- .\Material\Instances\CoreBenchmark\jenkinsinstance1.xinst Material\Instances\CoreBenchmark\jenkinssetting1.xsett Material\Instances\CoreBenchmark\jenkinsconfig_agentastar.xconf 7654 0.5 100 運行gymserver
 python .\scripts\collect_baseline.py --episodes 1 --output dataset/    收集baseline數據

Headless 運行指令                             
dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- Material/Instances/CoreBenchmark/aesop.xlayo Material/Instances/CoreBenchmark/aesop.xsett  Material/Instances/CoreBenchmark/aesop.xconf output/ 42

│                                                                                                                                                                                                                           │
│ # 1. 編譯                                                                                                                                                                                                                 │
│ dotnet build RAWSimO.sln                                                                                                                                                                                                  │
│                                                                                                                                                                                                                           │
│ # 2. TC-ON 測試：確認 0 collisions + 吞吐量持平                                                                                                                                                                           │
│ dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- \                                                                                                                                                                  │
│   Material/Instances/CoreBenchmark/aesop-test100.xlayo \                                                                                                                                                                  │
│   Material/Instances/CoreBenchmark/aesop-tc-on.xsett \                                                                                                                                                                    │
│   Material/Instances/CoreBenchmark/aesop.xconf output/occupancy_test_on/ 42                                                                                                                                               │
│                                                                                                                                                                                                                           │
│ # 3. TC-OFF 測試：確認安全層獨立運作                                                                                                                                                                                      │
│ dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- \                                                                                                                                                                  │
│   Material/Instances/CoreBenchmark/aesop-test100.xlayo \                                                                                                                                                                  │
│   Material/Instances/CoreBenchmark/aesop-tc-off.xsett \                                                                                                                                                                   │
│   Material/Instances/CoreBenchmark/aesop.xconf output/occupancy_test_off/ 42 