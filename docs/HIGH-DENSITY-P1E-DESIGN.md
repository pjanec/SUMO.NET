# HIGH-DENSITY-P1E-DESIGN.md — `device.rerouting` (periodic congestion-reactive rerouting)

Design doc for P1-E. WHAT/WHY: `docs/SUMOSHARP-HIGH-DENSITY-FEATURES.md` §3 P1-E + `docs/HIGH-DENSITY-PLAN.md`
§1 (P1-E) / §"Owner steer". This is the HOW. **Design-first: nothing is implemented until the owner
signs off on this doc.** All SUMO citations verified against the vendored 1.20.0 source (`sumo/src/...`).

## 0. Scope & config

The SumoData config sets: `device.rerouting.probability=1.0`, `device.rerouting.period=30`,
`device.rerouting.adaptation-steps=18`, `routing-algorithm=astar` (adaptation-interval defaults to 1s).
Behaviour: every `period` s, each equipped vehicle re-routes from its **current edge** to its
(unchanged) destination on a graph weighted by **live, smoothed edge travel times**, and installs the
result. This is distinct from the existing obstacle-triggered one-shot reroute (`UpdateReroutes`,
`Engine.cs:3256-3412`), which stays untouched.

## 1. SUMO semantics — verified (the spec to port)

**A. Equip + periodic schedule** (`MSDevice_Routing.cpp:135-149,223-237,277-297`):
- Equipped by probability (`=1.0` → all vehicles).
- First periodic reroute fires at **`depart + period`**, then **every `period`** thereafter. **No RNG
  phase jitter** — herd desync is emergent from differing depart times. (`device.rerouting.synchronize`
  defaults **false**; only if true does SUMO snap `start -= start % period` to a global boundary.)
- **Skip-if-stale-weights guard**: a reroute is a no-op if the weights haven't changed since this
  vehicle's last routing (`myLastRouting >= MSRoutingEngine::getLastAdaptation()`).

**B. The reroute** (`MSBaseVehicle.cpp:259-406`, `MSVehicle.cpp:1405-1416`):
- Source = the vehicle's **current edge**, bumped to the *next* edge if it's already within its
  brake-gap of the junction (`getRerouteOrigin`). Destination = route's last edge (unchanged).
- **NO improvement gate.** `savings = previousCost − routeCost` is computed as *output metadata only*;
  there is no `if (savings>0)`. The freshly-computed path **always replaces** the current one, unless
  it fails a **structural** check (empty; current edge not on the new route; committed mid-junction).
  When the new edge list **equals** the current one, SUMO short-circuits (`MSBaseVehicle.cpp:438`) — no
  new route object. → We must mirror: always install, short-circuit on identical edge list.

**C. Edge-weight smoothing** (`MSRoutingEngine.cpp:113-167,216-291`), our `adaptation-steps=18` path:
- Per-edge dense tables seeded to **free-flow speed** (`edge.getMeanSpeed()` = lane speed limit when
  empty). `myPastEdgeSpeeds[edge]` = a length-`N=18` **ring buffer** seeded with that free-flow speed.
- Every `adaptation-interval` (1s), at **end of timestep**, for each **delayed** edge:
  `myEdgeSpeeds[id] += (currSpeed − myPastEdgeSpeeds[id][k]) / N; myPastEdgeSpeeds[id][k] = currSpeed;`
  then `k = (k+1) % N`. `currSpeed = edge.getMeanSpeed()` sampled once (occupancy-weighted mean over the
  edge's lanes' vehicles; = speed limit when empty). **Port the incremental recurrence exactly** (not a
  recomputed sum/N — float drift must match).
- **`isDelayed()` is a permanent one-way latch** (`MSEdge.h:711-713`): set the first time *any* vehicle
  ever enters a lane on the edge, never reset. An edge that has never seen a vehicle is never updated
  (stays at free-flow seed); one that has is updated every interval forever (converging back toward
  free-flow when it empties, since `getMeanSpeed()`→limit). Port this latch exactly.
- **Effort the router reads** (`getEffort`): `max(length / max(smoothedSpeed, ε), minimumTravelTime)`,
  where `minimumTravelTime = length / vehicleMaxSpeed(+ timePenalty)` is a per-vClass floor.

**D. A\*** (`AStarRouter.h:128-278`): heuristic = euclidean-distance / network-max-speed (no landmark
table in our config) — admissible **and consistent** ⇒ no node re-expansion ⇒ **returns the identical
optimal-cost path a Dijkstra returns on the same weights.** So A* is a pure optimisation; the router is
exactly testable against the existing `NetworkRouter` Dijkstra.

