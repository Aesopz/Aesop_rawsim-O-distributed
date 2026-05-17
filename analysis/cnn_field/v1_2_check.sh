#!/usr/bin/env bash
# V1.2 quick audit on HADGS+large baseline run

set -euo pipefail

RUN_DIR="C:/Users/Aesop/Desktop/EE-RAWSim-O_PP/.worktrees/feature/cnn-field-v1/out_v12_hadgs_large_s0"
ANALYSIS_DIR="C:/Users/Aesop/Desktop/EE-RAWSim-O_PP/analysis/cnn_field"

echo "==== KPI report ===="
KPI_CSV="$RUN_DIR/1-6-12-60-0.89-large-large_7200_300-hadgs-0/kpi_report.csv"
if [ -f "$KPI_CSV" ]; then
    head -2 "$KPI_CSV"
else
    echo "MISSING: $KPI_CSV"
fi

echo
echo "==== V1 Signal Audit on HADGS+large ===="
cd "$ANALYSIS_DIR"
python v1_signal_audit.py "$RUN_DIR" --filter "1-6-12-60-0.89-large" --csv-out v1_2_per_run.csv
