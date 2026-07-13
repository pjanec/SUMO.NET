# PANIC-EVAC-PHASE2-TRACKER.md — panic-as-local-information checklist

At-a-glance status for Phase 2. Each item references a task in `PANIC-EVAC-PHASE2-TASKS.md` (detail +
success conditions); design is `PANIC-EVAC-PHASE2-DESIGN.md`. A box is ticked only when Opus has verified
the task's success conditions first-hand.

> **Status:** design-first docs written; **awaiting owner sign-off before implementation.** No Phase-2
> code exists yet. Batches will follow the orchestration loop (Sonnet implements, Opus reviews hard).

## S1 — Fear primitives (pure)
- [ ] **T1.1** `LineOfSight.IsVisible` (segment-vs-disc)
- [ ] **T1.2** contagion kernel `w(d, radius)`

## S2 — Fear field
- [ ] **T2.1** `FearField` plan/commit update + `EvacConfig` tunables (seed-only-from-incident invariant)

## S3 — Integration
- [ ] **T3.1** wire `FearField` into `EvacDirector.PreStep` (+ Phase-1 backwards-compat)

## S4 — Behavioural validation
- [ ] **T4.1** contagion causes spread (far car panics with contagion ON, never OFF)
- [ ] **T4.2** line-of-sight occlusion (occluded car gets no direct term)
- [ ] **T4.3** jam-unease amplifies, does not originate
- [ ] **T4.4** distant traffic stays unaware
- [ ] **T4.5** front propagates, never teleports (measured)
- [ ] **T4.6** determinism (fear-evolution signature)
- [ ] **T4.7** inertness + suite green + hash gate

## S5 — Viz: the panic front
- [ ] **T5.1** per-vehicle fear tint in the payload
- [ ] **T5.2** `template.js` fear ramp (front visibly spreads)

---

### Proposed batches
- **B1:** S1 (T1.1, T1.2) + S2 (T2.1) — the pure primitives + fear field, with their unit tests.
- **B2:** S3 (T3.1) + S4 (T4.1–T4.7) — integration + behavioural/determinism/parity tests.
- **B3:** S5 (T5.1, T5.2) — viz fear front (Opus renders to confirm).
