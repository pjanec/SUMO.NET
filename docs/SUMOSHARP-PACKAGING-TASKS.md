# SUMOSHARP-PACKAGING-TASKS.md — staged tasks & success conditions

Work breakdown for the packaging rethink. **Design reference:** `SUMOSHARP-PACKAGING-DESIGN.md`
(sections cited per task — not restated here). **Tracker:** `SUMOSHARP-PACKAGING-TRACKER.md`.

**Global invariants (every task must hold these):**
- **G1 — Parity iron law.** `dotnet test` stays green and native-free (baseline **440 passed,
  0 failed, 3 skipped**; `Sim.Bench` determinism anchor unchanged). No simulation trajectory moves.
- **G2 — No native leak into the portable tier.** Nothing in `Ingest`/`Core`/`Replication`/
  `Viewer.Motion` may `PackageReference` a native dep (CycloneDDS, raylib, rlImgui) or
  `ProjectReference` a project that does.
- **G3 — Hermetic tests.** New guard tests read committed csproj/source only — no build, no SUMO,
  no network (same style as `RungB13PackagingTargetsTests`).
- **G4 — Additive metadata.** Packaging changes touch project metadata + code *organisation* only;
  public simulation APIs are unchanged except the `DrClock` decoupling (P1), which is viewer-side.

---

## Stage P0 — Reconcile the design docs with reality

### P0.1 — Land the packaging design docs
- **Design ref:** whole `SUMOSHARP-PACKAGING-DESIGN.md`.
- **Files:** `docs/SUMOSHARP-PACKAGING-DESIGN.md`, `docs/SUMOSHARP-PACKAGING-TASKS.md`,
  `docs/SUMOSHARP-PACKAGING-TRACKER.md` (this set).
- **Success:** the three docs exist, cross-reference each other, and the design doc's package graph
  covers every current `src/` project (§6 table has a row per project).

### P0.2 — Point `SUMOSHARP-API.md §1` at the new design
- **Design ref:** §0, §3 (D1, D3).
- **Files:** `docs/SUMOSHARP-API.md` (§1 only).
- **Success:** §1 carries a top note "Packaging layout is now owned by
  `SUMOSHARP-PACKAGING-DESIGN.md`"; the stale "Core = Sim.Core + Sim.Ingest bundled" row is corrected
  to the shipped two-package reality; the `SumoSharp.Runtime` row is marked **retired** (D3). No
  other §-content changed. `docs/` prose builds no code, so G1 is trivially met.

---

## Stage P1 — `SumoSharp.Viewer.Motion` (the load-bearing refactor)

### P1.1 — Decouple `DrClock` from `DdsSubscriber`
- **Design ref:** §5.
- **Files:** `src/Sim.Viewer.Core/DrClock.cs`, `src/Sim.Viewer.Core/DdsSubscriber.cs` (adapter),
  callers in `src/Sim.Viewer/Program.cs`.
- **Change:** introduce a transport-neutral `VehicleSample` type and an `IVehicleSampleHistory`
  (read-only, newest-last) in the motion namespace; change `DrClock.Resolve` to consume them; have
  `DdsSubscriber` expose/adapt its buffer to `IVehicleSampleHistory` instead of `DrClock` naming
  `DdsSubscriber.VehicleSample`/`DdsSubscriber.HistoryCap`.
- **Success:**
  1. `grep -R "DdsSubscriber" src/Sim.Viewer.Core/DrClock.cs` returns **nothing** (no comment or
     type reference).
  2. Solution builds; the loopback + remote viewer paths in `Program.cs` compile against the new
     signature.
  3. **G1** holds (the parity gate never built the viewer, so it must stay green unchanged).
  4. A focused unit test constructs a hand-built history and asserts `Resolve` picks the same
     interpolate/extrapolate branch and arc as before the refactor for a straight-lane and a
     junction-straddle case (regression pin for §5.2 behaviour).

### P1.2 — Create the `Sim.Viewer.Motion` project (portable, packable)
- **Design ref:** §4, §5, §6.
- **Files:** new `src/Sim.Viewer.Motion/Sim.Viewer.Motion.csproj` + moved `DrClock.cs`, the new
  sample/history types, and the DR-pipeline scalar helpers (auto-delay §5.4, extrapolation low-pass
  §5.5) extracted from `Program.cs`; `Traffic.sln`; `Sim.Viewer.Core`/`Sim.Viewer` references.
- **csproj shape:** `<TargetFrameworks>net8.0;netstandard2.1</TargetFrameworks>`,
  `IsPackable=true`, `PackageId=SumoSharp.Viewer.Motion`, `ProjectReference` → `Sim.Core`,
  `Sim.Ingest`, `Sim.Replication` **only**; link `src/Shared/NetstandardPolyfills.cs`;
  `System.Memory` on the ns2.1 target if needed.
- **Success:**
  1. `dotnet build src/Sim.Viewer.Motion -f netstandard2.1` and `-f net8.0` both succeed.
  2. The project has **no** `ProjectReference` to `Sim.Replication.Dds` and **no** `Raylib`/`rlImgui`
     `PackageReference` (**G2**).
  3. `dotnet pack src/Sim.Viewer.Motion -c Release` produces a `.nupkg` containing `lib/net8.0` **and**
     `lib/netstandard2.1`.
  4. **G1** holds.

