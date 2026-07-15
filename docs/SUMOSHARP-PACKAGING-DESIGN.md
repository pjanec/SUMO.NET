# SUMOSHARP-PACKAGING-DESIGN.md — à-la-carte NuGet packaging (the rethink)

**Status:** design of record for how this repo is sliced into NuGet packages.
**Supersedes** the package table in `SUMOSHARP-API.md §1`, which predates Replication, the
native viewer, `PoseResolver`/`DrClock`, and the evacuation subsystem. `SUMOSHARP-API.md` still
owns the *public-API surface* design (handles, read API, execution model); this doc owns *how the
assemblies are grouped, targeted, and shipped*. **Companion docs:** `SUMOSHARP-API.md` (API),
`SUMOSHARP-VIEWER-DR-SMOOTHING.md` (the render-side motion reconstruction that the viewer/motion
package documents and ships), `DESIGN.md` (simulation architecture).

**WHAT this is for:** the goal the user set — turn this repo into NuGet package(s) so the SUMO
port drops cleanly into a **simulation or game engine**, letting an integrator take **only what
they need** (engine only; engine + streaming; engine + streaming + render-side motion; the full
desktop viewer; dev-time tooling). Pay-for-what-you-use, native/heavy dependencies quarantined at
the leaves, one portable core.

---

## 0. Why a rethink (what changed since `SUMOSHARP-API.md §1`)

The original §1 table listed three intended packages — `SumoSharp.Core` (Sim.Core **+** Sim.Ingest
bundled), an optional `SumoSharp.Runtime`, and a dev-time `SumoSharp.Tools`. Since then:

1. **Two packages shipped, not one.** As-built, `Sim.Core` and `Sim.Ingest` publish as **separate**
   packages (`SumoSharp.Core`, `SumoSharp.Ingest`). The §1 table's "bundle them" intent never
   happened, and the split is actually the *better* shape (see §3). This doc adopts the reality.
2. **Replication landed** (`SumoSharp.Replication`, `SumoSharp.Replication.Dds`) — a whole
   transport-agnostic dead-reckoning wire layer plus a DDS transport. Absent from §1 entirely.
3. **The render-side motion stack landed.** `PoseResolver` + `ILaneShapeSource` (in `Sim.Core`) and
   `DrClock` (in `Sim.Viewer.Core`) turn sparse authoritative/DDS samples into smooth per-frame
   poses. This is the single most reusable thing a *game/3D* integrator wants, and it is currently
   **not packaged** and **entangled with the DDS transport** (see §5).
4. **A native desktop viewer landed** (`Sim.Viewer`, raylib + ImGui) with a headless brain
   (`Sim.Viewer.Core`). Native, desktop-only, not packaged.
5. **A panic-evacuation subsystem landed** (`Sim.Evac`) — an optional domain extension over the
   Core seams. Not packaged.
6. **`SumoSharp.Runtime` never split out.** The async `SimulationRunner` lives in `Sim.Core` today
   and carries no extra dependencies. This doc **retires** the separate-Runtime idea (see §3, D3).

The user's ask names all of these: viewer tools as (maybe standalone) packages, and a
reimplementation guide for dead-reckoning/smoothing across viewers. So the packaging surface has to
grow past the four shipped packages, and the growth has to stay **layered and optional**.

---

## 1. Design principles (the rails)

1. **One portable core, minimal dependencies.** `SumoSharp.Core` + `SumoSharp.Ingest` stay
   pure-managed and multi-target `net8.0;netstandard2.1` so **Unity (Mono/IL2CPP), Godot, and
   console/server** hosts can all consume them. Nothing in the base graph pulls a native binary.
2. **Quarantine native / heavy dependencies at the leaves.** `CycloneDDS.NET` (native DDS) and
   `Raylib-cs`/`rlImgui-cs` (native GPU + ImGui) live **only** in leaf packages an integrator opts
   into. A game engine that has its own renderer must be able to take the motion math **without**
   raylib, and the streaming client **without** committing to DDS.
