# Native viewer demo tool + live evac — tracker

Checklist for `docs/SUMOSHARP-VIEWER-DEMO-EVAC-TASKS.md` (design: `…-DESIGN.md`). A box is ticked ONLY
after Opus confirms the task's success conditions first-hand (diff read + gate re-run + Xvfb screenshot).

Standing gate on every tick: `dotnet test Traffic.sln` = 446/3/0 · `Sim.Bench` hash `909605E965BFFE59`
(single + parallel).

## Stage A — seam + evac data path
- [x] **T1** `SimulationRunner.OnAfterStep` hook (in-solution; byte-identical hash + suite) — Opus-verified: hook inert, gate 446/3/0, hash `909605E965BFFE59` single+parallel.
- [x] **T2** `EvacRenderSnapshot` + `EngineHost` evac path — Opus-verified: harness re-run first-hand, cascade fires for grid-tls (panic→abandon→peds), organic (39 panicked/32 peds), city (472 panicked, 5090 fear-tracked @10k).

## Stage B — catalog + switching
- [x] **T3** `DemoCatalog` — Opus-verified: 25 usable entries, 7 categories, all 3 evac kinds, rbl-left-turns present.
- [x] **T4** live demo switching + `--demo` — Opus-verified first-hand: --demo-smoke (Priority→Traffic-light→Evac, distinct edge counts, evac present, clean) + Xvfb `--demo "Roundabout"` screenshot renders the net + vehicle + HUD; ad-hoc `--mode local <path>` path preserved.

## Stage C — rendering + UI
- [x] **T5** ImGui "Demos" picker + non-evac polish — Opus-verified: categorized picker with current-highlight + evac legend/counters; DrawControlsPanel hides random-traffic + swaps the click hint for evac demos.
- [x] **T6** evac draw pass + place-incident click — Opus-verified first-hand (Xvfb screenshots + exact-color pixel scans): amber incident zone+ring, dashed boundary, fear-tinted vehicles, and peds/abandoned cars render (organic: cyan-fleeing=1, escaped-green=223, abandoned-red=56; grid-tls: green=24, red=98). Pure-SUMO demos carry 0 evac-color pixels (unchanged); left-click places the incident in evac demos.

## Stage D — close-out
- [x] **T7** docs + final gate — `docs/SUMOSHARP-NATIVE-VIEWER.md` documents the demo tool + live-evac mode; README "Live & native viewers" updated with `--demo` + live panic-evac. Final gate re-confirmed: `dotnet test Traffic.sln` = 446/3/0, `Sim.Bench` hash `909605E965BFFE59` single+parallel.

Status: **COMPLETE — all of T1–T7 landed + Opus-verified. The native viewer's `local` mode is now an interactive demo tool (ImGui scenario picker) covering pure-SUMO scenarios and the live panic-evacuation feature.**
