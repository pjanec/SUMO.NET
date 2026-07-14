# PANIC-EVAC-PHASE5-TIER2-TRACKER.md — 10k-city optimization checklist

Status for Phase-5 Tier 2 (`PANIC-EVAC-PHASE5-TIER2-DESIGN.md` / `-TASKS.md`). Priority is set by the
**measured** Tier-1 profile: the two O(m²) crowd solvers (pusher 40 % + pedestrian 34 %) come first, then
the city-size items (10k scenario, viz payload, auto-track scan). Both new hashes are opt-in + proven
bit-identical; parity hash `909605E965BFFE59` stays unmoved throughout.

> **Status:** **Tier2-B1 + Tier2-B2 DONE and Opus-reviewed.** Both crowd hashes bit-identical; 10k-city
> evac demo runs + renders on the committed `city-15000` host; before/after profile measured. 436 pass /
> 3 skip; hash `909605E965BFFE59` unmoved. **Tier 2 (planned scope) complete** — remaining items are
> measurement-gated and NOT warranted by T2.7 (see Deferred).

## STAGE T2-S1 — spatial-hash the two crowd solvers
- [x] **T2.1** `MixedTrafficCrowd` uniform-grid neighbour query — brute+grid share `GatherVehicleNeighbours` (grid = sorted 3×3-cell candidates) → bit-identical by construction; `MixedTrafficSpatialHashTests` asserts exact Position+Heading equality incl. MaxNeighbours-binding + non-holonomic paths. Opt-in, default off.
- [x] **T2.2** `EvacConfig.UseCrowdSpatialHash` enables the hash on BOTH `_peds` (OrcaCrowd) and `_mover` (MixedTrafficCrowd via VehicleMover pass-through); `EvacCrowdSpatialHashTests` proves the 604-ped/151-pusher organic demo signature is identical off vs on.
- [x] **T2.3** micro-benchmark (`Sim.EvacProfile --microbench`): at N=2000 OrcaCrowd **3.7×**, MixedTrafficCrowd **2.65×**; crossover ~N=1000 (grid slightly slower at N=250 due to rebuild overhead, as expected).

## STAGE T2-S2 — the 10k demo + payload/scan handling
- [x] **T2.4** 10k host = committed `scenarios/_bench/city-15000` (design §3a option A, the de-risked primary). The 2-lane `--rand` 10k spike (option B, stretch) was NOT delivered in this batch — deferred; the demo runs on the grid host as designed.
- [x] **T2.5** `EvacCityScenario` (incident radius 1800 / WorkingRadius 2200 / SafeRadius 2500 at the mesh centre; hashes on). `EvacCityDemoTests`: cascade + **tracked working-region pop. 357** (test window, floor 300) / **935** (full 300-tick profiler run) + locality (357 ⊊ 2535; 2178 never tracked) + determinism. Caught + fixed a real bug (SafeRadius must exceed WorkingRadius or peds spawn already-escaped).
- [x] **T2.6** viz payload management (`BuildEvacCity` + `--evac-city`): region-crop (1418 in-region + 271/5413 sampled distant, 5142 dropped) + frame-decimation (133/400), **all logged**; **7.36 MiB** (< 15 MB budget); Opus rendered t=250/340 — 10k grid reads, local exodus clusters at the incident, periphery flows.
- [x] **T2.7** before/after (`--city`, 300 ticks, 935 tracked / 167 pushers-ever / 652 peds): **pusher 1.08× · pedestrian 1.19× · total 1.14×**; engine.Step ~47 % (city-size floor, ~11 s / 300 ticks); **auto-track scan 0.2 % → NOT material, no optimization warranted.**

### Tier-2 measured finding (honest)
The hashes are correct + bit-identical but give only a **modest 1.14×** at city scale — because the evac
crowds **cluster tightly around the incident**, so a uniform grid's 3×3 block still contains most of the
local population and prunes little (unlike the spread-out micro-benchmark, 2.65–3.7× at N=2000). The
city-size floor is `engine.Step` (parity core, ~47 %), already tractable at 10k. Net: the 10k evac is
demonstrably tractable + watchable; the crowd-hash win grows with working-region SPREAD, not just count.

## Deferred (measurement-gated — T2.7 confirmed none are warranted yet)
- [ ] spatial `QueryNear` / disc feeds — disc feeds measured 0.2 % at 10k; NOT warranted.
- [ ] FearField grid — fear update measured 2.5 % at 10k; NOT warranted.
- [ ] auto-track scan optimization — measured 0.2 % at 10k; NOT warranted.
- [ ] 2-lane `--rand` 10k organic host (T2.4 option B) — optional realism upgrade over the grid host.
- [ ] cluster-aware crowd pruning — the only lever that would materially beat 1.14× (grid prunes little when agents cluster); open question, not obviously worth it.

---

### Proposed batches
- **Tier2-B1:** T2.1 + T2.2 + T2.3 — core algorithmic win (crowd-solver hashes), self-contained.
- **Tier2-B2:** T2.4 + T2.5 + T2.6 + T2.7 — 10k demo + closing before/after profile.