3. **Separate "reconstruct motion" (pure math) from "render motion" (a windowing/GPU toolkit).**
   This is the load-bearing new split (§5). The reconstruction pipeline (`DrClock`, `PoseResolver`,
   `ILaneShapeSource`, auto-delay, extrapolation low-pass) is portable scalar maths and belongs in
   its own portable package; raylib/ImGui belong in an optional desktop-viewer package.
4. **Transport is pluggable, and its abstraction is portable.** `SumoSharp.Replication` holds the
   wire records + codec + publish policy with no transport dependency; `SumoSharp.Replication.Dds`
   is one transport. A future WebSocket/ENet transport is a sibling leaf, never a Core concern.
5. **Additive and parity-inert.** Packaging changes touch only project metadata and *code
   organisation*; they must never alter a simulation trajectory. The offline parity gate
   (`dotnet test`, no SUMO, no native libs) stays byte-identical, and a guard test pins the
   packaging invariants (§7). This is the CLAUDE.md iron law applied to packaging.
6. **Every package is self-describing and legally honest.** Each carries the dual-license
   (`EPL-2.0 OR GPL-2.0-or-later`) and the "unofficial reimplementation of Eclipse SUMO" disclaimer
   in its own README (§8).

---

## 2. The package graph (target state)

Tiers are strictly layered — an arrow means "depends on". Native/heavy leaves are marked ⚠.

```
Tier 0 — Engine (portable: net8.0 + netstandard2.1)
  SumoSharp.Ingest      parsers + net/rou/sumocfg model                     (leaf)
  SumoSharp.Core        engine, obstacle store, SoA read API, runtime        → Ingest
                        demand, SimulationRunner, PoseResolver, ILaneShapeSource

Tier 1 — Replication / transport
  SumoSharp.Replication      DR wire records + packed codec + publish policy → Core   (portable)
  SumoSharp.Replication.Dds  ⚠ CycloneDDS transport (native)                → Replication (net8.0)

Tier 2 — Render-side motion / viewers
  SumoSharp.Viewer.Motion    DrClock + DR pipeline glue + sample history     → Core, Replication
                             (portable render-side reconstruction; NO raylib, NO DDS)   (portable)
  SumoSharp.Viewer.Raylib ⚠  the 2D desktop viewer as a reusable component   → Viewer.Motion,
                             (raylib + ImGui, native, desktop-only)             Replication.Dds (net8.0)

Tier 3 — Dev-time / tooling  (net8.0, optional, never referenced by a shipping game build)
  SumoSharp.Tools            SUMO-binary fetch + netconvert/duarouter wrappers          (net8.0)
  SumoSharp.Testing          Sim.Harness: FCD/tripinfo/summary parse + tolerance compare → Core (net8.0)

Tier 4 — Domain extensions
  SumoSharp.Evac             panic-evacuation subsystem over Core seams       → Core    (net8.0)

Convenience
  SumoSharp (meta)           references Core + Ingest + Replication (+ Viewer.Motion) — "just simulate & stream"
```

**Not packaged — shipped as samples/demos** (unchanged): `Sim.Run`, `Sim.Viz`, `Sim.Bench`,
`Sim.BenchCity`, `Sim.ExtDemo`, `Sim.EvacProfile`, `Sim.LiveHost`, and the `Sim.Viewer` **exe**
(the raylib viewer's *reusable* code moves into `SumoSharp.Viewer.Raylib`; the exe stays a thin
sample shell). These demonstrate; they are not the product.

### Legend of "who installs what"

