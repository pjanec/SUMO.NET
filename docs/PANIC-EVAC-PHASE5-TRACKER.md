# PANIC-EVAC-PHASE5-TRACKER.md — scale checklist

Status for Phase 5 (`PANIC-EVAC-PHASE5-DESIGN.md` / `-TASKS.md`). Principle: **full city on the parity
engine; evac attaches only to a bounded working region around the incident** (cost ∝ local affected
population, not city size). Staged: Tier 1 now, Tier 2 (heavy opt, 10k target) after Tier-1 measurement.

> **Status:** owner approved staged (Tier 1 now, Tier 2 later; final goal a 10k-vehicle city, but the
> evac stays local). **Tier1-B1 DONE and Opus-reviewed** (427 pass / 3 skip; hash `909605E965BFFE59`
> unmoved; locality proven: 50/371 ever-active vehicles tracked). B1 surfaced a latent parity-core
> reroute bug (multi-lane active reroute read the stale original route → crash); fixed + committed
> separately with a fail-pre-fix/pass-post-fix core regression test (`RungB3MultilaneRerouteRegressionTests`).

## TIER 1 — realistic organic town + local auto-attach
### S1 — working-region auto-attach
- [x] **T1.1** `EvacConfig.WorkingRadius` (default 250; documented ≥ incident radius + jam margin)
- [x] **T1.2** `EvacDirector` auto-track-by-region (deterministic sort by handle.Index; in-region only; off by default → grid/TLS demos unaffected)

### S2 — organic scenario
- [x] **T2.1** `EvacOrganicScenario` (LoadScenario city-organic-L2 net+demand; incident at junction 415, the busiest interior TLS junction)
- [x] **T2.2** demand-under-director confirmation (peakActive=231 under the director's Tick loop)

### S3 — behavioural tests
- [x] **T3.1** cascade on the organic net (panicked=7, peakOrcaPush=2, pedestrians=5, maxPedDist=274.6m ≫ 0.8·SafeRadius)
- [x] **T3.2** locality (50 tracked ⊊ 371 ever-active; 321 never tracked — the core Phase-5 property)
- [x] **T3.3** containment + determinism (no ped/pusher leaves navmesh; two runs bit-identical)
- [x] **T3.4** suite green (427 pass) + hash gate (`909605E965BFFE59`) + existing grid/TLS evac tests unchanged

### S4 — viz + measurement
- [ ] **T4.1** organic viz scene (Opus renders to confirm)
- [ ] **T4.2** cost profile at ~400 vehicles (dominant evac hotspot) — scopes Tier 2

## TIER 2 — 10k city (heavy optimization; outline, detailed later)
- [ ] FearField uniform grid (bit-identical)
- [ ] spatial composite CrowdSource + disc feeds (O(local) per query)
- [ ] enable OrcaCrowd spatial hash; MixedTrafficCrowd hash if needed
- [ ] 10k-city demo + viz payload management (region-crop / decimation, logged)
- [ ] working-region scan optimization (only if measured necessary)

---

### Proposed batches
- **Tier1-B1:** S1 (T1.1, T1.2) + S2 (T2.1, T2.2) + S3 tests — auto-attach + organic scenario + behavioural tests.
- **Tier1-B2:** S4 (T4.1 viz + T4.2 measurement) — render + measured cost profile.
- **Tier2-B*:** written as an addendum against Tier-1's measured profile.
