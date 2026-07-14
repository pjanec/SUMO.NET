# PANIC-EVAC-PHASE5-TRACKER.md — scale checklist

Status for Phase 5 (`PANIC-EVAC-PHASE5-DESIGN.md` / `-TASKS.md`). Principle: **full city on the parity
engine; evac attaches only to a bounded working region around the incident** (cost ∝ local affected
population, not city size). Staged: Tier 1 now, Tier 2 (heavy opt, 10k target) after Tier-1 measurement.

> **Status:** owner approved staged (Tier 1 now, Tier 2 later; final goal a 10k-vehicle city, but the
> evac stays local). **Tier1-B1 DONE and Opus-reviewed** (427 pass / 3 skip; hash `909605E965BFFE59`
> unmoved; locality proven: 50/371 ever-active vehicles tracked). B1 surfaced a latent parity-core
> reroute bug (multi-lane active reroute read the stale original route → crash); fixed + committed
> separately with a fail-pre-fix/pass-post-fix core regression test (`RungB3MultilaneRerouteRegressionTests`).
> **Tier1-B2 DONE and Opus-reviewed** (viz rendered t=80/180/290; cost profile measured — see S4). **All
> of Tier 1 is complete.** Tier-2 priority is now set by the measured profile (viz payload + auto-track
> scan lead; evac-phase optimization gated on working-region population). Awaiting go for a Tier-2 addendum.

## TIER 1 — realistic organic town + local auto-attach
### S1 — working-region auto-attach
- [x] **T1.1** `EvacConfig.WorkingRadius` (default 250; documented ≥ incident radius + jam margin)
- [x] **T1.2** `EvacDirector` auto-track-by-region (deterministic sort by handle.Index; in-region only; off by default → grid/TLS demos unaffected)

### S2 — organic scenario
- [x] **T2.1** `EvacOrganicScenario` (LoadScenario city-organic-L2 net+demand; incident at junction 415, the busiest interior TLS junction)
- [x] **T2.2** demand-under-director confirmation (peakActive=231 under the director's Tick loop)

### S3 — behavioural tests
- [x] **T3.1** cascade on the organic net (panicked=174, peakOrcaPush=10, pedestrians=604, maxPedDist=915.6m ≫ 0.8·SafeRadius)
- [x] **T3.2** locality (212 tracked ⊊ 371 ever-active; 159 never tracked — the core Phase-5 property)
- [x] **T3.3** containment + determinism (no ped/pusher leaves navmesh; two runs bit-identical)
- [x] **T3.4** suite green (427 pass) + hash gate (`909605E965BFFE59`) + existing grid/TLS evac tests unchanged

### S4 — viz + measurement
- [x] **T4.1** organic viz scene (`SceneGen.BuildEvacOrganic`, `Sim.Viz --evac-organic`; Opus rendered t=80/180/290 — realistic mesh, town-wide congestion, a large central incident disc with ~600 pedestrians + 151 abandoned cars radiating outward while the periphery stays pure parity traffic; payload 3.3 MB)
- [x] **T4.2** cost profile (`Sim.EvacProfile`; opt-in `EvacDirector` profiler, off by default) — **dominant evac hotspot = pusher step, closely followed by pedestrian step**

### Tier-1 measured cost profile (the input that scopes Tier 2)
Tuned demo config (incident radius 400 → mass exodus, the representative heavy load). `Sim.EvacProfile`,
organic town, 300 ticks, ~174 panic / 151 abandoned / 604 pedestrians, total 2.95 s:

| phase | ms | % tick | note |
|---|---|---|---|
| **pusher step** | **1318** | **39.7 %** | `DriveOrcaPushers` (`MixedTrafficCrowd`, 151 shaped NH movers × sub-steps) — dominant |
| **pedestrian step** | **1127** | **33.9 %** | `DrivePedestrians` (`OrcaCrowd`, 604 peds × `CrowdSubSteps`) — second |
| engine.Step | 704 | 21.2 % | parity core (drops as ~150 cars abandon and leave the driving sim) |
| other | 121 | 3.7 % | auto-track scan, blocked detector, bookkeeping |
| fear update | 34 | 1.0 % | |
| disc feeds | 9 | 0.3 % | |

**Key conclusion (scopes Tier 2):** under a realistic heavy evac load the two O(n²) crowd solvers
dominate — **pusher step (40 %) + pedestrian step (34 %) = ¾ of tick time** — exactly the design-§6
hypothesis. Fear/disc feeds are negligible (< 1.5 % combined), so a FearField grid is NOT a priority.
The evac cost is a function of the **working-region population**, not city size; this run's ~600
pedestrians / 151 pushers is the low-thousands-adjacent scale Tier 2 must handle. (An earlier
5-pedestrian run put the evac layer at only ~10 % — that under-represented the load; this tuned run is
the honest scoping input.)

## TIER 2 — 10k city (heavy optimization; outline, detailed later)
Priority now set by the measured profile above:
- [ ] **`MixedTrafficCrowd` spatial hash for pushers** — the #1 measured hotspot (pusher step, 40 %)
- [ ] **enable `OrcaCrowd.UseSpatialHash` for pedestrians** (already implemented; enable + verify bit-identical) — the #2 hotspot (pedestrian step, 34 %)
- [ ] **viz payload management** for a 10k city (region-crop / decimation / caps, logged) — city-size-driven cost (3.3 MB here)
- [ ] **auto-track scan** optimization (spatial query instead of full O(city) read-buffer scan each tick) — measure first at 10k
- [ ] spatial composite CrowdSource / disc feeds (bit-identical), FearField grid — LOW priority (fear/disc < 1.5 % measured)
- [ ] 10k-city demo scenario

---

### Proposed batches
- **Tier1-B1:** S1 (T1.1, T1.2) + S2 (T2.1, T2.2) + S3 tests — auto-attach + organic scenario + behavioural tests.
- **Tier1-B2:** S4 (T4.1 viz + T4.2 measurement) — render + measured cost profile.
- **Tier2-B*:** written as an addendum against Tier-1's measured profile.
