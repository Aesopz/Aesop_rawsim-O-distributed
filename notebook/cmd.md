 dotnet run --project .\RAWSimO.Visualization\RAWSimO.Visualization.csproj 運行visualizer
 dotnet run --project .\RAWSimO.GymServer\ -- .\Material\Instances\CoreBenchmark\jenkinsinstance1.xinst Material\Instances\CoreBenchmark\jenkinssetting1.xsett Material\Instances\CoreBenchmark\jenkinsconfig_agentastar.xconf 7654 0.5 100 運行gymserver
 python .\scripts\collect_baseline.py --episodes 1 --output dataset/    收集baseline數據

  Headless 運行指令                             
dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- Material/Instances/CoreBenchmark/aesop.xlayo Material/Instances/CoreBenchmark/aesop.xsett  Material/Instances/CoreBenchmark/aesop.xconf output/ 42