**E. Threading** (`MSNet.cpp:755-828`): reroute tasks read the shared weight table; a `waitForAll()`
barrier drains them **before** the single end-of-step writer (`adaptEdgeEfforts`) runs. Single-writer/
many-reader via **temporal phase separation**, never per-access locks.

## 2. New pieces, mapped onto SumoSharp seams

| # | Piece | SUMO ref | SumoSharp seam |
|---|-------|----------|----------------|
| 1 | Config keys (`device.rerouting.*`, `routing-algorithm`) | option registration | `ScenarioConfig`/`Parser` (mirror `<processing>`/`time-to-teleport`) — additive, default off |
| 2 | Per-edge live smoothed-speed table + ring buffer + `isDelayed` latch | `MSRoutingEngine::adaptEdgeEfforts` | **new** dense handle-indexed arrays (à la `LanesByHandle`); updated in a new **end-of-step** pass |
| 3 | Effort fn + A* over the network graph, weights injected | `getEffort` + `AStarRouter` | reuse `NetworkRouter`'s adjacency; **generalise `EdgeCost` to an injected weight fn**, add A* (or an A* variant) |
| 4 | Periodic per-vehicle reroute trigger (equip, period, skip-stale, no-gate install, identical-list short-circuit) | `MSDevice_Routing` | **new** pass beside `UpdateReroutes`, **before `PlanMovements`**, reusing `RegisterRerouted`/`CommandBuffer.ReplaceRoute` |

## 3. Step-loop placement & the hard phase-ordering rule

