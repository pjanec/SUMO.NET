# #15 — automatic per-area realism LOD gate for lane changing (design)

> **STATUS: SHIPPED** (T1–T4 done). Verified first-hand: parity **657/4** byte-identical, bench hash
> `D96213B7BB4021A7` (parallel==single); demo flow healthy (arrivals **1022**, stoppedFrac 0.11–0.68, no
> gridlock); `keepRight stop 0→571` (floats now occur OUTSIDE the pocket, low realism) while `coopAdvice`
> stays > 0 (cooperation still fires INSIDE the pocket); `Sim.LiveCity.Tests` 22 passed incl. the two new
> per-area LOD tests. The static crop-centre pocket (promote 70 m) is the v1 default; tying the source to a
> moving camera/avatar (below, "Open decision") is the follow-up.


Live-city demo only; **inert on every parity/bench golden**. Parity is the iron law — the engine seam this
adds defaults to "global flag applies", so every golden is byte-identical. See
`LIVE-CITY-15-RESUME.md` §2 item 2 and `LIVE-CITY-15-ATTEMPT-LOG.md` (OWNER REQUIREMENT … per-area LOD).

## WHAT (owner requirement)
Cooperative lane change (and its into-occupied vetoes / the stopped keep-right float guard) is currently a
**global** switch. The owner wants it **automatic per area** when globally enabled:
- **HIGH realism** area (observed / on-screen / near the interest pocket): cooperative LC + no pure-lateral
  float + into-occupied vetoes — never cheats.
- **LOW realism** area (distant / off-screen / unobserved): auto-fall-back to the cheap pure-lateral swap
  for performance — no viewer to see it, cheaper to run.

Effective per-car decision: `useCooperative = globalCoopEnabled && areaIsHighRealism(car)`. When false, the
car takes the cheap swap (the exact `LIVECITY_COOP=0` behaviour, but per-car instead of global).

## The keying signal (already exists)
`LiveCitySim` already owns an `InterestField` (`_field`) with a single static high-realism **pocket**
anchored on the crop crossing nearest the crop centre (`Sim.Pedestrians.Lod.InterestField` /
`InterestSource`, promote 70 m / demote 100 m). It is the same LOD split the ped side uses to promote peds to
full ORCA. The pocket centre + radii are already exposed on the sim
(`HighRealismPocketX/Y`, `HighRealismPromoteRadius`, `HighRealismDemoteRadius`).

`InterestField.Query(pos)` returns `AnyWithinPromote`/`AllOutsideDemote`, computed from **frozen
start-of-step** source positions — a pure function of position. For the single static car pocket a direct
distance test against the exposed centre/radius is equivalent and index-independent; the design uses that
(no dependency on the ped manager's per-step `RebuildIndex`). Hysteresis (promote vs demote radius) is a
ped-promotion refinement; for the car gate a single radius (promote) is enough and simpler to reason about.

## HOW (the engine seam) — recommended: per-vehicle flag, host-populated
Add `VehicleRuntime.LowRealismLaneChange` (bool, default **false**). The four cooperative/veto gating sites
that today read the global `CooperativeInformFollower` (keep-right float guard, speed-gain informFollower,
strategic informFollower, both into-occupied vetoes) additionally AND in `!v.LowRealismLaneChange`:

    useCoop(v) = CooperativeInformFollower && !v.LowRealismLaneChange

The host sets the flag once per step, BEFORE `Engine.Step()`, from each live vehicle's **previous-snapshot**
world position (`_lastSnapshot`, already built and frozen from the prior step) vs the static pocket:

    lowRealism(car) = distance(carPos, pocketCentre) > HighRealismPromoteRadius

Newly-spawned vehicles not yet in the previous snapshot default to `false` (high realism / cooperative) —
the conservative choice (they never float before their first classification).

### Why this seam
- **Determinism / parity.** `LowRealismLaneChange` defaults false and the host only sets it when
  cooperative LC is on (demo). Every golden leaves it false AND the global flags off ⇒ every gating site is
  byte-identical ⇒ goldens unchanged. The classification is a pure function of the previous frozen snapshot
  and a static pocket — order-independent, no `System.Random` — so serial==parallel is preserved.
- **One-step staleness is negligible & deterministic.** Using the previous snapshot (a car moves ≤ ~5 m per
  0.5 s step vs a 70 m pocket) avoids computing world positions inside the lane-change hot path, and previous-
  snapshot data is itself frozen deterministic state.
- **Minimal hot-path cost.** The gating sites read a bool already on the vehicle; no per-vehicle geometry or
  delegate call inside the parallel plan.

### Alternatives considered
- **Predicate delegate on the engine** (`Func<x,y,bool>`): elegant but calls into host state from the
  parallel plan (determinism only safe for a static source) and needs per-vehicle world-pos in the hot path.
- **Engine owns the pocket geometry + computes world pos per vehicle**: pushes demo scene knowledge into the
  engine and pays world-pos cost per candidate change. Rejected — keeps demo policy out of the engine.

## Determinism / parity argument (summary)
1. `VehicleRuntime.LowRealismLaneChange` default false + host sets it only under cooperative LC (demo) ⇒
   goldens inert ⇒ parity 657/4 byte-identical, bench hash `D96213B7BB4021A7`, parallel==single.
2. Classification is a pure function of the previous frozen snapshot + static pocket ⇒ order-independent ⇒
   serial==parallel unaffected.

## Tasks & success conditions
- **T1 — engine flag + gating.** Add `VehicleRuntime.LowRealismLaneChange`; AND `!v.LowRealismLaneChange`
  into `useCoop` at the 4 sites. Add an engine per-handle setter (`SetLowRealismLaneChange(handle, bool)`) or
  reset-and-set API. **Success:** parity 657/4 byte-identical; bench hash unchanged (flag never set on
  goldens).
- **T2 — host classification.** In `LiveCitySim.Step`, before the engine step and only when
  `cfg.CooperativeLaneChange`, classify each live vehicle from the previous snapshot vs the pocket and set the
  flag; unclassified (new) vehicles = high realism. **Success:** a unit test asserts a vehicle placed inside
  the pocket radius is classified high-realism (flag false) and one outside is low-realism (flag true), and
  the classification is a pure function of position (same positions → same flags).
- **T3 — flow + float measurement.** Run the repro. **Success:** demo flow not regressed (arrivals ≥ ~1000,
  no gridlock, liveness test still green); `LIVECITY-LCSWAP keepRight stop` may be > 0 now (floats permitted
  OUTSIDE the pocket) but stays 0 for cars inside the pocket (needs a pocket-scoped counter, or verify in the
  3D viewer that the observed central area shows no float while distant cars may).
- **T4 — docs.** Update RESUME §2 item 2 → done; attempt-log entry with measured flow.

## Open decision for the owner
Radius choice for the CAR gate (promote 70 m vs demote 100 m vs a car-specific radius) and whether the car
pocket should track the same InterestField source as peds (so a future moving camera/avatar bubble also
promotes nearby cars) or stay a static crop-centre pocket. The recommendation is: reuse the SAME
`InterestField` (via `Query(pos).AnyWithinPromote`) so cars and peds share one interest model and a future
moving source promotes both — at the cost of depending on the field's per-step `RebuildIndex` ordering
(already deterministic). Ship the static-radius version first (above), then switch the predicate to
`_field.Query` once the shared-source behaviour is desired.
