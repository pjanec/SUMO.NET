# LANELESS-DIRECTION.md — the validation bar + model for the laneless axis

Status: **direction decided (owner, this session)**. This note scopes a deliberate **bar change for
the laneless axis only**. It does not touch the phase-1 lane-based core, whose byte-exact SUMO
parity discipline (`CLAUDE.md`) stays sacred and unchanged.

## The decision, in one line

For laneless/heterogeneous traffic: **keep SUMO's per-step lateral *physics* as the reference, but
validate behaviorally + statistically — not byte-exact — and build the lateral layer as a continuous
footprint / velocity-obstacle (RVO/ORCA) model that unifies SUMO vehicles with external navmesh/RVO
agents.**

## Why (the evidence from the sublane byte-exact attempt)

Porting SUMO's sublane model (`MSLCM_SL2015`) to byte-exact parity split cleanly (see
`docs/PHASE2-SUBLANE.md`):

- **Cheap & achieved exact:** the per-step *physics/geometry* — single-vehicle drift
  (`scenarios/60`), side-by-side coexistence (`61`), `departPosLat` placement, following-parity
  (`62`), and even the exact `keepLatGap` magnitude (`−0.3015`, grid-quantized minGapLat).
- **Expensive & the wall:** the dynamic multi-vehicle *decisions* — overtake timing and the gap-keep
  **hold duration** — because those are governed by SUMO's **persistent cross-step lateral state
  machine** (`mySafeLatDist*` + `updateExpectedSublaneSpeeds` + push-based `inform*`). Two faithful
  ports each nailed the *magnitude* and missed the *duration*: exactness is **emergent from the state
  machine**, not any single formula.

Two structural facts make byte-exact the *wrong* bar here specifically:

1. **SUMO's sublane model is itself an approximation of laneless traffic** — lane-anchored,
   sublane-discretized. Byte-matching it means exactly reproducing an approximation.
2. **Its exact-timing machinery is ECS-hostile** — persistent per-vehicle lateral state and
   push-based neighbor signaling fight the plan/execute double buffer (the same push→pull conflict
   B6 hit with `MSDevice_Bluelight`). We'd pay the highest cost to import the least parallel-friendly
   part of SUMO, to reproduce an approximation of the behavior we actually want.