| Integrator | Installs |
|---|---|
| Headless sim / training / digital-twin backend | `SumoSharp.Core` (+ `Ingest`) |
| …that streams state to a decoupled process | + `SumoSharp.Replication` + `SumoSharp.Replication.Dds` |
| **Game / 3D engine with its own renderer** | `SumoSharp.Core` + `SumoSharp.Viewer.Motion` (+ a transport) |
| Wants the batteries-included 2D desktop view | + `SumoSharp.Viewer.Raylib` |
| Content pipeline (build a `.net.xml` from OSM) | + `SumoSharp.Tools` (dev-time) |
| Validating their own scenarios vs SUMO goldens | + `SumoSharp.Testing` (dev-time) |
| Evacuation / crowd scenarios | + `SumoSharp.Evac` |
| "Just give me the usual bundle" | `SumoSharp` (meta) |

---

## 3. Per-package decisions

- **D1 — Keep `SumoSharp.Ingest` a separate package (do not fold into Core).** Reality already
  ships it separate; the split is clean (Ingest is a true leaf with no internal deps) and lets a
  host that feeds pre-parsed data or its own network format take the engine without the parser.
  `SumoSharp.Core` `PackageReference`s `SumoSharp.Ingest`, so `install Core` still transitively
  pulls it — the split costs a consumer nothing but buys flexibility. **The §1 table's "one bundled
  package" intent is dropped.**
- **D2 — `PoseResolver` + `ILaneShapeSource` stay in `SumoSharp.Core`.** They already live in
  `Sim.Core`, are dependency-light, and are shared by the engine's opt-in production render mode as
  well as by every viewer. Moving them out would churn Core's public API for no gain. The motion
  *pipeline* (`DrClock` + glue) is what moves into `Viewer.Motion` (§5).
- **D3 — Retire `SumoSharp.Runtime`.** The async `SimulationRunner` is in `Sim.Core` and adds no
  dependency. A separate package would be churn with no payoff; a training build that wants only the
  stepped API simply never touches `SimulationRunner`. Documented here so the stale §1 idea is
  formally closed.
- **D4 — `Replication` portable, `Replication.Dds` native-leaf.** Already the case; codified as a
  principle so no transport dependency ever creeps into `Replication` (which is `net8.0;ns2.1`).
- **D5 — Ship the raylib viewer as a *package* (`SumoSharp.Viewer.Raylib`), not only a sample.**
  The user explicitly wants "viewer tools (maybe standalone nugets)". The reusable brain is already
  split into `Sim.Viewer.Core`; this doc extends that split so the render component is consumable,
  while the `Sim.Viewer` exe remains a thin runnable sample. Native raylib keeps it a leaf.
- **D6 — `SumoSharp.Testing` and `SumoSharp.Tools` are dev-time packages.** Never referenced by a
  shipping game build. `Testing` = the existing `Sim.Harness`. `Tools` is still design-only (§2 of
  `SUMOSHARP-API.md`) — implement last, or ship as a `dotnet sumosharp` global tool.
- **D7 — `SumoSharp` meta-package** groups the common bundle (Core + Ingest + Replication, and
  optionally Viewer.Motion) for one-line discoverability; it contains no code.

---

## 4. Framework targeting & native isolation

- **Portable tier (`net8.0;netstandard2.1`):** `Ingest`, `Core`, `Replication`, **`Viewer.Motion`
  (new — must stay ns2.1-clean so Unity/Godot can reconstruct motion)**. The ns2.1 discipline
  (span-based one-API surface, `System.Memory` on ns2.1 only, `NetstandardPolyfills.cs`) already
  established for Core/Ingest/Replication (`SUMOSHARP-API.md §3`) extends verbatim to `Viewer.Motion`.
- **Native / net8-only leaves:** `Replication.Dds` (CycloneDDS), `Viewer.Raylib` (raylib + ImGui).
  net8.0 single-target; they carry the native runtime assets. Because they are leaves, an integrator
  who avoids them never pulls a native binary.
- **Dev-time net8-only:** `Tools`, `Testing`, `Evac`.
- The offline parity gate builds only the portable + managed set (the test project references Core,
  Ingest, Replication, Evac, Harness, GameHostSample — never DDS or raylib), so `dotnet test`
  stays hermetic and native-free. **This must not regress.**

