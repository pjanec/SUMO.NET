# MIXED-WALL-CONTAINMENT.md — solver-level wall confinement for MixedTrafficCrowd

Design + tasks (folded — small change) to pay down the Phase-3 tech-debt recorded in
`PANIC-EVAC-PHASE3-TRACKER.md`: make `Sim.Core.Mixed.MixedTrafficCrowd` confine movers to its walls at the
**solver** level, so `Sim.Evac.VehicleMover`'s external clamp backstop is no longer needed. Parity-exempt
(`Sim.Core.Mixed` is off the golden path); the determinism hash must stay `909605E965BFFE59`.

## Problem (root cause, confirmed in code)
Walls are full-yield shaped obstacles in the ORCA solve (`MixedTrafficCrowd.Plan`, `:388-409`,
responsibility 1.0), so they only constrain the **holonomic target velocity's direction** for this step.
`SteerNonholonomic` then maps that target to a car-like velocity, and the kinematic-bicycle integration
(`Step`, `:246-267`) commits the new centre `c1` **with no check that the path `c0→c1` stayed on the
interior side of the walls**. On the ShapedVoSolver "already overlapping" recovery branch the target can be
a large burst; the resulting one-step displacement can **tunnel a thin wall** (Phase-3 B2 observed a pusher
escaping to ≈(−11, 370), far outside the band). Today `VehicleMover` backstops this externally: an
axis-aligned bounding clamp + `DeterministicNudge` + the additive `MixedTrafficCrowd.SetPose`. That masks
the gap rather than fixing it, and only works for a rectangular band.

## Fix — swept wall clip in the integration (both NH and holonomic branches)
In `MixedTrafficCrowd.Step`, after computing the candidate new centre `c1` (NH branch after the bicycle
integration; holonomic branch after `pos += v*dt`), **clip the centre displacement `c0→c1` at the first
wall it would cross**:
- For each wall `(centre, shape)` in range, test whether the segment `c0→c1` intersects the wall's convex
  box (`shape` is a `ConvexShape` rectangle already rotated to the wall angle, positioned at `centre`).
- If it does, set `c1` to the **entry point minus a small skin** (so the body stops just short on the
  interior side) and remove the into-wall (normal) component of `_velocity[i]` for the neighbour-facing
  velocity. Keep `_heading[i]` (the car simply stops against the wall; it does not teleport or spin).
- If the segment crosses no wall, leave `c1` unchanged.

**Why this is safe for existing scenes:** it is a pure anti-tunnelling guard — it only alters a step whose
centre path actually crosses a wall box, which does not happen in the well-behaved India/mixed scenes (the
ORCA wall half-plane already keeps them off the walls). So it is **inert** there and their trajectories are
byte-unchanged (guarded by T2 below). Deterministic (fixed wall order, no RNG). Centre-path swept test is
sufficient for the thin-wall tunnelling case; a full swept-BODY test is a non-goal (over-engineering for
this feature).

## Then simplify VehicleMover
With the solver guaranteeing confinement, remove `VehicleMover`'s external containment clamp +
`DeterministicNudge`, and remove `MixedTrafficCrowd.SetPose` if it then has no caller. `VehicleMover` keeps
its wedge/creep/maxspeed logic and `ArmWalls` (still needed to add the walls to the crowd).

## Tasks (one batch)
- **W1 — solver clip.** Implement the swept wall clip in `MixedTrafficCrowd.Step` (both branches). New
  helper for segment-vs-convex-box (Liang-Barsky against the wall's `ConvexShape` edges, or equivalent).
- **W2 — reproduce-then-fix test.** `tests/Sim.ParityTests/MixedWallContainmentTests.cs`: build a crowd
  with a thin wall (small thickness) and drive a mover **hard straight at it** at high speed (the
  tunnelling setup). Assert the mover's centre never ends up on the far side of the wall across many steps
  (stays interior within a skin tolerance). This test must FAIL on today's code (tunnels) and PASS after
  W1 — note that in the report.
- **W3 — inertness / no-regression.** All existing `MixedTraffic*` tests (13), `VehicleMover*`, and the
  `Evac*` suites stay green; determinism hash unmoved. India scenes byte-unchanged.
- **W4 — remove the backstop.** Delete `VehicleMover`'s clamp + `DeterministicNudge`; remove
  `MixedTrafficCrowd.SetPose` if now unused. `VehicleMoverTests.BandWalls_ConfineMoverDrivenOutsideTheBox`
  and `EvacPhase3Tests.ActivePushers_StayWithinNavmeshBounds` must still pass **on the solver guarantee
  alone**.

## Success conditions (acceptance)
1. W2 tunnelling test passes (and demonstrably failed pre-fix).
2. Full `dotnet test` green (only the pre-existing 3 skips); India mixed-traffic 13 tests unchanged.
3. `Sim.Bench` `hashA==hashPar==909605E965BFFE59`.
4. `VehicleMover` no longer contains the bounding clamp / nudge; evac pusher confinement (Phase-3 T4.2)
   still holds via the solver.

## Non-goals
Changing the ORCA half-plane math or the NH steering; full swept-body (vs centre-path) collision; per-lane
band geometry (that is the separate Phase-3 "richer band" sub-task).
