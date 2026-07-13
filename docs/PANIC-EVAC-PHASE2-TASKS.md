# PANIC-EVAC-PHASE2-TASKS.md — stages, tasks, success conditions

Phase-2 task breakdown. **Design refs** (e.g. *DESIGN §2*) point at `PANIC-EVAC-PHASE2-DESIGN.md`;
requirements at `PANIC-EVAC.md` (R3, §6.2); Phase-1 seams at `PANIC-EVAC-DESIGN.md`. Checklist:
`PANIC-EVAC-PHASE2-TRACKER.md`. Each task lists checkable **success conditions**; a task closes only when
all pass. Stages are dependency-ordered. Delegation follows the orchestration loop (Sonnet implements a
batch, Opus reviews hard before ticking).

---

## Stage S1 — Fear primitives (pure, unit-testable in isolation)

### T1.1 — `LineOfSight.IsVisible(from, target, occluders)`
- **Design:** DESIGN §3.
- **Files:** `src/Sim.Evac/LineOfSight.cs` (new).
- **Success conditions (unit tests):** returns true for a clear segment; false when an occluder disc
  straddles the segment; true when a disc is near but off the segment; true when the disc lies beyond the
  target or behind `from` (only occluders *between* the two points block). Pure (no Engine/network).

### T1.2 — contagion kernel `w(d, radius)`
- **Design:** DESIGN §4.
- **Files:** `src/Sim.Evac/FearField.cs` (kernel as a pure static, tested via T2.1's file or its own).
- **Success conditions (unit tests):** `w(0)=1`, `w(radius)=0`, `w(>radius)=0`, monotically decreasing,
  `w(radius/2)=0.5` (linear).

---

## Stage S2 — The fear field

### T2.1 — `FearField` update (plan/commit) + `EvacConfig` tunables
- **Design:** DESIGN §2, §5, §9.
- **Files:** `src/Sim.Evac/FearField.cs`, `src/Sim.Evac/EvacConfig.cs` (add the §9 tunables).
- **Success conditions:**
  1. Plan/commit over a frozen previous-fear array (order-independent): a unit test with a hand-built set
     of vehicle states/fears produces the same result regardless of processing order.
  2. **Seed-only-from-incident:** with no visible incident, all fears stay 0 across many updates
     (contagion + jam produce nothing from a zero field).
  3. Direct term jumps fear to `FearAt` for a visible in-radius vehicle; latch at `ThetaPanic` is
     monotone (never un-latches); a latched vehicle's fear is pinned to 1.0.
  4. Jam term only amplifies a vehicle with pre-existing fear (jam on a zero-fear vehicle adds nothing).

---

## Stage S3 — Integration into `EvacDirector`

### T3.1 — Wire `FearField` into the tick (replace the radius-only latch)
- **Design:** DESIGN §5.
- **Files:** `src/Sim.Evac/EvacDirector.cs`.
- **Success conditions:**
  1. `PreStep` drives `FearField.Update` from live poses + incident + abandoned-car occluders +
     `GetDrModel==Stationary`; a vehicle whose fear crosses θ this tick gets the flee preset + reroute
     exactly as before (downstream convert/pedestrian path unchanged).
  2. `Fear(handle)` observability added.
  3. **Backwards compatibility:** with `EnableContagion=false, EnableJamUnease=false,
     EnableLineOfSight=false, FearDecay=1`, behaviour reduces to Phase-1 (radius latch); a test confirms
     the pre-existing `EvacSpineTests` assertions still hold with Phase-2 defaults ON.

---

## Stage S4 — Behavioural validation (the Phase-2 promises)

All in `tests/Sim.ParityTests/EvacPhase2Tests.cs` (may reuse `EvacGridScenario`, or build small
purpose-made setups).

### T4.1 — Contagion causes spread
- **Success:** a vehicle starting **outside** the incident radius but within a chain of neighbours to a
  panicking cluster **eventually panics** with contagion ON; the **same** vehicle **never** panics with
  `EnableContagion=false` (all else equal). This is the crisp proof contagion (not radius) drives the far
  field.

### T4.2 — Line-of-sight occlusion
- **Success:** (pure) `LineOfSight` blocks a segment through an occluder (covered by T1.1); (behavioural)
  a vehicle inside the radius but occluded from the incident receives **no direct term** (its fear stays 0
  until/unless contagion reaches it), whereas an unoccluded peer at the same distance panics promptly.

### T4.3 — Jam-unease amplifies, does not originate
- **Success:** a stationary vehicle with some seeded fear crosses θ **sooner** with jam-unease ON than
  OFF; a stationary vehicle with **zero** seed and no visible incident **never** panics (jam alone cannot
  originate panic).

### T4.4 — Distant traffic stays unaware
- **Success:** a vehicle far from the incident, never stationary, with no panicking neighbour in
  `ContagionRadius`, has fear 0 and never panics for the whole run.

### T4.5 — Front propagates, never teleports
- **Success:** over the grid run, the max distance-from-incident among panicked vehicles is
  **non-decreasing** and, in the early post-incident steps, strictly less than the far-corner distance
  (panic is near/visible first, then expands) — measured, not eyeballed.

### T4.6 — Determinism
- **Success:** two runs produce a bit-identical fear-evolution signature (per-vehicle fear trace folded to
  a string), extending the Phase-1 determinism check.

### T4.7 — Inertness + suite + hash gate
- **Success:** `NoIncident_LayerIsInert` still holds (no incident ⇒ 0 panicked); full `dotnet test` green;
  `Sim.Bench` `hashA==hashPar==909605E965BFFE59`.

---

## Stage S5 — Viz: the panic front

### T5.1 — Per-vehicle fear tint in the payload
- **Design:** DESIGN §8.
- **Files:** `src/Sim.Viz/SceneGen.cs` (write `[x,y,angle,fear]`), `src/Sim.Viz/Payload.cs` if needed.
- **Success:** the evac scene's vehicle entries carry a 4th `fear` element; other scenes/entries
  unaffected (still `[x,y,angle]`).

### T5.2 — `template.js` fear ramp
- **Design:** DESIGN §8.
- **Files:** `src/Sim.Viz/template.js`.
- **Success:** a vehicle box with a 4th element is coloured on a calm→panicked ramp; entries without it
  render in the existing single colour (additive, all other scenes unchanged); rendered bundle shows the
  panic front spreading outward from the incident over time (Opus renders it to confirm).
