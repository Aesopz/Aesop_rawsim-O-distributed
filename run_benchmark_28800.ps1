$EXE   = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe"
$BASE  = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\Material\Instances\CoreBenchmark"
$INST  = "$BASE\benchmark_ss_layout.xlayo"
$SETT  = "$BASE\benchmark_28800_sett.xsett"
$OUT   = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\output_benchmark_28800"
$SEED  = "42"

$controllers = @("hadgs", "M1G", "sequ")

foreach ($ctrl in $controllers) {
    $conf = "$BASE\benchmark_controller_$ctrl.xconf"
    Write-Host "=== Running: $ctrl ===" -ForegroundColor Cyan
    Write-Host "Start: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    & $EXE $INST $SETT $conf $OUT $SEED
    Write-Host "End:   $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Host ""
}

Write-Host "=== All runs complete ===" -ForegroundColor Green