### P1.3 — Ship the DR/smoothing guide as the package README
- **Design ref:** §5 (last paragraph).
- **Files:** package README referencing/embedding `docs/SUMOSHARP-VIEWER-DR-SMOOTHING.md`.
- **Success:** the `.nupkg` `PackageReadmeFile` renders the reimplementation guide (§8 of the guide —
  "reimplementing in a future viewer") and carries the license/disclaimer (design §8).

---

## Stage P2 — `SumoSharp.Viewer.Raylib`

### P2.1 — Make the raylib viewer component packable; keep the exe a thin sample
- **Design ref:** §5 (last paragraph), §6.
- **Files:** `src/Sim.Viewer.Core` (retain as the raylib-tier host+DDS-adapter, now depending on
  `Viewer.Motion`), `src/Sim.Viewer/*` (exe reduced to a thin shell), csproj metadata, `Traffic.sln`.
- **csproj shape:** the reusable component `IsPackable=true`, `PackageId=SumoSharp.Viewer.Raylib`,
  net8.0, `ProjectReference` → `Viewer.Motion` + `Replication.Dds`; native raylib/ImGui + TTF asset
  packaged as content/runtime assets.
- **Success:**
  1. `dotnet pack` produces `SumoSharp.Viewer.Raylib.<v>.nupkg` with the native raylib runtime assets
     and the bundled font.
  2. The `Sim.Viewer` exe still runs the local/loopback/remote modes (manual smoke per
     `README.md` "Live & native viewers"); no reusable logic remains in `Program.cs` beyond arg
     parsing + the render loop wiring.
  3. `Viewer.Raylib` is one of exactly two packable projects carrying a native dep (the other:
     `Replication.Dds`) — asserted by the guard (P4.2).
  4. **G1** holds.

---

## Stage P3 — Dev-time & domain packages

### P3.1 — `SumoSharp.Testing` (from `Sim.Harness`)
- **Design ref:** §2 (Tier 3), §3 (D6).
- **Files:** `src/Sim.Harness/Sim.Harness.csproj`, a package README.
- **Success:** `IsPackable=true`, `PackageId=SumoSharp.Testing`, net8.0; `dotnet pack` yields the
  `.nupkg`; the parity test project still references `Sim.Harness` by project ref (unchanged); **G1**.

### P3.2 — `SumoSharp.Evac` (from `Sim.Evac`)
- **Design ref:** §2 (Tier 4), §3 (D6).
- **Files:** `src/Sim.Evac/Sim.Evac.csproj` (flip `IsPackable` to true), a package README.
- **Success:** `IsPackable=true`, `PackageId=SumoSharp.Evac`, net8.0, `ProjectReference` → `Sim.Core`;
  `dotnet pack` yields the `.nupkg`; **G1** (Evac is in the parity test graph — trajectory must not
  move).

---

## Stage P4 — Convenience & CI

### P4.1 — `SumoSharp` meta-package
- **Design ref:** §2 (Convenience), §3 (D7).
- **Files:** new `packaging/SumoSharp.Meta/SumoSharp.Meta.csproj` (no code; `PackageId=SumoSharp`,
  `<PackageReference>`-as-dependency to Core + Ingest + Replication + Viewer.Motion).
- **Success:** `dotnet pack` yields a code-less `SumoSharp.<v>.nupkg` whose `.nuspec` lists the four
  dependency package IDs; installing it into a fresh console project restores all four.

### P4.2 — Extend the packaging guard test
- **Design ref:** §7.
- **Files:** `tests/Sim.ParityTests/RungB13PackagingTargetsTests.cs` (extend) or a sibling
  `PackagingLayoutTests.cs`.
- **Success (all hermetic, source-reading):**
  1. `Sim.Replication` and `Sim.Viewer.Motion` assert `<TargetFrameworks>` net8+ns2.1, `IsPackable=true`,
     and the expected `PackageId`.
  2. `Sim.Viewer.Motion.csproj` contains **no** `Sim.Replication.Dds` project ref and **no**
     `Raylib`/`rlImgui` package ref.
  3. Exactly two packable projects reference a native dep: `Sim.Replication.Dds`, `Sim.Viewer`
     component (`Viewer.Raylib`).
  4. The new test runs inside `dotnet test` and passes; total test count rises; **G1** otherwise
     unchanged.

### P4.3 — Publish CI covers the new package IDs
- **Design ref:** §2, `SUMOSHARP-API.md §1` STATUS note.
- **Files:** `.github/workflows/publish.yml`.
- **Success:** a `v*` tag packs the full shipped set (Core, Ingest, Replication, Replication.Dds,
  Viewer.Motion, Viewer.Raylib, Testing, Evac, meta) at the tag version, uploads artifacts, pushes
  `.nupkg`+`.snupkg` with `--skip-duplicate`; push is *skipped not failed* when `NUGET_API_KEY` is
  absent (fork dry-run preserved). The offline parity gate remains a required pre-pack step.

---

## Sequencing notes
- **P0 first** (docs; zero code risk). **P1 is the critical path** — it is the only real code
  refactor and everything viewer-facing depends on it. P2 depends on P1. P3 is independent of P1/P2
  and can run in parallel. P4 depends on all prior stages.
- Each stage closes only when its success conditions are **verified first-hand** (build/pack/`dotnet
  test` re-run), per the CLAUDE.md orchestration gate — a reported "done" is unverified until proven.
