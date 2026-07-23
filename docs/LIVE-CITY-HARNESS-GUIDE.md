# Live-city test harness — how to run it (orientation for a fresh/compacted session)

> **Who this is for.** Any session (esp. one that lost context to compaction) that needs to run the
> **live-city coupled cars+peds demo** — e.g. to investigate **issue #15 (junction gridlock)**. It's
> written to be self-contained: what the harness is, how to run it headless, the knobs, and exactly
> how to reproduce/measure the gridlock. Companion to `docs/_windows-test-session-report.md` (the
> findings) and `docs/LIVE-CITY-WINDOWS-TESTING-BOOTSTRAP.md` (the GPU/Godot viewer side).

## What it is
`Sim.LiveCity.LiveCitySim` (`src/Sim.LiveCity/LiveCitySim.cs`) is the coupled host: it loads the
downtown scene, spawns cars on a crop, steps a ped crowd + crossing-yield gate + the vehicle `Engine`,
and publishes both onto in-memory replication wires. Scene = **`scenarios/_ped/demo_city/box/`**
(committed; `net.xml` + `buildings.json` + `pois.json`). The downtown **crop** `[2055,2055]–[2895,2895]`
(~840 m) is where cars spawn and render; it's a `LiveCityConfig` constant.

## Run it HEADLESS (no GPU/Godot needed — this is your loop)
The 2D Raylib viewer's `--smoke` path runs the **exact same `LiveCitySim`** headless, N steps, then
exits — no window:
```bash
dotnet run --project src/Sim.Viewer -c Release -- --mode live-city --smoke --frames 400
# 400 render frames -> max(frames,120) steps; at sim-hz 2 (Dt 0.5s) that's ~200s of sim.
# Prints one LIVECITY-SMOKE summary line (peakCars/peakPeds/carYieldObservations/...).
```
Env knobs (all optional, resolved in `LiveCityConfig.ForRepoRoot`):
- `LIVECITY_CARS=<n>` concurrent car cap (default 160)
- `LIVECITY_PEDS=<n>` concurrent ped cap (default 160; also scales ped spawn rate) — *added by the
  testing session*
- `LIVECITY_YIELD=0` turn the crossing-yield gate OFF (A/B)
- `LIVECITY_LCMIN=<mps>`, `LIVECITY_HZ=<hz>` (sim tick rate)

**Offline parity gate (must stay green, no SUMO/network):** `dotnet test tests/Sim.ParityTests -c
Release` → **654 passed / 4 skipped**. Any engine change for #15 must keep this green (or be gated).

## Reproducing / measuring #15 (junction gridlock) — the method the report used
The `LIVECITY-SMOKE` summary does NOT show movement, so the testing session added a **throwaway**
probe to the smoke loop (`RunLiveCitySmoke` in `src/Sim.Viewer/Program.cs`) — re-add it to iterate:

1. **Stopped-fraction** (are cars moving?): each step, per car, compute displacement from the previous
   step (`snap.Cars[i]` X/Y via `_liveCitySource`/`sim.Sample()`); count `disp < 0.05 m` (=<0.1 m/s)
   as stopped; log `stopped/total` + `avgSpeed` every ~40 steps.
2. **Arrival rate** (are cars reaching destinations?): `LiveCitySim` has no arrival counter — the
   testing session temporarily tallied `_engine.Events` where `SimEventKind.Arrived` right after
   `_engine.Step()` and exposed a cumulative `ArrivedTotal`. Log arrivals-per-interval.

**What it showed (report #15):**
- Stopped-fraction climbs from ~0.09 (free flow) to **0.8–0.97 within ~80 s**, and is **INDEPENDENT
  of car count** (70 vs 160 identical) → NOT saturation/spillback.
- **Arrivals are near-zero throughout** (~16 in 200 s for ~64 cars; 0 for the first 100 s even while
  cars moved at 11.7 m/s) → cars **have** destinations but almost never **reach** them.
- Conclusion: **junction discharge / right-of-way bug** — cars can't get *through* junctions — and
  with **no teleport ported** (`grep -r teleport src/Sim.Core` = 0 hits; phase-1 keeps it off for
  determinism) nothing ever breaks the jam, so it's terminal.

## Candidate fixes for #15 (on other branches, NOT merged into the live-city line)
- **`claude/dense-lane-overlap-fix-5tr4ha`** — junction/signalized discharge deficit (the theme):
  landed `f69a58d` permissive/minor-crossing yield (`MSLink::blockedByFoe` port — over-yield =
  "waiting on green"), `ca8d515` arriving-vehicle red-light-brake / signalized-discharge; dominant
  unresolved cause = **turn-lane mis-segregation** (`9a77d3b`/`ad8d738`, WIP).
- **`claude/c4vii-willpass-gridlock-lktroh`** — `996493d` deterministic **right-before-left deadlock
  break** (symmetric conflict cycle; `e7a76f0` diagnoses it), parity-reviewer ACCEPT.
- **`claude/sumosharp-junction-row-issue2`** / **`claude/permissive-yield-crossing-wip`** — related
  "blanket crossing-yield vs arrival-time RoW".

Likely a combination (turn-lane segregation + blockedByFoe over-yield + right-before-left deadlock),
plus the missing teleport as the reason it never recovers. Integrate the landed pieces into the
live-city line, re-run the headless probe above, and watch stopped-fraction fall + arrivals rise.

## Direct SUMO cross-check (network-enabled, for parity)
SUMO 1.20.0 is available (`sumo --version`). Run SUMO on the same `net.xml` + a comparable demand and
diff tripinfo/summary (arrivals, mean speed) against the engine to localize the discharge gap — the
standard engine-vs-SUMO diagnosis (see `CLAUDE.md`).

## Key files
- `src/Sim.LiveCity/LiveCitySim.cs` — `Step()` (spawn cars → ped demand → crossing gate → `Engine.Step`),
  `SampleCars()`/`Sample()`, the InterestField ORCA pocket (promote 70 m / demote 100 m).
- `src/Sim.LiveCity/LiveCityConfig.cs` — crop, caps, env knobs.
- `src/Sim.Core/Engine.cs` + `CommandBuffer.cs` — vehicle engine; arrival = `CommandBuffer` `Arrived`
  (Destroy analog), lifecycle events on `Engine.Events`. **No teleport implemented.**
