# NEED — complete route→lane resolution for a general multi-lane (`-L 2+`) city

**For the SUMO parity coding session.** This is the **remaining** multi-lane route→lane gap after
`C2-iii` (multi-hop best-lanes) and `C2-v` (intra-edge mid-route lane change) both landed. Those two
closed most of it and their anchors (`scenarios/36-multihop-lanes`, `scenarios/37-intraedge-lanechange`)
pass — but a **general `netgenerate -L 2` city still throws at insertion**. Verified on the current
engine (`main@7718953`). Same parity-track bar: exact `@1e-3`, new anchor + SUMO golden +
parity-reviewer gate.

## Exact failing case (reproduced on current `main`)

`netgenerate --grid --grid.number=3 --grid.length=200 -L 2 --tls.guess --seed 42` +
`randomTrips.py --fringe-factor 5 --seed 42` + `duarouter --named-routes` → the engine throws:

```
System.IO.InvalidDataException: No <connection> found from edge 'C1B1' lane 1 to edge 'B1B2'.
  at Sim.Ingest.NetworkModel.ResolveSequenceCore(...)                NetworkModel.cs:350
  at Sim.Ingest.NetworkModel.ResolveLaneSequenceHandlesWithArrival(...) NetworkModel.cs:415
  (via Engine.TryInsertOnLane -> InsertDepartingVehicles at insertion)
```

The topology (route `C1B1 B1B2 B2C2`, `C1B1` is the route's first edge):

- The only connection from `C1B1` onward to the route's next edge `B1B2` is
  `fromLane="0" toLane="0" dir="r"` (a right turn) — **lane 0 only**.
- `C1B1` lane 1 connects to `B1A1` (straight), `B1B0` (left), `B1C1` (uturn) — **never `B1B2`**.
- So the vehicle must be on **`C1B1` lane 0** to make the turn; whatever put it on lane 1 (the
  depart-lane choice, or an incoming `toLane=1` connection for a longer route that also crosses this
  hop) is not being redirected to lane 0, so `ResolveSequenceCore` finds zero candidate connections
  and throws.

SUMO runs this exact net+demand to completion (all trips insert and arrive; the vehicle simply
lane-changes from 1→0 on `C1B1` before the junction). The engine's resolution does not.

## Why the existing C2-iii/C2-v work doesn't cover it

`ResolveSequenceCore` (`src/Sim.Ingest/NetworkModel.cs`, ~300-390) computes each edge's EXIT lane
by taking `ComputeBestLanes(routeEdges, edgeId)` and applying `BestLaneOffset` to the arrival lane,
**but only when `offset != 0 && targetExists`** (the sibling lane exists on this edge). The
`C1B1`-lane-1 case is one where the arrival lane has NO connection to the route's immediate next
edge at all, yet the redirect to the connecting sibling lane (lane 0) is not happening — either
`ComputeBestLanes` is not assigning lane 1 a nonzero offset toward lane 0 for this geometry
(e.g. it treats "continues to *some* next edge" as continuing, rather than "continues to the
*route's* next edge"), or the exit-lane redirect has a gap for this edge position. The parity
session should instrument `ComputeBestLanes`/`ResolveSequenceCore` for route `C1B1 B1B2 B2C2` to see
which. The invariant to enforce: **on every edge, the resolved exit lane must have a `<connection>`
to the route's next edge; if the arrival lane doesn't, redirect to the sibling lane that does (a
strategic intra-edge lane change), which is exactly what SUMO's per-edge `bestLaneOffset` does.**

## Port target

Same as C2-iii/C2-v: `MSVehicle::updateBestLanes` / `LaneQ`
(`sumo/src/microsim/MSVehicle.cpp:5744-6063`). The relevant guarantee is that `bestLaneOffset` is a
per-edge quantity keyed on continuation to the **route**, and the vehicle changes toward the
connecting lane on each edge it occupies (`LCA_STRATEGIC`, the existing `Engine.TryStrategicLaneChange`).

## Definition of done

1. **General `-L 2` city runs.** The repro above (and `scripts/gen-benchmark.sh` regenerated at
   `-L 2`) inserts and runs to completion in the engine — no `No <connection> found` for any route
   SUMO itself routes and runs.
2. **New anchor + golden.** A minimal 2-lane net where a vehicle's arrival/depart lane on an edge
   does NOT connect to its route's next edge but a sibling lane does, forcing the redirect that
   `C1B1` needs and that scenarios 36/37 don't exercise (36 = multi-hop best-lane; 37 = intra-edge
   mid-route change on a lane that *did* connect immediately). `sigma=0`, SUMO golden `--precision 6`,
   match `lane`/`pos`/`speed` `@1e-3`.
3. **Inert / no regressions.** Scenarios 36, 37, 18 + all committed scenarios stay green
   (`dotnet test`, currently **151**); `Sim.Bench` highway-dense determinism hash unchanged.
4. **Gate.** parity-reviewer ACCEPT; faithful to `MSVehicle.cpp`, no curve-fit.

## Why it matters

This is the last blocker for the scaled-city benchmark's **multi-lane** rungs (`BENCHMARK_SPEC` /
`VIZ_BENCH_TASKS.md` Phase 2). The benchmark + the `city-organic` demo currently run single-lane
(`-L 1`) only, so no lane-changing/overtaking is exercised at scale. It is also a genuine realism
gap independent of the benchmark: a general multi-lane route with a forced turn is currently
unroutable by the engine though SUMO handles it routinely.
