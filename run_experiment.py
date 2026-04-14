"""
Run CBS and ECBS with seeds 1-30, up to PARALLEL workers at a time.
Usage: python run_experiment.py [--parallel N]
"""
import subprocess, os, sys, argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

BASE = Path(__file__).parent
INST = "Material/Instances/PP/1-4-4-32-550.xinst"
SETT = "Material/Instances/PP/PPMinimal.xsett"
CONFS = {
    "cbs":  "Material/Instances/PP/aesop-cbs.xconf",
    "ecbs": "Material/Instances/PP/aesop-ecbs.xconf",
}
CLI = "RAWSimO.CLI/RAWSimO.CLI.csproj"

def run_one(algo, seed):
    outdir = BASE / f"output_exp/{algo}/seed_{seed}"
    stat = outdir / "statistics.txt"
    if stat.exists():
        print(f"[SKIP] {algo} seed={seed}")
        return algo, seed, "skip"
    outdir.mkdir(parents=True, exist_ok=True)
    log = outdir / "run.log"
    cmd = [
        "dotnet", "run", "--project", str(CLI), "--no-build", "--",
        INST, SETT, CONFS[algo], str(outdir), str(seed)
    ]
    with open(log, "w") as f:
        result = subprocess.run(cmd, cwd=str(BASE), stdout=f, stderr=subprocess.STDOUT)
    status = "ok" if result.returncode == 0 else f"err({result.returncode})"
    print(f"[{status.upper()}] {algo} seed={seed}")
    return algo, seed, status

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--parallel", type=int, default=4)
    args = parser.parse_args()

    jobs = [(algo, seed) for algo in ["cbs", "ecbs"] for seed in range(1, 31)]
    print(f"Total jobs: {len(jobs)}, parallel={args.parallel}")

    with ThreadPoolExecutor(max_workers=args.parallel) as ex:
        futures = {ex.submit(run_one, algo, seed): (algo, seed) for algo, seed in jobs}
        for f in as_completed(futures):
            pass

    print("All done.")
