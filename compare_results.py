import re, sys, glob, os

def load_stats(base_dir):
    pattern = os.path.join(base_dir, "*", "statistics.txt")
    files = glob.glob(pattern)
    if not files:
        return {}
    with open(files[0], encoding="utf-8") as f:
        txt = f.read()
    d = {}
    for line in txt.splitlines():
        m = re.match(r"\s*(\w+):\s*(.+)", line)
        if m:
            d[m.group(1)] = m.group(2).strip()
    return d

runs = [
    ("CBS  seed42", "output_compare/ecbs-test-cbs-42"),
    ("ECBS seed42", "output_compare/ecbs-test-ecbs-42"),
    ("CBS  seed0 ", "output_compare/ecbs-test2-cbs-0"),
    ("ECBS seed0 ", "output_compare/ecbs-test2-ecbs-0"),
]

base = r"C:/Users/Aesop/Desktop/rawsim-o-rmfs-world-models"
data = {}
for label, rel in runs:
    data[label] = load_stats(os.path.join(base, rel))

keys = [
    ("StatOverallOrdersHandled",     "Orders"),
    ("StatEnergyTotalKJ",            "Total E (kJ)"),
    ("StatEnergyPerOrderKJ",         "E/Order (kJ)"),
    ("StatEnergyE1AccelKJ",          "E1 Accel"),
    ("StatEnergyE3CruiseKJ",         "E3 Cruise"),
    ("StatEnergyE4RotationKJ",       "E4 Turn"),
    ("StatTurningCount",             "Turns"),
    ("StatTimingPathPlanningAverage","Avg PP (s)"),
    ("StatTimingPathPlanningOverall","Total PP (s)"),
    ("StatRealTimeUsed",             "Wall (s)"),
]

col_labels = [label for label, _ in runs]
header = f"{'Metric':<22}" + "".join(f"{l:>14}" for l in col_labels)
sep = "-" * (22 + 14 * len(runs))
lines = [header, sep]

for k, label in keys:
    row = f"{label:<22}"
    for run_label, _ in runs:
        v = data[run_label].get(k, "N/A")
        try:
            row += f"{float(v):>14.2f}"
        except:
            row += f"{'N/A':>14}"
    lines.append(row)

lines.append(sep)
lines.append("")
lines.append("ECBS vs CBS delta (seed42 / seed0):")
for k, label in [("StatEnergyPerOrderKJ","E/Order"), ("StatOverallOrdersHandled","Orders"), ("StatRealTimeUsed","Wall(s)")]:
    v_cbs42  = float(data["CBS  seed42"].get(k, 0))
    v_ecbs42 = float(data["ECBS seed42"].get(k, 0))
    v_cbs0   = float(data["CBS  seed0 "].get(k, 0))
    v_ecbs0  = float(data["ECBS seed0 "].get(k, 0))
    d42 = (v_ecbs42 - v_cbs42) / v_cbs42 * 100
    d0  = (v_ecbs0  - v_cbs0)  / v_cbs0  * 100
    lines.append(f"  {label:<12} seed42: {d42:+.1f}%   seed0: {d0:+.1f}%")

sys.stdout.buffer.write(("\n".join(lines) + "\n").encode("utf-8"))
