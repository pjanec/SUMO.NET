# Native viewer demo tool + live evac — tracker

Checklist for `docs/SUMOSHARP-VIEWER-DEMO-EVAC-TASKS.md` (design: `…-DESIGN.md`). A box is ticked ONLY
after Opus confirms the task's success conditions first-hand (diff read + gate re-run + Xvfb screenshot).

Standing gate on every tick: `dotnet test Traffic.sln` = 440/3/0 · `Sim.Bench` hash `909605E965BFFE59`
(single + parallel).

## Stage A — seam + evac data path
- [ ] **T1** `SimulationRunner.OnAfterStep` hook (in-solution; byte-identical hash + suite)
- [ ] **T2** `EvacRenderSnapshot` + `EngineHost` evac path (cascade appears headlessly for all 3 kinds)

## Stage B — catalog + switching
- [ ] **T3** `DemoCatalog` (≥15 usable entries, ≥5 categories, RBL-fix diag included)
- [ ] **T4** live demo switching + `--demo` (two demos render different nets; clean switch)

## Stage C — rendering + UI
- [ ] **T5** ImGui "Demos" picker + non-evac polish
- [ ] **T6** evac draw pass (incident zone / peds / abandoned / pushers / fear tint) + place-incident click

## Stage D — close-out
- [ ] **T7** docs (native-viewer doc + README) + final gate re-confirm + screenshots attached

Status: **design approved, implementation not started.**