The lane-based core was different: there, byte-exact parity *is* the value (a validated, unambiguous
reproduction of SUMO's 20-year-tuned car-following). That stays. The laneless axis has looser
real-world requirements (the owner's stated bar) and a continuous target behavior, so it earns a
different, cheaper, better-fitting bar.

## The bar: behavioral + statistical, SUMO-grounded

This is not "no validation" — it reuses tools the project already has:

- **Byte-exact anchors (keep):** the tractable static/single-vehicle sublane rungs (`60/61/62`) stay
  as golden tests. They pin the physics constants and prove sublane mode does not perturb
  car-following. These are cheap and worth keeping exact.
- **Behavioral property tests (the Group-B bar, already in use):** no-overlap (footprints never
  intersect), overtake-completes, lateral-gap-maintained, resumes-when-clear, never-leaves-the-road.
  This is exactly how the external-agent behaviors (B1/B5/B6) are already validated.
- **Statistical / aggregate parity vs SUMO (already supported):** `tolerance.json` carries
  `parityMode="statistical"`; compare distributions (lateral position spread, headway, throughput,
  mean speed, fundamental diagram) over an ensemble, within a statistical tolerance — *not* per-step
  position. Use it for the lane-following regime (where good continuous avoidance should reduce to
  car-following) and for aggregate flow on mixed scenarios.

Rule of thumb: **exact where a SUMO golden is cheap and diagnostic; behavioral where the behavior is
continuous/heterogeneous; statistical where we want SUMO-comparability without per-step matching.**

## The model: continuous footprint / velocity-obstacle (RVO/ORCA)

Represent every mover — SUMO-derived vehicle *and* external navmesh/RVO agent — as a **footprint
agent** (position, velocity, footprint) and pick each agent's next velocity to avoid its
near-neighbors' velocity obstacles. Why this fits:

- **ECS-native & embarrassingly parallel.** Each agent independently reads its near-neighbors
  (frozen start-of-step) and writes only its own velocity/`MoveIntent` — the plan phase exactly, with
  **no persistent push-state** (the thing that made SUMO's sublane ECS-hostile).
- **Reuses what exists:** footprints (`ExternalObstacle.Width/LatPos`, vType `Width`), the spatial-
  hash groundwork (`_packed`/`HotVeh`, `SPATIAL-OPT.md`), the multi-constraint reducer, `DriftToward`,
  `FootprintsOverlap`, and the external-obstacle input surface (`EXTERNAL-AGENTS-VIZ.md`).
- **Unifies the two worlds for free.** "SUMO traffic respects non-SUMO navmesh/RVO agents" (a stated
  project goal, and why B4 u-turn was deferred *to* this layer) becomes trivial: both are the same
  kind of footprint agent doing velocity-obstacle avoidance. No special integration seam.
- **Better for the real target.** Dense heterogeneous/mixed traffic is what continuous avoidance
  handles well and lane-anchored sublane handles poorly.

SUMO stays the *physics reference*: the avoidance keeps SUMO's `minGapLat` lateral clearance and
`maxSpeedLat` lateral-speed bound; the longitudinal stays the validated Krauss car-following (the
lane-following regime should emerge as RVO reducing to car-following when laterally boxed in).

## Staged plan (each stage independently verifiable, all opt-in / byte-identical when off)

1. **RVO-lite lateral PoC (this session):** an opt-in `Engine.LanelessRvo` flag; a continuous
   lateral avoidance over near-neighbors (vehicles + external obstacles, treated uniformly) that
   produces an **emergent overtake** (drift to clear a slower leader, accelerate past via the
   existing `FootprintsOverlap` bypass, recenter). Validated **behaviorally** (no-overlap, passes,
   recenters). Off by default → phase-1 and the exact sublane rungs (`60/61/62`) untouched.
2. **Full ORCA over the spatial hash:** replace the lite lateral push with proper ORCA half-plane
   velocity selection (lateral + longitudinal) over the fixed-radius near-neighbor query (Seam 1's
   phase-2 form). Reciprocal (agents share avoidance). Parallel over `_packed` chunks.
3. **External-agent unification:** route navmesh/RVO agents through the same ORCA solve as vehicles.
   **API note (owner):** the current external-obstacle surface (`ExternalObstacle`, string-`Id`-keyed
   dictionary in `EXTERNAL-AGENTS-VIZ.md`) will be **replaced** — it does not scale (string ids, per-
   obstacle record, dictionary lookup). The unification targets a **scalable int-indexed footprint-
   agent store** (array/SoA, entity-handle keyed, spatial-hash indexed, high agent counts). So the RVO
   layer is deliberately built on a **neutral value-typed footprint-neighbour abstraction**
   (`RvoNeighbor`: pos/length/latOffset/halfWidth/speed — no strings, no dictionary), populated from
   `VehicleRuntime` today and from the new agent store later. Do **not** couple the RVO solve to the
   current `ExternalObstacle` API.
4. **Statistical SUMO cross-check:** on a mixed/heterogeneous scenario, compare aggregate flow +
   lateral distribution to a SUMO sublane run within `parityMode="statistical"` tolerance — the
   SUMO-grounding without byte-chasing.

## Coordination with SUMOSHARP-API (the NuGet packaging branch)

Reviewed `docs/SUMOSHARP-API.md` (branch `claude/sumo-csharp-nuget-strategy-4vlkki`; currently the
design doc only, no implementation, forked from the same `main` as this branch — so all overlaps are
*future*, at their Phase-1 implementation). Findings:

**STRONG ALIGNMENT (no conflict).** Both sessions independently concluded the string-keyed
`ExternalObstacle` record-dictionary must be **replaced by a handle-based struct-of-arrays store**
(SUMOSHARP-API §4.2–4.4 / D5: `ObstacleHandle` = 32-bit index + 16-bit generation, direct-mapped
columns `laneHandle/frontPos/length/startTime/endTime/speed/maxDecel/latPos/width/latSpeed`, zero-alloc
`UpdateObstacle`, dense active list, batch span API). This is exactly the "scalable int-indexed
footprint-agent store" this doc's Stage 3 calls for. The lateral columns (`latPos/width/latSpeed`)
they already list are load-bearing for the RVO solve — keep them.

**OWNERSHIP SPLIT (to avoid building the store twice).** The obstacle-store redesign is ONE
subsystem both directions want. Decision: the **NuGet session owns the store redesign** (SUMOSHARP-API
§4.3–4.4, Phase 1); the **laneless/RVO layer consumes it** via the neutral `RvoNeighbor` value
abstraction (already built for exactly this decoupling — the RVO solve reads `RvoNeighbor`s, never the
store directly). Today Stage 3 folds obstacles in through a **thin transitional adapter** over the
current `_obstacles` store (one loop in `ComputeRvoLateral`); when the SoA store lands, only that
adapter changes, not the solve.

**INCOMPATIBILITIES needing attention in the NuGet session:**
1. **Read API (§5) must expose lateral state.** The column list is `PosX/Y/Z/Angle/Speed/LaneHandle`
   but the sublane/laneless axis makes `posLat` (and `latSpeed`) first-class — this branch already
   added `VehicleExportSnapshot.PosLat`, `TrajectoryPoint.PosLat`, `Kinematics.LatSpeed`. Add a
   `PosLat` (double, parity-exact) column (lateral IS visible via derived `PosX/Y`, but hosts that
   consume lane-relative state and the sublane goldens need `posLat` directly).
2. **`VTypeParams` / `DefineVType` (§9, open item §14.1) must include the sublane vType params:**
   `maxSpeedLat`, `latAlignment`, `minGapLat` (added here to `VType`/`ResolvedVType`). Without them,
   runtime-spawned vehicles cannot do sublane/laneless lateral behaviour.
3. **Surface the new engine modes** through the facade/config: `ScenarioConfig.LateralResolution`
   (the `_sublane` master switch) and `Engine.LanelessRvo`.
4. **Merge-conflict surface (additive, resolvable):** both branches edit `Engine.cs`,
   `VehicleExportSnapshot.cs`, `DemandModel.cs`, `DemandParser.cs`, `VTypeDefaults.cs`,
   `ScenarioConfig.cs`, `TrajectoryPoint.cs`, `ToleranceConfig.cs`. All additions are in distinct
   regions; whoever merges second reconciles the additive hunks. Coordinate merge order. Both sides
   hold the same parity anchor (hash `909605E965BFFE59`, goldens byte-identical), so the merged result
   must too — both are additive/gated, so it should hold.

## What we explicitly do NOT do

Port SUMO's persistent lateral state machine (`mySafeLatDist*` / `updateExpectedSublaneSpeeds` /
`inform*`) to byte-exact. It is large, brittle, version-dependent, ECS-hostile, and reproduces an
approximation while blocking the continuous model we actually want. The magnitude/geometry mechanisms
it relies on are already understood and documented (`docs/PHASE2-SUBLANE.md`) and are reused as the
RVO layer's physics constants (`minGapLat`, `maxSpeedLat`, footprint widths).
