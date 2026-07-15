# SUMOSHARP-PACKAGING-TRACKER.md — at-a-glance to-do

Checklist for the packaging rethink. Task IDs → `SUMOSHARP-PACKAGING-TASKS.md`; design →
`SUMOSHARP-PACKAGING-DESIGN.md`. A box is ticked only when the task's success conditions are
verified first-hand (build / `dotnet pack` / `dotnet test`), per the CLAUDE.md accept gate.

## Baseline (integrated this session)
- [x] Fast-forward the Windows-GPU viewer branch (`native-viewer-perf-gpu`, main + 8 commits) into
      the packaging branch — brings DR/smoothing **code** (DrClock arc-window, lane-change straddle,
      ChordHeading, auto-delay, extrapolation low-pass) + **docs**.
- [x] DR/smoothing reimplementation guide present: `SUMOSHARP-VIEWER-DR-SMOOTHING.md` (+ lane-change
      design/tasks, DR-motion-jitter investigation).
- [x] Offline parity gate green after integration: **440 passed, 0 failed, 3 skipped**.

## Stage P0 — Reconcile docs with reality
- [x] P0.1 — Packaging design/tasks/tracker docs landed.
- [x] P0.2 — `SUMOSHARP-API.md §1` points here; two-package reality + retired `Runtime` recorded.

## Stage P1 — `SumoSharp.Viewer.Motion` (critical path)
- [ ] P1.1 — `DrClock` decoupled from `DdsSubscriber` (transport-neutral sample + history); branch/arc
      regression test.
- [ ] P1.2 — `Sim.Viewer.Motion` project created: net8+ns2.1, `IsPackable`, `PackageId=SumoSharp.Viewer.Motion`,
      no DDS/raylib deps; packs `lib/net8.0` + `lib/netstandard2.1`.
- [ ] P1.3 — DR/smoothing guide shipped as the package README (+ license/disclaimer).

## Stage P2 — `SumoSharp.Viewer.Raylib`
- [ ] P2.1 — raylib/ImGui component packable (`PackageId=SumoSharp.Viewer.Raylib`, native assets);
      `Sim.Viewer` exe reduced to a thin sample; viewer modes still run.

## Stage P3 — Dev-time & domain packages
- [ ] P3.1 — `SumoSharp.Testing` from `Sim.Harness`.
- [ ] P3.2 — `SumoSharp.Evac` from `Sim.Evac`.

## Stage P4 — Convenience & CI
- [ ] P4.1 — `SumoSharp` meta-package (Core + Ingest + Replication + Viewer.Motion).
- [ ] P4.2 — packaging guard test extended (portable-tier target/packability + no-native-leak invariants).
- [ ] P4.3 — publish CI packs the full shipped set on a `v*` tag.

## Already shipped before this session (context)
- [x] `SumoSharp.Core`, `SumoSharp.Ingest` — packable, net8+ns2.1, publish CI, B13 guard.
- [x] `SumoSharp.Replication`, `SumoSharp.Replication.Dds` — packable.
