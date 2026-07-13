# PANIC-EVAC-PHASE2-DESIGN.md — panic as local information: HOW it works

Design (HOW) for **Phase 2** of the panic evacuation. The WHAT is `PANIC-EVAC.md` **R3** and **§6.2**
("panic as local information — contagion + LoS + jam-unease; distant traffic oblivious"); the Phase-1
mechanisms this builds on are `PANIC-EVAC-DESIGN.md` (the `Sim.Evac` layer, `EvacDirector` tick,
`Incident`, `EvacConfig`, `BlockedDetector`). Tasks + success conditions: `PANIC-EVAC-PHASE2-TASKS.md`;
checklist: `PANIC-EVAC-PHASE2-TRACKER.md`. Nothing here restates those.

Scope: replace Phase-1's stateless **radius-only instant panic latch** with a per-vehicle **fear field**
that evolves from *local information only* — direct (line-of-sight-gated) incident perception, contagion
from panicking neighbours, and jam-unease — so panic **propagates outward as a front** and distant,
un-jammed, unconnected traffic stays organized and unaware. Still parity-exempt; determinism preserved.

---

## 1. What changes vs Phase 1

Phase 1 (`PANIC-EVAC-DESIGN.md` §4.5): each step, `EvacDirector.PreStep` computed `Incident.FearAt` and
latched panic the instant it crossed `ThetaPanic`. Fear was a stateless function of distance — so panic
appeared everywhere inside the radius at once (a step-function front) and nothing propagated.

Phase 2: fear is **per-vehicle state** that integrates local inputs over time. `Incident.FearAt` stays,
but only as the *intensity* of the **direct** term, and only when the incident is actually **visible**
(line-of-sight). Two new local inputs — **contagion** and **jam-unease** — let fear grow in vehicles that
cannot see the incident, but only once a neighbour is already afraid. Panic still latches monotonically.

---

## 2. The fear field (per-vehicle, local, deterministic)

Per tracked, live vehicle *i*, a scalar `fear_i ∈ [0,1]`, all initially 0. Each tick, updated in a
**plan/commit** pass (read every vehicle's *previous* fear from a frozen array, compute all new fears,
then commit) — so the update is order-independent and deterministic, exactly like the engine's own
plan/execute discipline. No RNG, no wall-clock.

```
directLoS_i   = visible_i ? Incident.FearAt(pos_i, t) : 0          // 0 beyond incident radius or occluded
contagion_i   = Σ_j≠i  w(dist_ij) · fear_j(prev)                   // neighbours' FROZEN fear, kernel w
jam_i         = stationary_i ? 1 : 0                                // DrModel.Stationary (reuse the seam)

accum_i       = Decay · fear_i(prev)
              + dt · ( ContagionRate · contagion_i
                     + JamUneaseRate · jam_i · fear_i(prev) )       // jam AMPLIFIES existing fear only
fear_i(new)   = clamp( max( accum_i, directLoS_i ), 0, 1 )          // direct perception can jump fear up
if fear_i(new) ≥ ThetaPanic  →  latch panicked_i = true  (permanent)
if panicked_i               →  fear_i(new) = 1.0                    // a panicked driver is a full contagion source
```

**Key invariant — fear seeds ONLY from the incident (via LoS).** Both `contagion_i` and `jam_i` are
strictly *proportional to existing fear* (contagion to neighbours' fear, jam to the vehicle's own). With
no visible incident anywhere, every `fear_j` stays 0, so contagion and jam stay 0 and **nothing panics**.
This is what preserves Phase-1's `NoIncident_LayerIsInert` and models reality: *you do not panic in an
ordinary jam — only when something frightening is actually perceived nearby.*

**Prompt near-field, gradual far-field.** `max(accum_i, directLoS_i)` lets a vehicle that directly sees a
strong incident jump to full fear immediately (Phase-1-like promptness near the epicentre), while
vehicles that only hear it through neighbours ramp up gradually — producing an expanding front rather
than an instant fill.

**Latch + decay.** Panic is monotone (once latched, stays). `Decay (<1)` only matters *below* threshold:
a brief contagion blip on a distant car fades if its panicking neighbour moves away before it crosses θ,
so the front is driven by sustained local pressure, not transient touches.

---

## 3. Line-of-sight gate (the "direct" term)

`visible_i` = the incident is within perception range **and** the straight segment `pos_i → incident`
is not occluded. Phase-2 occluders = the **abandoned cars** (`WorldDisc`s the director already tracks).
Implemented as a pure helper `LineOfSight.IsVisible(from, target, occluders)` = segment-vs-disc
intersection test (no engine dependency, unit-testable). Perception range collapses to the incident
radius (`FearAt` is already 0 beyond it), so `visible_i` = `FearAt(pos_i) > 0 && no occluder on the
segment`. Gated by `EvacConfig.EnableLineOfSight` (off ⇒ Phase-1 behaviour: always visible within
radius). Richer occlusion (buildings/geometry) is future work behind the same helper.

