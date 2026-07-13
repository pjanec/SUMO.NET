# PANIC-EVAC.md — organized traffic → externally-driven panic evacuation

## 0. Goal (what this simulates)

Organized urban traffic that, on a localized **security incident** (bomb, shooting, armed
strike), switches to a **panic evacuation**: cars flee, streets block solid, and once boxed in
people **abandon their cars and flee on foot** — off the road, avoiding the stuck cars. The
organized traffic before it is a realistic-enough backdrop; the evacuation is the product.

This is **not** an "Indian traffic" model. The organized phase is ordinary lane driving, with an
*optional* **sublane** layer whose realistic value is small vehicles (scooters/cyclists)
**filtering to the front at red lights** — still fully organized (they stop on red). We start
**without** sublane; sublane is a separate opt-in.

## 1. The hard boundary — separation of concerns (the core of this design)

Two systems with a clean interface between them; neither reaches into the other's internals.

### A. The SUMO port — road-only, **panic-agnostic**
Does exactly what SUMO's data and tooling support: **driving vehicles on the road network**
(lane car-following + junctions; sublane optional). It contains **no panic logic at all**. It
exposes state and accepts generic control inputs, and its responsibility for a given vehicle
**ENDS at the boundary**:

> **Handoff boundary:** a vehicle leaves the SUMO port when it (i) is **stuck in a jam** (cannot
> make road progress), or (ii) is to **leave the road**. SUMO is lane-bound and never drives
> off-road — so "leaving the road" always means *the external layer has taken control*; it is a
> post-handoff state, not something SUMO does.

At handoff the car typically becomes a **static road obstacle** (an abandoned/blocked car still
blocks the carriageway — the realistic cascade that turns a jam into gridlock), and its pose +
dimensions are available to the external layer.

### B. The external evacuation layer — everything panic / off-road / on-foot
A separate layer **on top of** the SUMO port's public data, talking to it only through the seam
(§2). It owns:
- **panic-spread modelling** (fear + contagion — §4), kept entirely out of the SUMO port;
- the **organized→panic switch**, issued **per vehicle as an external input** to the SUMO port;
- **driver/passenger → pedestrian conversion** (external);
- **off-road free movement** for panicked vehicles and pedestrians — **ORCA + a fake-navmesh**
  (§3), using the SUMO cars as obstacles;
- the **handoff decision** (when a vehicle hits the boundary, take it over).

Because it is a layer, not a fork, the external system can later be **replaced** by a fully
world-aware (3D navmesh) system without touching the SUMO port — the seam is the contract.

## 2. The seam (SUMO port ↔ external layer)

All of this already exists on the branch (issue #4 froze it); the evac layer is a new consumer.

**SUMO port exposes (read):**
- Per-vehicle state — pose, heading, speed, lane, dimensions (`VehicleReadBuffer` columns /
  `TryGetVehicle`).
- **Cars as obstacles** — vehicle footprints projected for the external solver
  (`ICrowdFootprintSource` / `WorldDisc`; extend to an oriented footprint if the external layer
  wants OBB rather than disc).
- **Regime / stuck signal** — `DrModel` (`LaneArc` driving, `Stationary` stuck) + `Manoeuvring`;
  the external layer reads these to detect the boundary.

**SUMO port accepts (external input, per vehicle):**
- **Reroute** — set a vehicle's route to a flee target (standard SUMO dynamic rerouting; the
  *decision* to flee is external, the *act* stays SUMO on-road).
- **Release / hand-off** — stop driving this vehicle; hand back its final pose; optionally convert
  it to a static road obstacle. This is the "switch to ORCA mode" input, per vehicle.

The SUMO port never learns *why* — it just reroutes or releases on command. Panic stays external.

## 3. The "fake-navmesh" (road-network-derived, replaceable)

Free (non-lane) movement needs a notion of navigable space, but we only have the SUMO **road
network** — no buildings, no 3D world. So v1 is a deliberate simplification derived **only** from
that data, reusing our geometry structures:
- **Known navigable surface** = the union of lane + junction polygons (the drivable area).
- **Off-road** = the open plane beyond the road edges, treated as *assumed traversable* — because
  without world data we cannot know what is building vs. open. The only hard obstacles off-road
  are the **SUMO cars** (stuck/abandoned) the external layer already has.
- Pedestrians/panicked vehicles navigate this with **ORCA** (car footprints as obstacles), free to
  leave the road into the open plane and head for the incident's away-direction.

