$EXE  = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe"
$BASE = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\Material\Instances\CoreBenchmark"
$INST = "$BASE\benchmark_ss_layout.xlayo"
$SETT = "$BASE\benchmark_sett.xsett"
$OUT  = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\output_benchmark_7200_multi"

$controllers = @("hadgs","M1G","sequ")
$seeds       = @(42, 43, 44, 45, 46)

New-Item -ItemType Directory -Force -Path $OUT | Out-Null

foreach ($ctrl in $controllers) {
    foreach ($seed in $seeds) {
        $conf = "$BASE\benchmark_controller_$ctrl.xconf"
        Write-Host "=== $ctrl seed=$seed ===" -ForegroundColor Cyan
        Write-Host "Start: $(Get-Date -Format 'HH:mm:ss')"
        & $EXE $INST $SETT $conf $OUT $seed
        Write-Host "End:   $(Get-Date -Format 'HH:mm:ss')"
    }
}

Write-Host "=== All 15 runs complete ===" -ForegroundColor Green