---

## 4. Contagion kernel

`w(d) = ContagionRate`-scaled proximity weight, `w(d) = max(0, 1 − d/ContagionRadius)` for
`d ≤ ContagionRadius`, else 0. Pure, unit-testable. Neighbour scan is brute-force O(n²) over live tracked
vehicles (Phase-2 scale is small; a spatial hash is Phase 5, `PANIC-EVAC.md` §6.5). Only vehicles carry
fear and participate; pedestrians are already fleeing and abandoned cars are inert occluders (they do not
emit contagion — a wrecked empty car is not a scared driver).

---

## 5. Integration into `EvacDirector`

- New component `FearField` (owns `fear[]` + the plan/commit update); `EvacDirector` holds one.
- `PreStep` (Phase-1 §4.4/§4.5) changes: instead of "compute FearAt; if ≥θ latch + flee", it now
  (a) runs `FearField.Update(...)` reading each live vehicle's pose (`TryGetVehicle`), the incident,
  the abandoned-car occluders, and `stationary` (`GetDrModel == Stationary`); (b) for any vehicle whose
  fear just crossed θ this tick, applies the flee preset + reroute **exactly as today** (the downstream
  flee/convert/pedestrian machinery is unchanged).
- `EvacConfig` gains: `EnableContagion`, `ContagionRadius`, `ContagionRate`, `EnableJamUnease`,
  `JamUneaseRate`, `FearDecay`, `EnableLineOfSight`. Defaults chosen so the grid demo still cascades.
- Observability: expose `Fear(handle)` (0..1) and the count/among-front metrics for tests + viz.

Phase-1 equivalence: `EnableContagion=false, EnableJamUnease=false, EnableLineOfSight=false, FearDecay=1`
reduces the model to `fear_i = FearAt` latched at θ — i.e. Phase-1 exactly. The existing `EvacSpineTests`
therefore remain valid; with the Phase-2 defaults ON, their loose assertions (`PanickedCount>0`,
`ConvertedCount>0`, containment, determinism) still hold (contagion only ever panics *more* cars).

---

## 6. Determinism & parity

- Parity: `Sim.Evac` remains off the golden path; hash `909605E965BFFE59` unmoved (a test, S4).
- Determinism: plan/commit over a frozen `fear(prev)` array; fixed iteration order; pure kernel/LoS; no
  RNG/wall-clock ⇒ bit-identical fear evolution across runs (a test, S4).

---

## 7. Believability targets (`PANIC-EVAC.md` §5, R3)

- Panic **front propagates outward, never teleports**: the max distance-from-incident among panicked
  vehicles grows gradually; early on only near/visible vehicles are panicked.
- **Distant traffic stays organized & unaware**: a vehicle far from the incident, not jammed, with no
  panicking neighbour in range, never panics.
- **Occlusion matters**: a vehicle inside the radius but hidden behind an abandoned car gets no direct
  term — it only panics if contagion reaches it.
- **Jams amplify, don't originate**: being stuck raises fear only for a vehicle that already senses danger.

---

## 8. Viz (watch the front spread)

Add a per-vehicle **fear tint**: extend each vehicle frame entry from `[x,y,angle]` to
`[x,y,angle,fear]` (optional 4th element; `template.js` colours the box on a calm→panicked ramp when
present, else the existing single colour — additive, other scenes unchanged). This makes the expanding
panic front directly visible in the replay. `EvacDirector` exposes `Fear(handle)`; `SceneGen.BuildEvacGrid`
writes the 4th element.

---

## 9. Tunables (defaults; calibrated against the viz)

| Tunable | Default (first cut) | Meaning |
|---|---|---|
| `EnableLineOfSight` | true | gate direct perception on unoccluded sight |
| `EnableContagion` | true | neighbour-to-neighbour fear spread |
| `ContagionRadius` | 25 m | contagion neighbourhood |
| `ContagionRate` | 0.6 /s | contagion gain |
| `EnableJamUnease` | true | jam amplifies existing fear |
| `JamUneaseRate` | 0.3 /s | jam amplification gain |
| `FearDecay` | 0.98 /step | sub-threshold fear fade |
| `ThetaPanic` | 0.05 (Phase-1) | panic latch threshold (may retune) |

---

## 10. Non-goals (later phases)

Vehicle Orca-mode / off-road (Phase 3); per-lane buffered navmesh (Phase 3); sublane (Phase 4);
spatial-hash scale to thousands (Phase 5). Occlusion by real geometry/buildings (future, behind the LoS
helper). No global broadcast — ever.