Explicitly a placeholder: a later real navmesh (building footprints, walkable/blocked regions,
elevation — from `.poly.xml` or a 3D world) drops in behind the same interface. **Out of scope
now.** The point of the fake-navmesh is to prove the evacuation *movement* using nothing but road
data.

## 4. Panic-spread model (external, local information only)

Panic is a **local, propagating information process** — no global "flee" broadcast. Per-agent
fear ∈ [0,1] from local signals only:
- **Direct** — proximity to the incident (optionally line-of-sight gated by the road geometry).
- **Contagion** — you catch panic from nearby already-panicking agents; this spreads panic
  *faster than the physical threat*, rippling outward.
- **Jam-unease** — being stopped in dense traffic raises fear slightly, but never alone reaches
  panic — so distant drivers in an ordinary jam **do not flee**; they sit, unaware of the cause.

Above `θ_panic` the external layer issues the switch for that vehicle. Deterministic (local
functions of frozen state; hashed tie-breaks; no RNG). This whole model lives in the external
layer and never touches the SUMO port.

## 5. Correctness / believability bar

Parity-exempt (no SUMO golden for panic); the SUMO port stays parity-exact:
- **Hard invariants:** the SUMO port with the evac layer **absent** is byte-identical to today
  (hash `909605E965BFFE59` unchanged); no vehicle interpenetration on-road; deterministic runs;
  external control inputs are additive/opt-in.
- **Behavioural targets (viz + stats):** panic front propagates outward and does not teleport;
  distant traffic stays organized/jammed and unaware; jam → handoff → foot-exodus cascade emerges;
  evacuation drains.
- **Acceptance = the viz** (cars, pedestrians, abandoned-car obstacles, marked incident, fear
  overlay), backed by the invariants.

## 6. Phased roadmap (panic-first; each phase a watchable milestone)

**Phase 1 — the spine (minimal, end-to-end).** Small hand-built road network; **non-sublane**
organized traffic (SUMO port); an external evac layer that: marks an incident, computes radius
fear, reroutes panicked cars to a flee exit (SUMO reroute input), detects the boundary
(stuck/`Stationary`), **releases** those cars (→ road obstacles) and spawns pedestrians, and
steers the pedestrians off-road via ORCA + the fake-navmesh (car obstacles) to the away-edge.
Both regimes rendered together. *Done:* the full transition plays once, coherent, in the viz.

**Phase 2 — panic as local information.** Contagion + distance/LoS fear + jam-unease; distant
traffic stays oblivious. *Done:* panic ripples outward; the far edge does not flee.

**Phase 3 — off-road vehicles + richer fake-navmesh.** Panicked cars leave the road under the
external layer (the shaped NH free-space model, in its right context); pedestrians route to
off-road havens / side-streets. *Done:* the "abandon the road, push through anywhere" mess.

**Phase 4 — sublane realism (optional, separate).** Enable the sublane layer in the SUMO port for
the organized phase: scooters/cyclists filter to the front at lights (stop on red). Independent of
the evac layer. *Done:* believable filtering in organized traffic.

**Phase 5 — scale.** Hundreds → thousands as panic spreads; spatial indexing. The far side that
never learns of the incident just stays jammed.

## 7. Open decisions (pin at Phase-1 kickoff)

- **Does a panicked-but-still-moving car stay SUMO-driven (reroute-to-flee on the road) until the
  stuck/off-road boundary, then hand to ORCA — OR does the panic switch hand it to ORCA
  immediately?** This doc assumes the former (matches "SUMO ends at stuck/off-road"); confirm.
- Exact reroute / release control-input API on the SUMO port (which exists vs. small additive
  opt-in).
- Away-direction flee-goal selection from the road graph; pedestrian haven representation in the
  fake-navmesh.
- Fear constants (θ_panic, decay, contagion kernel) — tuned against the viz.

## 8. Relationship to other docs

- `INDIA-TRAFFIC.md` — the shaped-footprint / non-holonomic / ORCA **machinery** (`Sim.Core.Mixed`,
  `ShapedVoSolver`, the bicycle model): the reusable *substrate* for off-road panic movement
  (Phase 3), not the headline. Dense mixed traffic per se is de-scoped.
- `LANELESS-DIRECTION.md` — the open-space ORCA regime + cross-regime bridge the seam is built on.
- `SUMOSHARP-DEADRECKONING.md` (NuGet branch) — the `DrModel` seam the boundary detection rides.