---

## 5. The load-bearing refactor: a portable `SumoSharp.Viewer.Motion`

**Problem.** The reusable render-side reconstruction — the thing a game/3D integrator most wants —
is today split across `Sim.Core` (`PoseResolver`, `ILaneShapeSource`) and `Sim.Viewer.Core`
(`DrClock`). `Sim.Viewer.Core` is **not packable** and, worse, `ProjectReference`s
`Sim.Replication.Dds`, so it transitively drags the **native DDS binary**. And `DrClock.Resolve`
takes a `DdsSubscriber.VehicleSample` / `DdsSubscriber.HistoryCap` — i.e. the clock is **coupled at
the type level to the DDS subscriber**, even though `SUMOSHARP-VIEWER-DR-SMOOTHING.md §8` already
argues (correctly) that `DrClock` is *conceptually* transport-agnostic.

**Target seam** (matches `SUMOSHARP-VIEWER-DR-SMOOTHING.md §8`'s "clean seam"):

```
SumoSharp.Viewer.Motion  (net8.0;netstandard2.1, NO raylib, NO DDS)
  ├─ DrClock                 (moved from Sim.Viewer.Core, decoupled from DdsSubscriber)
  ├─ IVehicleSampleHistory   (new: transport-neutral per-vehicle sample buffer abstraction)
  ├─ VehicleSample           (new/relocated: lane + arc-pos + posLat + kinematics + upcoming + time)
  ├─ DrPipeline helpers      (auto-delay §5.4, extrapolation low-pass §5.5 — plain scalar maths)
  └─ (re-exports) PoseResolver, ILaneShapeSource, Pose, DrState   [these stay defined in Core]
      depends on → SumoSharp.Core (PoseResolver), SumoSharp.Replication (VehicleRecord shape)
```

The one real code change is **decoupling `DrClock` from `DdsSubscriber`**: introduce a
transport-neutral sample type + history interface in `Viewer.Motion`, have `DrClock.Resolve` take
those, and make the DDS subscriber (which stays in the raylib/DDS tier) *adapt* its samples to that
interface. This is a mechanical, parity-inert refactor (viewer-only code; `Sim.Core`'s
`PoseResolver` is untouched) — but it is the change that makes "reimplement DR in a different
viewer" a `PackageReference`, not a copy-paste. After it, the DR/smoothing guide
(`SUMOSHARP-VIEWER-DR-SMOOTHING.md`) becomes the **README/primary doc** of `SumoSharp.Viewer.Motion`.

`SumoSharp.Viewer.Raylib` then depends on `Viewer.Motion` + `Replication.Dds` and holds the
raylib/ImGui rendering + the DDS subscriber adapter; the `Sim.Viewer` exe becomes a thin
`Program.cs` over it.

---

## 6. Mapping every current project to its packaging fate

| Project | Kind | Fate |
|---|---|---|
| `Sim.Ingest` | lib (net8;ns2.1) | **`SumoSharp.Ingest`** (shipped) |
| `Sim.Core` | lib (net8;ns2.1) | **`SumoSharp.Core`** (shipped) |
| `Sim.Replication` | lib (net8;ns2.1) | **`SumoSharp.Replication`** (shipped) |
| `Sim.Replication.Dds` | lib (net8) ⚠native | **`SumoSharp.Replication.Dds`** (shipped) |
| `Sim.Viewer.Core` | lib (net8) | **split**: portable reconstruction → new `SumoSharp.Viewer.Motion`; DDS/host wiring → `Viewer.Raylib` internal |
| `Sim.Viewer` | exe ⚠native | reusable parts → **`SumoSharp.Viewer.Raylib`**; exe stays a thin **sample** |
| `Sim.Harness` | lib (net8) | **`SumoSharp.Testing`** (opt-in dev-time package) |
| `Sim.Evac` | lib (net8) | **`SumoSharp.Evac`** (opt-in package) |
| `Sim.EvacProfile` | exe | sample (unchanged) |
| `Sim.Viz` | exe | sample (reusable `PayloadBuilder`/`SceneGen` could later become `SumoSharp.Viz`; not now) |
| `Sim.ExtDemo` | exe | sample (unchanged) |
| `Sim.Run`, `Sim.Bench`, `Sim.BenchCity` | exe | samples/benches (unchanged) |
| `Sim.LiveHost` | web app | sample (unchanged) |
| (none yet) | — | **`SumoSharp.Tools`** (design-only in `SUMOSHARP-API.md §2`; implement last) |
| (none — metadata only) | — | **`SumoSharp` meta-package** |

---

## 7. Parity & packaging guard (success bar)

- **Offline parity unchanged.** `dotnet test` must stay green and native-free after every packaging
  step (baseline at time of writing: **440 passed, 0 failed, 3 skipped**; determinism anchor per
  `Sim.Bench` unchanged).
- **Extend the packaging guard test.** `RungB13PackagingTargetsTests` already pins that Core/Ingest
  multi-target net8+ns2.1 and keep the polyfills. Add assertions that:
  - `Sim.Replication` and the new `Sim.Viewer.Motion` also `<TargetFrameworks>` net8+ns2.1 and are
    `IsPackable=true` with the expected `PackageId`;
  - `Viewer.Motion` has **no** `ProjectReference` to `Sim.Replication.Dds` (no native leak into the
    portable motion package) and no `Raylib`/`rlImgui` `PackageReference`;
  - `Replication.Dds` and `Viewer.Raylib` remain the *only* packable projects carrying native deps.
  This is a hermetic, source-reading test (no build, no SUMO, no network) — same style as B13.

---

## 8. Licensing & disclaimer (per package, unchanged policy)

- Dual license **`EPL-2.0 OR GPL-2.0-or-later`** (set in `Directory.Build.props`) — cannot be
  relicensed; SumoSharp is a derivative of SUMO. State the practical read (EPL-2.0 = weak, file-level
  copyleft; a proprietary game may link and keep its own source closed, but must keep SUMO-derived
  files under EPL and publish changes to *those* files) high in each README. Get counsel for
  commercial use — this is not legal advice.
- Each `PackageReadmeFile` carries the disclaimer: *"Unofficial, independent C# reimplementation of
  Eclipse SUMO's microscopic simulation core. Not affiliated with or endorsed by the Eclipse SUMO
  project."* "SUMO" is an Eclipse trademark.

---

## 9. Rollout order (see `SUMOSHARP-PACKAGING-TASKS.md` for tasks + success conditions)

1. **P0 — Reconcile docs with reality.** Update `SUMOSHARP-API.md §1` to point here; record the
   two-package (not bundled) reality and the retired `Runtime` idea. *(This doc + a pointer edit.)*
2. **P1 — `SumoSharp.Viewer.Motion`.** Decouple `DrClock` from `DdsSubscriber`; move the portable
   reconstruction into a new packable, ns2.1-clean project; wire `Viewer.Raylib`/exe to adapt onto
   it. Ship the DR/smoothing guide as its README. *(The load-bearing refactor, §5.)*
3. **P2 — `SumoSharp.Viewer.Raylib`.** Make the reusable raylib/ImGui component packable; leave the
   exe a thin sample.
4. **P3 — `SumoSharp.Testing` + `SumoSharp.Evac`.** Flip `Sim.Harness`/`Sim.Evac` to packable with
   IDs + READMEs.
5. **P4 — `SumoSharp` meta-package + `SumoSharp.Tools`.** Convenience bundle; then the SUMO-binary
   fetch tool (or global tool) per `SUMOSHARP-API.md §2`.
6. **Every step:** extend the B13-style guard, re-run the offline gate, publish CI packs the new IDs.
