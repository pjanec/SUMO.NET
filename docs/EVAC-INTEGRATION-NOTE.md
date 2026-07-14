# Integration note — merging the panic-evacuation work into `main`

For the session that owns `main`. This is a hand-off: the panic-evacuation feature is complete on a
feature branch, already rebased/merged onto the latest `main`, and green. Below is exactly what it
contains, the one thing to review, and how to verify after you merge.

## Branch
`claude/sumo-phase-2-planning-p3w7kh-i1gsgu` — already merged with current `main`
(merge commit is the branch tip; it includes your native-viewer + right-before-left junction-fix work).

## Current state (verified on the merged branch)
- `dotnet build -c Release` clean (incl. your `Sim.Viewer` / `Sim.Replication.Dds` projects).
- `dotnet test -c Release` → **440 passed / 3 skipped / 0 failed** — every evac test **and** your
  `RblLeftTurnsDiagTests` pass together.
- Determinism hash **`909605E965BFFE59` unmoved** (single + parallel). Your junction fix does not touch
  the `highway-dense` determinism scenario, so the parity baseline is unchanged.
- The merge had **no conflicts**; the only two files touched on both sides (`src/Sim.Core/Engine.cs`,
  `src/Sim.Core/VehicleRuntime.cs`) auto-merged and both changes were verified functionally intact
  (my reroute regression test + your junction diag test both pass).

## What the branch adds
**Overwhelmingly additive** — a new external layer + tests + docs + demo scenarios, none on the golden
path:
- `src/Sim.Evac/` — the panic-evac layer (parity-exempt; drives the core via public seams only).
- `src/Sim.EvacProfile/` — the evac cost profiler + crowd-solver micro-benchmark.
- `src/Sim.Viz/` — new evac scene builders (`BuildEvacGrid`/`BuildEvacOrganic`/`BuildEvacCity`) +
  `--evac-organic` / `--evac-city` modes.
- `tests/Sim.ParityTests/` — evac behavioral tests + bit-identity tests for the crowd spatial hashes.
- `scenarios/evac-grid/`, `scenarios/evac-grid-tls/` — demo nets. (The organic/city demos reuse the
  already-committed `scenarios/_bench/city-organic-L2` and `city-15000`.)
- `docs/PANIC-EVAC*.md` — design/tasks/tracker per phase, indexed by **`docs/PANIC-EVAC-OVERVIEW.md`**.

## The ONE thing to review: two small parity-CORE commits
Everything else is additive; these two touch `Sim.Core` and are the only bits that interact with your
engine work. Both are isolated, regression-tested, and proven **hash-inert** (the determinism hash is
unchanged with them in):

1. **`045e472` — multi-lane active-reroute fix** (`src/Sim.Core/Engine.cs`). A latent bug: an *active*
   vehicle rerouted onto a multi-lane edge that its **original** route never contained made the
   strategic-lane-change read the stale original route and throw
   `InvalidDataException: Edge 'X' is not part of the given route`. Fix: a rerouted vehicle gets its own
   synthetic route id (`_effectiveRouteIdByEntity` / `EffectiveRouteId` / `RegisterRerouted`); only the
   two active-vehicle hot-path reads are rerouted, all insertion-time reads untouched. This is *more*
   SUMO-faithful (SUMO replaces the route on reroute). Golden-inert: the table is empty unless reroute
   actually fires (off by default, `RerouteThresholdSeconds = +inf`). Guarded by
   `RungB3MultilaneRerouteRegressionTests` (throws pre-fix, clean post-fix). **RungB3's diamond net is
   single-lane, which is why this stayed latent — the new test uses a 2-lane net.**

2. **`baf5098` — `VehicleRuntime.VType` made settable** (`src/Sim.Core/VehicleRuntime.cs`), for the
   `SetVehicleParams` seam the flee preset uses. Purely additive; no golden scenario calls it. (Note:
   your junction fix also added fields to `VehicleRuntime.cs`; they auto-merged cleanly.)

If either of these conflicts with in-flight engine work on your side, they are the *only* places to look.

## A viz change worth knowing about (not parity-affecting)
`src/Sim.Viz/template.js` + `Payload.cs` gained **stable per-entity disc slots** (`FramePayload.D` is now
`double[]?[]`; `interpolatedDiscs` handles null slots like `interpolatedVehicles`). This fixed a
frame-to-frame identity-smear artifact and affects **all** disc-bearing viz scenes — it's an improvement,
verified, and viz is never in `dotnet test`. No action needed; just so it isn't a surprise in the diff.

## How to verify after merging into `main`
```bash
dotnet build -c Release
dotnet test  -c Release            # expect 440 passed / 3 skipped / 0 failed
dotnet run   -c Release --project src/Sim.Bench --no-build   # expect hashA = hashPar = 909605E965BFFE59
```
If all three hold, the integration is clean. The feature index (`docs/PANIC-EVAC-OVERVIEW.md`) has the
demo commands if you want to eyeball the replays.
