# BENCHMARK-INSTRUCTIONS.md — running the traffic-engine benchmarks on the target Windows machine

Everything below runs from the **repo root** in **PowerShell** on Windows. No SUMO required — all
benchmark scenarios are committed. Copy-paste the raw commands; the `.ps1` scripts are optional
convenience wrappers.

## 0. Prerequisites
- **.NET 8 SDK** (`dotnet --version` → 8.x). Install from https://dotnet.microsoft.com/download/dotnet/8.0.
- The repo, on `main` (or the high-density branch).
- Build once (Release — always benchmark Release, never Debug):
  ```powershell
  dotnet build Traffic.sln -c Release
  ```

## 1. What the benchmark tools are
- **`Sim.BenchCity`** — runs the engine on a scenario to completion and prints a metric block:
  `wall time`, `RTF (sim/wall)`, `steps/sec`, `peak concurrent`, `peak RSS`, `arrived`, and the
  `stuck` (gridlock) counts. This is the main throughput tool.
  ```powershell
  dotnet run -c Release --project src/Sim.BenchCity -- <scenarioDir> [flags]
  ```
  Useful flags: `--region` (region-parallel plan — use it for throughput), `--serial` (force single
  thread), `--max-parallelism N` (cap worker threads for a scaling sweep), `--no-fcd` (skip FCD write —
  **always use for timing**, FCD I/O dominates otherwise), `--steps N` (override step count),
  `--parity` / `--coordinated-lc` (lane-change mode, see §3).
- **`scripts/bench-scaling.ps1`** — thread-count scaling sweep on one scenario (wall time, speedup,
  parallel efficiency, optional SUMO baseline), writes a CSV. This is the existing core-scaling harness.

## 2. Committed benchmark scenarios (scale ladder)
| dir | scale | lanes | use |
|---|---|---|---|
| `scenarios/_bench/city-30` | ~30 concurrent | 1 | tiny / smoke |
| `scenarios/_bench/city-300` | ~300 concurrent | 1 | small |
| `scenarios/_bench/city-3000` | ~3,000 concurrent | 1 | **medium — the main scaling rung** |
| `scenarios/_bench/city-15000` | ~15,000 concurrent | 1 | **large — stress / peak throughput** |
| `scenarios/_bench/city-organic-L2` | ~620 veh | **2** | multi-lane organic (parity mode) |
| `scenarios/_diag/willpass-saturation` | 412 veh | **2** | **saturated multi-lane grid — the coordinated-LC A/B scenario** |

Steps default to the scenario's config end; pass `--steps N` to fix the horizon for a fair timing.

## 3. Benchmark A — engine throughput & core scaling (the primary numbers)
This uses the **default (parity) lane-change mode**, which is robust on every scenario. This is what to
report for "how fast is the engine on the target hardware."

**Single run, medium + large rungs (record wall time / RTF / steps-sec / peak RSS):**
```powershell
dotnet run -c Release --project src/Sim.BenchCity -- scenarios/_bench/city-3000  --region --no-fcd
dotnet run -c Release --project src/Sim.BenchCity -- scenarios/_bench/city-15000 --region --no-fcd
```

**Core-scaling sweep (speedup vs thread count) — the existing script:**
```powershell
pwsh scripts/bench-scaling.ps1 -Scenario scenarios/_bench/city-3000  -Repeats 5
pwsh scripts/bench-scaling.ps1 -Scenario scenarios/_bench/city-15000 -Repeats 5
```
It sweeps 1,2,4,…,coreCount threads, reports the median wall time + parallel efficiency, and writes a
CSV. Add `-Sumo` to also time single-threaded SUMO on the same net if `sumo` is on PATH (baseline).

**Manual thread sweep (if you prefer raw commands):**
```powershell
foreach ($t in 1,2,4,8,16) {
  dotnet run -c Release --project src/Sim.BenchCity -- scenarios/_bench/city-15000 --no-fcd --max-parallelism $t
}
```
Note: the trajectory is thread-count-independent (byte-identical serial vs parallel — verified), so only
wall time changes across the sweep.

## 4. Benchmark B — coordinated lane-change mode vs parity (multi-lane believability cost)
The **coordinated dense lane-change model** (`--coordinated-lc`) adds believable multi-lane
overtaking/merging. It is **OPT-IN** today (default = parity). Measured cost on dense grids: **perf-neutral
to slightly faster** (better flow offsets the extra LC work). A/B it on the saturated multi-lane grid:

```powershell
# parity (default) vs coordinated -- same scenario, 3 runs each, take the best wall time
1..3 | % { dotnet run -c Release --project src/Sim.BenchCity -- scenarios/_diag/willpass-saturation --steps 700 --region --no-fcd --parity }
1..3 | % { dotnet run -c Release --project src/Sim.BenchCity -- scenarios/_diag/willpass-saturation --steps 700 --region --no-fcd --coordinated-lc }
```
Or the convenience wrapper (does both, prints a comparison):
```powershell
pwsh scripts/bench-coordinated.ps1 -Scenario scenarios/_diag/willpass-saturation -Steps 700 -Repeats 3
```

### IMPORTANT caveat for coordinated mode
`--coordinated-lc` is validated on **grid** nets (the saturated grid, city grids). It is **NOT yet
robust on large ORGANIC nets** — it currently crashes on `city-organic-L2` (a lane-sequence desync being
hardened; see `docs/HIGH-DENSITY-P2G2-COOPERATIVE-LC-DESIGN.md` §3.7). So for Benchmark B use the
**grid** scenario (`willpass-saturation`), and run `city-organic-L2` in **parity mode only** for now.

## 5. What to record (per run)
- `wall time` (s) and `RTF (sim/wall)` — the headline throughput.
- `steps/sec` — engine tick rate.
- `peak concurrent` vehicles and `peak RSS` (MiB) — capacity / memory.
- `arrived` / `running@end` / `stuck (still, at sim end)` — sanity that the run drained (not gridlocked).
- Machine: CPU model, physical/logical cores, RAM, OS build; and `dotnet --version`.

Report the median (or best) of ≥3 repeats per configuration; the first run per config is JIT warm-up —
discard it.
