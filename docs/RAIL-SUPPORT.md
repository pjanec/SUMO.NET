# Rail support — status and remaining work

**Read `CLAUDE.md`, `DESIGN.md`, and `TASKS.md` first.** This doc is the self-contained record of
the SUMO TRAIN / RAIL support added to the engine (rungs R1–R6), plus the precisely-scoped pieces
that were deliberately deferred. Rail was previously scoped out everywhere (vClass `rail*`/`tram`
threw `NotSupportedException`, `rail_signal`/`rail_crossing` were unhandled, bidi topology was
ignored). All six rungs below are on `main`, each parity-reviewer ACCEPTED, exact @1e-3.

## What is DONE (on `main`, do not redo)

| Rung | What it does | Anchor scenario | Key files |
|---|---|---|---|
| **R1** | Rail vClass defaults (tram, rail_urban, subway, rail, rail_electric, rail_fast) + one free-running train on Krauss with rail params. Fixed a latent bug: sigma's default is vClass-dependent (0.5 road / 0.0 rail), now carried per-class. | `47-rail-free-flow` | `VTypeDefaults.cs` |
| **R2** | Two trains following (rail leader-gap car-following; Krauss steady-state gap with rail decel/minGap/length). Pure additive — no engine change. | `48-two-train-following` | (scenario only) |
| **R3** | Bidirectional single-track topology: ingest the `<edge bidi=...>` attribute + the no-signal deadlock guard (a rail vehicle does not insert onto a track whose opposing lane is occupied). | `49-rail-bidi-meet` | `NetworkModel/Parser.cs`, `Engine.RailBidiTrackOccupied` |
| **R4** | Rail signals + driveway reservation (**the headline**): a train holds at a rail signal until the shared block ahead (its bidi conflict lane) clears, using full-length (partial) occupancy. Reuses the red-light stop-line brake. | `50-rail-signal-meet` | `Engine.{BuildRailSignalInfo,RailSignalConstraint,VehicleBodyOccupies}` |
| **R5** | Level crossings (MSRailCrossing): a rail_crossing closes its controlled ROAD links while a train occupies the crossing (G/y/r/u state machine), so road vehicles yield. | `51-rail-crossing` | `Engine.{BuildRailCrossingInfo,AdvanceRailCrossings,RailCrossingConstraint}` |
| **R6** | MSCFModel_Rail traction model: speed-dependent acceleration from parametric traction/resistance curves (the tapering rail accel profile, distinct from Krauss's constant bound). | `52-rail-traction` | `RailModel.cs`, `VTypeDefaults.cs`, `Engine.cs` dispatch |

**Invariants held by every rung** (CLAUDE.md rule 3): committed suite green, `Sim.Bench` determinism
hash **unchanged at `909605E965BFFE59`** for the non-rail path (rail is inert-when-absent, like every
prior additive group), a non-vacuous anchor (the pre-port engine visibly fails each — throws or
collides), and a parity-reviewer ACCEPT before promotion.

## Golden regeneration essentials (network side, ends in a commit)

SUMO 1.20.0 via `python3 -m pip install eclipse-sumo==1.20.0 libsumo==1.20.0 traci==1.20.0`; put its
`bin/` on PATH. Bidi rail nets need `spreadType="center"` on both reversed edges (netconvert then
emits `bidi=`). rail_signal / rail_crossing come from the node `type=`. Per-scenario golden command:

```
sumo -c config.sumocfg --fcd-output golden.fcd.xml --fcd-output.acceleration --precision 6 \
     --save-state.times 1 --save-state.files golden.state.xml --no-step-log true
```

Rail scenarios rely on rail's sigma default being 0 (deterministic — no explicit sigma). For
carFollowModel="Rail"/"IDM" vTypes, do **not** commit `golden.vtype.json`: the dump helper hardcodes
`carFollowModel="Krauss"` and misreports the Rail model's sigma as -1, so it is not a valid
cross-check reference (same pattern as the IDM/ACC scenarios 22–25). Trajectory parity validates the
resolved params end-to-end.

## Deferred / remaining work (precisely scoped, not yet built)

Each was deliberately left out because the committed anchor does not exercise it; none is a bug in
what landed. Ordered roughly by value.

1. **R4 — the `mustYield` priority tie-break for SIMULTANEOUS opposing arrivals.** The committed R4
   anchor staggers departures so the winning train physically occupies the block before the other
   arrives (pure `conflictLaneOccupied`). Two trains reaching opposing rail signals in the SAME step
   need `MSRailSignal::DriveWay::hasLinkConflict`/`mustYield` (MSRailSignal.cpp:865/928) to arbitrate
   by priority — not ported. Needs a symmetric-arrival scenario. Also deferred: the crossing/flank-foe
   internal lanes (they clear the same step as the bidi block in scenario 50, so they do not change
   its golden) and the protecting-switch/link-conflict checks. See the `BuildRailSignalInfo`/
   `RailSignalConstraint` scope comments.

2. **R5 — the arrival-time prediction close arm.** The committed R5 anchor has the road car react only
   once the train is PHYSICALLY on the crossing, so physical occupancy closes it in time. SUMO also
   pre-closes the crossing before the train arrives via `avi.arrivalTime`/`leavingTime` vs `time-gap`
   (MSRailCrossing.cpp:128–134). Reproducing that needs the **approaching-registration infrastructure
   with arrival/leave TIMES** — the same `ApproachingVehicleInformation` machinery TASKS.md rung 9b
   deliberately deferred project-wide (9b used a static request matrix + frozen snapshot instead). It
   is precision-fragile (the close step depends on the train's planning look-ahead + `getArrivalTime`
   → `getMinimalArrivalTime`, MSCFModel.cpp:429). Build the approaching-registration pass first; it
   would also unlock a symmetric R4 tie-break and richer junction timing. See the `AdvanceRailCrossings`
   scope comment.

3. **R6 — the built-in per-type lookup tables + moving-block followSpeed + dwell + reversal.** R6 ports
   only the PARAMETRIC traction/resistance curves (`trainType="custom"` with maxPower/maxTraction/
   resCoef_*). The built-in NGT400/ICE/RB* types (MSCFModel_Rail.h init* functions) are big hardcoded
   `speed→value` lookup tables + `getInterpolatedValueFromLookUpMap` (piecewise-linear) — a mechanical
   transcription per type. The moving-block leader `followSpeed` (MSCFModel_Rail.cpp:181, the CIR-ELKE
   safety gap) is deferred — needs a two-train Rail-model scenario. Station **dwell** (`<stop>` with a
   duration) and **reversal** (the train changes direction at a stop; TASKS.md "B4" also deferred it as
   a poor fit for lane-based car-following) are separate hard pieces, each its own rung + golden.

4. **Multi-lane bidi lane-pairing.** `NetworkModel.TryGetBidiLaneId` pairs by same lane INDEX (correct
   for single-lane rail track, all that the committed scenarios use). Multi-lane bidi edges reverse
   lane order — revisit if a multi-track bidi scenario is added.

5. **The `firstRailSignal` cut-off on the R3 bidi INSERTION walk.** R3 walks the whole forward route
   checking bidi occupancy; SUMO stops at the first rail signal on the route (MSLane.cpp:999). The
   committed scenarios never place a rail signal on a bidi insertion route (both R4 trains insert while
   the block is empty), so the over-block never triggers — but implement the cut-off before a scenario
   puts a rail signal downstream of a bidi departure lane. Flagged in the R3 review.
