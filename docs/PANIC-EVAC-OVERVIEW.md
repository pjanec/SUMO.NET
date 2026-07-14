# Panic-Evacuation — feature overview (final state)

The canonical index for the **panic-evacuation** capability: what it is, how it is layered on the parity
engine, what shipped, how to run it, and where the detailed docs live. This is the "so we don't forget
what the repo contains" summary; the per-phase design/tasks/tracker docs (linked below) carry the detail.

## What it is
Organized urban traffic that, on a **localized security incident**, transitions into a **panic
evacuation**: cars near the incident panic and flee → the streets jam → boxed-in drivers abandon their
cars and flee **on foot** → the foot crowd streams outward to safety, while distant traffic keeps flowing
normally. It is a demonstration of the engine's "live reactivity" seam taken to its limit, on top of the
unchanged SUMO-parity driving core.

Requirements live in **`docs/PANIC-EVAC.md`** (R1–R10). The load-bearing ideas:
- **Panic is local information** (R3): contagion + line-of-sight + jam-unease, no global broadcast.
- **The core is only ever DRIVEN, never forked** (R9): the evac layer is *parity-exempt* — it drives the
  engine through public seams and nothing it does is on the golden path, so with panic off the driving
  core stays byte-identical (determinism hash `909605E965BFFE59` unmoved).
- **The evacuation is LOCAL** (R3, Phase-5 principle): the evac layer attaches only to a bounded
  **working region** around the incident, so its cost scales with the *local* affected population, not
  the city size — a 10k-vehicle city still only evacuates the hundreds-to-low-thousands near the incident.

## Architecture (one layer, `src/Sim.Evac/`)
`Sim.Evac` sits entirely above `Sim.Core`, driving it via public seams only (`SetVehicleParams`,
`SetDestination`/`Reroute`, `Despawn`, `AddObstacle`, `CrowdSource`, `GetDrModel`, the read buffer). Key
components:

| Component | Role |
|---|---|
| `EvacDirector` | the orchestrator — one coordinated tick = PreStep → `engine.Step()` → PostStep |
| `Incident` | epicentre, radius, start time |
| `FearField` + `LineOfSight` | per-vehicle fear (contagion + occlusion-gated LoS + jam-unease); monotone panic latch |
| `BlockedDetector` | a car is "blocked" once its DR regime has been Stationary for a dwell |
| `VehicleMover` (wraps `MixedTrafficCrowd`) | the Orca-**push** stage: a blocked+panicked car noses onto the shoulder as a shaped non-holonomic free-space mover |
| `OrcaCrowd` (pedestrian crowd) | the foot exodus — reciprocal collision avoidance, hard-edge navmesh confinement |
| `FakeNavMesh` | the "known world" — road extent + a vicinity band with a hard outer edge (R7) |
| `CompositeFootprintSource` | feeds pedestrians **and** pushers to the lane engine as obstacles cars react to (R5/R8) |
| `EvacConfig` | all tunables (fear rates, radii, flee preset, dwell, spatial-hash flag, working-region auto-track) |

The driver→pedestrian conversion, the flee reroute, and the panic decision are all **external** — the
core just sees param overrides, reroutes, despawns, and obstacles.

## What shipped (by phase)
| Phase | Delivered | Docs |
|---|---|---|
| **1 — spine** | incident + radius fear + panic mark; blocked detector; fake-navmesh + pedestrian crowd; driver→pedestrian conversion; the coordinated tick; grid demo | `PANIC-EVAC-DESIGN/TASKS/TRACKER.md` |
| **2 — fear field** | contagion + line-of-sight occlusion + jam-unease, seed-only-from-incident, monotone latch | `PANIC-EVAC-PHASE2-*.md` |
| **3 — Orca-push** | blocked+panicked car → shaped non-holonomic mover (nose onto shoulder) → wedge → pedestrian; composite crowd source | `PANIC-EVAC-PHASE3-*.md` |
| **4 — sublane decision** | analysed SL2015; **deferred** (kept the fast non-sublane core; sublane stays a per-scenario switch) so 10k perf is protected | `PANIC-EVAC-PHASE4-DECISION.md` |
| **5 Tier 1 — realistic town + locality** | working-region **auto-attach**; `EvacOrganicScenario` on a 274-junction 2-lane town; measured per-phase cost profile | `PANIC-EVAC-PHASE5-DESIGN/TASKS/TRACKER.md` |
| **5 Tier 2 — 10k scale** | opt-in **bit-identical spatial hashes** for both crowd solvers; `EvacCityScenario` on a ~13k-vehicle grid; viz payload management; before/after 10k profile | `PANIC-EVAC-PHASE5-TIER2-*.md` |