- **Periodic reroute pass**: run at the existing `UpdateReroutes` point — **before** `PlanMovements` —
  and **flush `CommandBuffer` before `PlanMovements`**, so a vehicle that reroutes this step plans on
  its new route this step (matches SUMO's begin-of-step events → `planMovements`).
- **Edge-weight update pass**: run at **end of step**, after `ExecuteMoves`/`DecideSpeedGainChanges`
  settle, so a reroute always reads the *previous* step's fully-settled weights, never a mid-write
  snapshot (this is the temporal analog of SUMO's `waitForAll` barrier). **This relative order is a
  correctness requirement, not a detail** — getting it wrong breaks parity silently.
- `getLastAdaptation()` analog = the sim-time of the last end-of-step weight update; the per-vehicle
  skip-stale guard compares against it.

## 4. Thread-safety & the owner's fast/parallel requirement

The default faithful path is **already parallel and parity-safe** — no separate "fast mode" needed for E:
- **Reroute pass**: collect the vehicles due this step into a batch; `Parallel.For` over the batch, each
  running A* as a **pure read** of the frozen edge-weight snapshot into its **own** per-vehicle scratch
  (candidate edge list). No shared writes. Then a **serial** pass applies each result via
  `RegisterRerouted` + `CommandBuffer.ReplaceRoute` (add a lock there, or keep the record serial — matching
  `ChangeLane`'s existing discipline), one `Flush()` before `PlanMovements`. Because each A* is a pure
  function of (frozen snapshot, origin, dest), the result is **independent of thread order** → parallel is
  bit-identical to serial. This directly satisfies the "buffer the due-this-tick vehicles, fan A* out
  across cores" steer, and the 10k-vehicle herd is just a large batch.
- **Weight-update pass**: each edge's ring-buffer update is independent → parallelisable over edges,
  deterministic (fixed per-edge order). `currSpeed = getMeanSpeed()` is a per-edge reduction in fixed
  lane/vehicle order → deterministic.
- **Router state** per-thread/thread-local (no shared open/closed sets).
- **Gated fast-but-different (optional, later):** a cheaper cadence or approximate weights could be an
  opt-in CLI flag per the owner's standing principle, but is **out of P1-E scope** — the default is both
  faithful and parallel.

## 5. Determinism / parity argument

The reroute switch is **discrete edge-list equality**, not a float threshold — so it is robust to tiny
numeric noise *provided the edge weights themselves match*. The whole chain is bit-portable on a
deterministic (single-thread or fixed-reduction) run: `getMeanSpeed` (fixed-order reduction) → the exact
incremental moving-average recurrence → effort fn → A* (= Dijkstra exact). Therefore **exact trajectory
parity is the target** for the P1-E scenario, with statistical parity as a **fallback** only if a
genuine float-order divergence is observed. (This resolves Q2 toward the two-tier plan, leaning exact.)

## 6. Acceptance — two-tier (Q2; owner to confirm)

**Tier 1 — exact unit/parity on the deterministic machinery:**
- **A\* router**: given a fixed static weight table, returns the *same path* as `NetworkRouter`'s Dijkstra
  on the same weights (several hand-built graphs incl. a congestion-vs-alternate case). Exact.
- **Smoothing**: given a fixed sequence of per-edge `currSpeed` samples, `myEdgeSpeeds` matches the
  hand-computed ring-buffer recurrence (incl. the free-flow seed and the `isDelayed` latch behaviour).
- **Effort fn**: `max(length/max(v,ε), minTT)` on fixed tuples.

**Tier 2 — end-to-end parity scenario `scenarios/NN-reroute-congestion`:**
- A small net: a short "shortcut" edge-path and a longer alternate between the same OD, both driveable.
  Enough demand on the shortcut to congest it so its smoothed travel time rises above the alternate's;
  `device.rerouting` on (`probability=1, period=30, adaptation-steps=18, routing-algorithm=astar`).
  Golden from vanilla SUMO 1.20.0. **Target: exact `(lane,pos,speed)` trajectory parity** (deterministic,
  RNG-insensitive: sigma=0, fixed depart). If a real float-order divergence appears, fall back to
  **statistical** parity on the route split + throughput (declared via `parityMode` in `tolerance.json`),
  and document why. Distinct from `15-reroute` (obstacle-based).
- Also assert (functional) that the flow actually splits (some vehicles take the alternate) — i.e. the
  feature is genuinely exercised, not a no-op.

## 7. Config-parsing additions

`ScenarioConfig` gains (additive, defaults = off/SUMO-default): `RerouteProbability` (0),
`ReroutePeriod` (0 = disabled), `RerouteAdaptationSteps` (180), `RerouteAdaptationInterval` (1),
`RoutingAlgorithm` ("dijkstra"). Parsed from `<processing>` `device.rerouting.*` / `routing-algorithm`
`value=` attributes. Absent → rerouting inert → every existing scenario byte-identical.

## 8. Faithfulness risks (must honour)

1. **Phase order** (reroute reads previous-step weights; weight write is end-of-step) — hard requirement.
2. **`isDelayed` permanent latch** — port exactly (never-touched edges stay at free-flow; once-touched
   edges update forever). Not "update occupied edges."
3. **No improvement gate** — always install (short-circuit only on identical edge list).
4. **Moving-average incremental recurrence** — `avg += (new−oldest)/N`, not a recomputed mean (float drift).
5. **`getRerouteOrigin` brake-gap bump** — reroute from the next edge when within brake-gap of the junction.
6. **`minimumTravelTime` floor** — SumoSharp has no per-vClass min-travel-time-with-penalty concept yet;
   confirm it's `length / vType.maxSpeed` (timePenalty 0 for our nets) or design it before porting `getEffort`.

## 9. Proposed task breakdown (each closes on its success condition)

- **P1E-1** config keys (§7) — unit test: parser reads the keys; absent → inert/byte-identical.
- **P1E-2** edge-weight aggregation (§1C, seam #2) — unit tests: seed, ring-buffer recurrence, `isDelayed`
  latch, end-of-step timing. Deterministic, exact.
- **P1E-3** A* router + effort fn (§1C/1D, seam #3) — unit tests: A*==Dijkstra on fixed weights; effort fn.
- **P1E-4** periodic reroute trigger + parallel batch + integration (§1A/1B, §3, §4) — wired before
  `PlanMovements`; reuses `RegisterRerouted`/`ReplaceRoute`; no-gate install + short-circuit; skip-stale.
- **P1E-5** `scenarios/NN-reroute-congestion` + Tier-2 parity gate (§6). Full suite green.

## 10. Open questions for the owner
- **Q2 confirm**: two-tier acceptance, targeting **exact** end-to-end parity with statistical fallback? (§6)
- **Scenario authoring**: OK to hand-author the small congestion net (nodes/edges + `netconvert`) for
  `NN-reroute-congestion`, or is there a preferred net? (I'll hand-author a minimal one.)
- **`pre-period` / pre-insertion rerouting** (`device.rerouting.pre-period`, default 60): our config
  doesn't set it, but SUMO's default enables a pre-departure reroute. In scope for parity, or defer?
  (I recommend porting it only if the scenario shows it affects the golden; otherwise defer and document.)
