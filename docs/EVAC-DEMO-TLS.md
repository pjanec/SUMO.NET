# EVAC-DEMO-TLS.md — richer signalized evac demo (design + tasks, folded)

A more compelling evacuation replay: a **signalized (traffic-light) grid** with **denser traffic**, so the
organized phase shows real red-light stop-and-go and the incident produces a bigger jam → more
shoulder-pushers → a larger foot-exodus than the current sparse radius-60 demo. Parity-exempt, **no engine
change, no golden** — this is a new scenario + demo wiring only.

## Feasibility (confirmed)
`Engine.InitializeLoaded()` is shared by `LoadScenario` and `LoadNetwork` (`Engine.cs:895,908`) and builds
the TLS phase machines from `_network.TlLogicsById` (`:994-999`). So the evac layer's `LoadNetwork` +
runtime `SpawnVehicle` path **already runs the net's traffic-light programs** — organized vehicles will
stop at reds with no engine change. The repo has TLS parity scenarios (`09-traffic-light`, `35-actuated-tls`)
as references.

## Scope & key decisions
- **New committed net** `scenarios/evac-grid-tls/net.net.xml` via `netgenerate` with traffic-light
  junctions (`--tls.guess` or grid nodes typed `traffic_light`), boundary stubs as before. Parity-exempt:
  net only, **no golden / no rou.xml**, README noting it's evac-only + how it was generated.
- **New demo scenario builder** `EvacTlsScenario` (sibling of `EvacGridScenario`) — its own routes/exits/
  incident/config, **denser** (more vehicles), tuned so the demo stays viz-tractable (~a few hundred
  vehicles, 240 frames). **The existing `EvacGridScenario` + `scenarios/evac-grid` + all Phase-1/2/3 tests
  are left UNTOUCHED** (they pin the original demo; regressions there would be a red flag).
- **Viz** uses the TLS demo as the opening "Panic evacuation" scene (`SceneGen.BuildEvacGrid` switches to
  `EvacTlsScenario`, or a new `BuildEvacTls`). The incident/fear/push/ped machinery is unchanged.
- Density/incident are **tunables calibrated against the render** (Opus renders + tunes), not fixed here.

## Tasks (one batch)
- **T1 — TLS grid net.** `netgenerate` a signalized grid (e.g. `--grid --grid.number=5 --grid.length=80
  --grid.attach-length=60 --no-turnarounds --tls.guess` — or junctions typed traffic_light) →
  `scenarios/evac-grid-tls/net.net.xml`; add README (parity-exempt, no golden). Enumerate its entry/exit
  stub edges.
- **T2 — `EvacTlsScenario`.** New `src/Sim.Evac/EvacTlsScenario.cs`: load the TLS net, spawn a denser
  organized flow (more cars per entry), wire a tuned incident + config; return `(engine, director,
  handles)`. Reuse `EvacDirector`/`EvacConfig` unchanged.
- **T3 — viz.** Point the evac viz scene at `EvacTlsScenario`; regenerate the bundle.
- **T4 — smoke/invariant test** `tests/Sim.ParityTests/EvacTlsDemoTests.cs`: on the TLS net, the cascade
  emerges (panicked > 0, `OrcaPushCount` peak > 0, `PedestrianCount` > 0, some escaped), the run is
  deterministic (signature bit-identical across two runs), and no pedestrian leaves the navmesh. Separate
  from the original-grid tests.

## Success conditions
1. `evac-grid-tls/net.net.xml` committed (TLS junctions, no golden, README); it loads via `LoadNetwork`
   and organized vehicles **stop at reds** before the incident (assert at least one tracked vehicle is
   `DrModel.Stationary` at a signalized approach in the pre-incident window, or confirm visibly in render).
2. Denser cascade than the radius-60 demo — a higher peak `OrcaPushCount` and more pedestrians (report the
   numbers); still coherent.
3. Determinism + no-pedestrian-crosses-edge invariants hold on the new net.
4. Full `dotnet test` green (existing evac-grid tests unchanged); `Sim.Bench` hash `909605E965BFFE59`.
5. Opus renders the bundle and confirms: red-light stop-go in the organized phase, then a richer
   incident → flee → shoulder-push → foot-exodus.

## Non-goals
Sublane / filter-to-front (deferred, `PANIC-EVAC-PHASE4-DECISION.md`); any engine/parity change; actuated
signal tuning (static netgenerate program is fine); replacing the original evac-grid (kept for the tests).