Supporting docs: **`EVAC-DEMO-TLS.md`** (signalized demo), **`MIXED-WALL-CONTAINMENT.md`** (the shaped-mover
wall clip), **`LANELESS-HANDOFF.md`** / **`INDIA-TRAFFIC.md`** (the ORCA / shaped-VO crowd layer the evac
foot/push crowds reuse).

## Determinism & parity guarantees
- Driving core **byte-identical with panic off**; the full offline suite is green and the determinism
  hash `909605E965BFFE59` is unmoved by the entire feature.
- Two small parity-core commits underpin it and are isolated + regression-tested: a **multi-lane active
  reroute fix** (`RungB3MultilaneRerouteRegressionTests` — fails pre-fix, passes post-fix) and making
  `VehicleRuntime.VType` settable (for `SetVehicleParams`). Both proven hash-inert.
- The Tier-2 crowd-solver spatial hashes are **bit-identical to brute force** (proven by exact
  position/heading-equality tests: `MixedTrafficSpatialHashTests`, `OrcaSpatialHashTests`,
  `EvacCrowdSpatialHashTests`), default-off, opt-in per scenario.
- Every evac run is deterministic (fixed iteration order, no RNG, no wall-clock) — asserted per demo.

Tests: `EvacSpineTests`, `EvacPhase2Tests`, `EvacPhase3Tests`, `EvacTlsDemoTests`, `EvacOrganicDemoTests`,
`EvacCityDemoTests`, `FearFieldTests`, `LineOfSightTests`, `ContagionKernelTests`, `IncidentTests`,
`BlockedDetectorTests`, `FakeNavMeshTests`, `VehicleMoverTests`, `MixedWallContainmentTests`,
`SetVehicleParamsTests`, and the spatial-hash + reroute regression tests above.

## Demos & how to run them
All offline (no SUMO). Each writes a self-contained HTML replay.

```bash
# realistic organic town — congestion + a large local foot exodus (~600 pedestrians)
dotnet run -c Release --project src/Sim.Viz -- --evac-organic evac-organic.html

# 10k-vehicle-class city (grid host) — local catastrophe, rest of the city keeps flowing;
# region-crop + frame-decimation keep the payload bounded (every drop is logged)
dotnet run -c Release --project src/Sim.Viz -- --evac-city evac-city.html

# per-phase cost profile (fear / disc feeds / pedestrian step / pusher step / engine.Step / auto-track)
dotnet run -c Release --project src/Sim.EvacProfile              # organic town
dotnet run -c Release --project src/Sim.EvacProfile -- --city    # 10k city, spatial hashes OFF vs ON
dotnet run -c Release --project src/Sim.EvacProfile -- --microbench   # brute-vs-grid crowd-solver speedup
```

Committed demo scenarios: `scenarios/evac-grid/` (4×4 priority grid), `scenarios/evac-grid-tls/` (5×5 TLS
grid); the organic and city demos load `scenarios/_bench/city-organic-L2` and `scenarios/_bench/city-15000`.

## Measured scale finding (scopes any future work)
At the 10k-city scale the evac layer is bounded by the working region (locality: ~50–950 tracked of
thousands active). The two O(m²) crowd solvers dominate evac cost when the local crowd is large; the
Tier-2 spatial hashes are bit-identical and free, and give a large win on **spread** crowds (2.6–3.7× at
N=2000 in the micro-benchmark) but a **modest ~1.14×** on the tightly-**clustered** evac crowd (the 3×3
grid block still contains most of the cluster). The city-size floor is `engine.Step` (the parity core,
already fast). `Sim.EvacProfile --city` confirmed **none** of the further deferred optimizations
(disc-feed / FearField grid / auto-track-scan indexing) are warranted at this scale — details in
`PANIC-EVAC-PHASE5-TIER2-TRACKER.md`.

## Source & test map
- Source: `src/Sim.Evac/` (the layer), `src/Sim.EvacProfile/` (the cost profiler + micro-benchmark),
  crowd solvers in `src/Sim.Core/Orca/` (`OrcaCrowd`) and `src/Sim.Core/Mixed/` (`MixedTrafficCrowd`),
  viz scenes in `src/Sim.Viz/SceneGen.cs` (`BuildEvacGrid`/`BuildEvacOrganic`/`BuildEvacCity`).
- Tests: `tests/Sim.ParityTests/Evac*`, `Fear*`, `LineOfSight*`, `MixedTraffic*`, `MixedWall*`,
  `RungB3*Reroute*`, `SetVehicleParams*`, `VehicleMover*`.
