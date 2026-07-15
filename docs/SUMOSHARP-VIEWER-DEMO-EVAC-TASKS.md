# Native viewer demo tool + live evac — tasks & success conditions

Implements `docs/SUMOSHARP-VIEWER-DEMO-EVAC-DESIGN.md` (sections referenced as §N). Tracked by
`…-TRACKER.md`. Delegation model (CLAUDE.md): **Sonnet implements a batch; Opus reviews HARD** (reads the
diff, re-runs the gate + the Xvfb screenshot itself, ticks the box only on first-hand confirmation).

**Standing invariant for every task (re-verified, not assumed):**
`dotnet test Traffic.sln` → **446 passed / 3 skipped / 0 failed**, and
`dotnet run -c Release --project src/Sim.Bench` → hash **`909605E965BFFE59`** (single AND parallel).
The viewer projects are out of `Traffic.sln`; the only in-solution edit in the whole feature is T1.

---

## Stage A — the seam + evac data path (headless-verifiable, no rendering)

### T1 — `SimulationRunner.OnAfterStep` hook  (§2)  [in-solution]
- **Files:** `src/Sim.Core/SimulationRunner.cs`.
- **Do:** add `public Action<Engine>? OnAfterStep { get; set; }`; invoke it in `Tick()` as
  `_engine.Step(); OnAfterStep?.Invoke(_engine);` — before the snapshot capture. Comment it as
  inert-when-null.
- **Success:** builds; `dotnet test Traffic.sln` = 446/3/0; `Sim.Bench` hash `909605E965BFFE59` single +
  parallel — i.e. **byte-identical** with the hook present but unset. (This is the whole point of T1.)

### T2 — `EvacRenderSnapshot` + `EngineHost` evac path  (§3, §4)
- **Files:** new `src/Sim.Viewer.Core/EvacRenderSnapshot.cs`; edit `src/Sim.Viewer.Core/EngineHost.cs`;
  add `<ProjectReference>` to `Sim.Evac` in `Sim.Viewer.Core.csproj`.
- **Do:** `EvacRenderSnapshot` per §3. `EngineHost.CreateEvac(evacKind, repoRoot, incident?)` binding the
  right `Evac*Scenario.Build`; in `BuildSim()` use the provider when set (director built, `_randomTraffic
  = false`, `runner.OnAfterStep = tick+capture`), expose `EvacRenderSnapshot? Evac`, add
  `SetIncidentAtWorld(wx,wy)`. `CaptureEvac(director)` reads the §3 fields off the director.
- **Success (headless console harness in `Sim.Viewer.Core` or a scratch runner):** for each evac kind
  (`grid-tls`, `organic`, `city`) build the host, let it run past the incident start, and assert the
  published `Evac` snapshot shows the cascade: `Panicked > 0`, then `Abandoned > 0` and `Peds.Length > 0`
  within a bounded number of steps; `Boundary` non-degenerate; `Incident` matches the built spec. No
  exceptions; disposes cleanly. (`grid-tls` is the fast one to gate on; organic/city just must not throw
  and must produce peds.)

---

## Stage B — the catalog + live switching (pure-SUMO demos work end-to-end)

### T3 — `DemoCatalog`  (§1)
- **Files:** new `src/Sim.Viewer.Core/DemoCatalog.cs`.
- **Do:** `DemoEntry`/`DemoKind`/`DemoCategory` per §1 + the curated list. Repo-root resolution (walk to
  `Traffic.sln`). A `Resolve()` that drops entries whose path is missing (logged), returning only usable
  demos.
- **Success:** a unit/console check enumerates the catalog on the real repo and asserts: every non-evac
  entry's path exists; the three evac kinds are present; ≥ 15 usable entries spanning ≥ 5 categories;
  the RBL-fix diag (`_diag/rbl-left-turns`) is included.

### T4 — live demo switching + `--demo`  (§5)
- **Files:** `src/Sim.Viewer/Program.cs` (+ a small session holder, may live in `Sim.Viewer.Core`).
- **Do:** build the initial host from `--demo "<name>"` (or first Junctions entry; `--mode local <path>`
  still works as an ad-hoc "(custom)" demo). A swap-under-lock holder the render loop reads; switching
  disposes the old host then builds the new; camera re-fits to new bounds.
- **Success (Xvfb):** `--demo "<a pure-SUMO scenario>" --screenshot a.png --frames 120` renders that
  scenario (roads + vehicles). A second run with a different `--demo` renders a visibly different net.
  No leak/hang across a switch (a scripted 2-switch console smoke test exits cleanly).

---

## Stage C — rendering + UI (the demo tool proper)

### T5 — ImGui "Demos" panel + non-evac polish  (§6 panel half)
- **Files:** `src/Sim.Viewer/Renderer.cs`.
- **Do:** categorized collapsing picker (blurb tooltips, current highlighted) that drives T4's switch;
  keep the existing controls panel; hide obstacle-drop/random-traffic for evac demos (wired in T6).
- **Success (Xvfb):** screenshot shows the picker with categories populated; clicking (scripted via a
  forced initial `--demo`, since Xvfb has no mouse) confirms the highlighted entry matches the loaded net.

### T6 — evac draw pass + evac controls  (§6 world half)
- **Files:** `src/Sim.Viewer/Renderer.cs` (+ left-click routing to `SetIncidentAtWorld` for evac demos).
- **Do:** draw boundary, incident zone + safe ring, fear-tinted vehicles, abandoned cars, pedestrians
  (cyan→green), pushers (orange) from `host.Evac`; colours per `SceneGen` kinds. Legend + live counters;
  left-click (re)places the incident in evac demos.
- **Success (Xvfb):** `--demo "Evacuation (grid TLS)" --frames <past incident start> --screenshot
  evac.png` shows the amber incident zone, pedestrian discs, and ≥ 1 abandoned car; a frame BEFORE the
  incident shows only traffic (faint incident outline). Pure-SUMO demos are visually unchanged (no evac
  overlay, pure speed colour) — confirm with a re-shot non-evac demo.

---

## Stage D — close-out

### T7 — docs + final gate
- **Files:** `docs/SUMOSHARP-NATIVE-VIEWER.md` (add the demo-tool + live-evac mode), README one-liner in
  the "Live & native viewers" block, this tracker.
- **Success:** full `dotnet test Traffic.sln` 446/3/0 + `Sim.Bench` hash unmoved (final re-confirm after
  all stages); `docs/SUMOSHARP-NATIVE-VIEWER.md` describes `--demo` + the evac category; screenshots from
  T4/T6 attached to the tracker. Build clean incl. both viewer projects.

## Batching for delegation
- **Batch 1:** T1 → T2 (data path; gate-critical hook first, verified before anything builds on it).
- **Batch 2:** T3 → T4 (catalog + switching; pure-SUMO demo tool usable).
- **Batch 3:** T5 → T6 (UI + evac rendering).
- **Batch 4:** T7 (docs + final gate).
Opus reviews each batch first-hand (diff read + gate re-run + Xvfb screenshot) before the next is issued.
