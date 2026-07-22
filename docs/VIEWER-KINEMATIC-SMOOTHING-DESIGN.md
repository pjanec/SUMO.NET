# Viewer kinematic smoothing — adopt the IgBridge reconstruction in the DR viewers (DESIGN / the HOW)

**WHAT (the ask).** Both DR viewers — the 3D **City3D** (Godot) IG and the 2D **Raylib** native viewer —
currently reconstruct a per-render-frame pose from the DDS/replication DR stream and smooth it with the
older `DrPoseSmoother` (§10.2 capped-correction + §10.3 heading-tilt of `SUMOSHARP-VIEWER-DR-SMOOTHING.md`).
`DrPoseSmoother` is not good enough. Replace it with the **KinematicHeading** reconstruction we developed and
tuned in IgBridge (no-slip rear-axle drag + low-passed lane-heading prediction + spatial look-ahead +
anticipation lead-bound + coarse-feed junction-straddle handling + lane-change ease), so every viewer render
frame produces the current `(x, y, z, orientation)` per entity with that quality.

**This doc is the HOW.** It does not restate: the DR pipeline (`SUMOSHARP-VIEWER-DR-SMOOTHING.md` §5/§8), the
IgBridge reconstruction internals or its tuning story (`IGBRIDGE-DECISIONS.md` §5.x, `IGBRIDGE-METHODOLOGY.md`),
or the replication contract (`Sim.Replication`). Tasks + success conditions:
`VIEWER-KINEMATIC-SMOOTHING-TASKS.md`; tracker: `VIEWER-KINEMATIC-SMOOTHING-TRACKER.md`.

---

## 0. The load-bearing facts (from the code investigation)

- **Same seam in both viewers.** Each viewer's per-frame loop is `source.Pump() → DrClock.Pump →
  DrClock.Resolve → PoseResolver.Resolve(ChordHeading) → smoother.Smooth(...)`. The final smoother call is the
  single swap point:
  - 3D: `demos/City3D/CityLib/Reconstructor.cs:118` — `_smoother.Smooth(handle, pose.X, pose.Y, pose.HeadingDeg, state.Speed, frameDt)`.
  - 2D: `src/Sim.Viewer.Raylib/RenderHelpers.cs:110` — `smoother.Smooth(handle, px, py, pdeg, resolved.State.Speed, frameDt)`.
- **`KinematicHeading` already lives in the shared `Sim.Viewer.Motion`** and is currently used only by IgBridge
  (`IgBridgeSession.cs:304`). The viewers reference the same project. So this is a *wiring* change, not new math.
- **Everything the algorithm needs is already in scope at the seam:** the resolved `DrState`, `resolved.Upcoming`
  (K≤4 upcoming lane handles), the `ILaneShapeSource`, `speed`, per-vehicle `Length/Width`, `frameDt`, and
  `resolved.IsLateralStraddle`. The spatial look-ahead (`TryLookAheadHeading`) needs exactly `Upcoming` + the
  lane source — both present.
- **`PoseResolver` returns the FRONT reference** (`PoseResolver.cs:16`), whereas `KinematicHeading` returns the
  vehicle **CENTER** (½·length behind the front). City3D's `CarTransform` places the box **center** at the
  smoothed point (`CarTransform.cs:21,50`), which is currently the *front* → the box sits ~½·length too far
  forward today. Feeding it `KinematicHeading`'s center **fixes** that latent offset. (Confirm the Raylib box
  pivot in T1; same fix applies.)
- **Lane-change straddle differs between the viewers:** Raylib already resolves both bracketing poses and
  Cartesian-lerps a continuous front through a lane change (`RenderHelpers.cs:57-96`); **City3D skips straddles**
  (`Reconstructor.cs:90-96` `continue`) so it has **no lane-change smoothing at all**. KinematicHeading's
  lane-change ease needs a *continuous* front input, so City3D must gain the straddle-lerp.
- **The viewers are coarse-feed consumers.** DR packets are DR-error-gated and sparse (~1–3 Hz, `§2.2`),
  reconstructed to a continuous front at render rate by `DrClock`. So the **coarse-feed robustness we built for
  IgBridge (junction-straddle discriminator, look-ahead lead-bound, retire/lookahead-horizon fix) is directly
  load-bearing here** — the viewers should run with `CoarseFeed = true`.

---

## 1. Approach — one shared reconstruction entry, called by all three consumers (DRY)

Rather than duplicate the "straddle-lerp → front resolve → look-ahead → KinematicHeading" logic (which today
lives inline in `IgBridgeSession`) into each viewer, **extract it once** into `Sim.Viewer.Motion` as a small
reconstruction facade, and have IgBridge, City3D, and Raylib all call it. This is the true *fix-once-fixes-both*
and it retires `DrPoseSmoother` from the vehicle path.

### 1.1 New shared component: `Sim.Viewer.Motion.KinematicReconstructor`
A per-consumer instance (holds the per-vehicle `KinematicHeading` state + the look-ahead jump-guard history).
One method, fed the output of `DrClock.Resolve` plus geometry:

```
readonly struct KinematicReconResult { double CenterX, CenterY, CenterZ; float HeadingDeg; }

KinematicReconResult Resolve(
    VehicleHandle handle,
    in DrResolved resolved,           // DrClock.Resolve output: State, SecondState?, Blend, Upcoming, SecondUpcoming, IsLateralStraddle
    ILaneShapeSource lanes,
    (double Length, double Width) dims,
    float frameDt)
```

Internally (verbatim-extracted from `IgBridgeSession` so behavior is preserved byte-for-byte for IgBridge):
1. **Front resolve, straddle-aware.** Non-straddle → `PoseResolver.Resolve(...ChordHeading)`. Straddle →
   resolve both bracketing states and Cartesian-lerp the front (`IgBridgeSession.cs:206-219`), yielding a
   continuous front `(fx, fy, fz)`, `laneHeading`, `speed`, and `lateralEvent`.
2. **Coarse-feed junction-straddle discriminator** (`CoarseFeed` + `MaxStraddleLaneChangeHeadingDeg`): a
   straddle whose two lane poses diverge > threshold is a junction turn, not a lane change → not absorbed.
3. **Spatial look-ahead** (`TryLookAheadHeading`, moved here from `IgBridgeSession`): advance `Pos` by the
   effective look-ahead (`max(LookAheadMeters, LookAheadLengthFactor·length)`), re-resolve down `Upcoming`,
   apply the frame-to-frame jump-guard + the anticipation lead-bound (`MaxAnticipationLeadDeg`).
4. **`KinematicHeading.Update(handle, fx, fy, laneHeading, speed, length, frameDt, lateralEvent, predictHeading)`**
   → center + heading.
5. **Z**: sample `lanes.LaneShapeZ` / carry `PoseResolver` `Pose.Z` at the smoothed center (viewers keep their
   existing pitch-from-z-gradient; the reconstructor only supplies center-z, or the viewer samples it — T2).

Config carried on the reconstructor = the tuned v5 defaults (`LookAheadMeters=3`, `LookAheadLengthFactor=0.5`,
`MaxAnticipationLeadDeg=70`, `PositionSmoothTime=0.60`, `LanePredictSmoothTime=0.18`, `LaneChangeDecayTau=2.0`,
`CoarseFeed=true`, `MaxStraddleLaneChangeHeadingDeg=20`). Exposed for tuning; defaults match IgBridge v5.

### 1.2 `DrPoseSmoother` disposition
Keep the class but the **vehicle render path no longer calls it**. Retain it behind a viewer toggle
(`--smoother=kinematic|legacy`, default `kinematic`) for one release so an in-app A/B is possible (owner's
eyes), then remove in a follow-up. (Owner: "either replace, or revamp." This is *replace with a legacy
fallback*, which also gives the A/B the owner asked for in Q2.)

### 1.3 Why a shared facade and not just "call KinematicHeading at the seam"
The look-ahead + straddle-lerp are ~60 lines that must be *identical* across producer and consumers or the IG
trace and the on-screen viewer will drift apart. Extraction is the only way to keep them one implementation
(the "fix once" tenet), and it lets IgBridge's committed metric regression continue to guard the shared code.

---

## 2. Per-viewer integration

### 2.1 City3D (`demos/City3D/CityLib/Reconstructor.cs`)
- **Stop skipping straddles** (`:90-96`): route them through the facade (which does the Cartesian-lerp).
- Replace `_smoother.Smooth(...)` (`:118`) with `_recon.Resolve(handle, resolved, lanes, dims, frameDt)`.
- Feed `CarTransform` the **center** (`result.CenterX/Y`) — fixes the ½·length forward offset. Keep the
  existing `CoordinateTransform.SumoToGodot`, z, and `ComputePitchRad`.
- `CityLib` consumes `Sim.Viewer.Motion` via the `SumoSharp.Viewer.Motion` **nupkg** (packaged). The facade
  ships in that package (netstandard2.1-compatible — it already is), so City3D gets it through a normal package
  bump; no ProjectReference into `src/`.

### 2.2 Raylib 2D (`src/Sim.Viewer.Raylib/RenderHelpers.cs`)
- Replace both the straddle branch (`:57-96`) and the normal `smoother.Smooth(...)` (`:110`) with a single
  `_recon.Resolve(...)` call (the facade already handles straddle vs normal).
- Feed the renderer the **center** (confirm the box pivot in T1; apply the same front→center fix if needed).
- Applies to `--mode loopback` and `--mode remote` (both DR). `--mode local` (authoritative snapshot,
  `BuildLocalVehicleDraws`) is **out of scope** — it interpolates real x,y and never uses this path.

### 2.3 IgBridge (`src/Sim.IgBridge/IgBridgeSession.cs`)
- Refactor `EmitVehicles` to call the shared facade instead of its inline straddle/look-ahead/kinematic block.
  **Success condition: the emitted trace stays byte-identical to v5** (verbatim extraction, verified by the
  determinism/byte-diff gate). This is what proves the extraction is behavior-preserving.

---

## 3. Consumer-side considerations (new vs the IgBridge producer)

- **Render cadence vs emit cadence.** IgBridge runs the kinematics at a fixed 20 Hz emit dt; the viewers run at
  the **display frame rate** (~60 Hz, variable). `KinematicHeading`'s gains are all `1−e^(−dt/τ)` (tau-based),
  so they adapt to any `frameDt` — no retune expected. The look-ahead jump-guard already uses `realDt`.
  **Risk:** a very long frame (stall) → large `dt`; `KinematicHeading` already clamps via the reseed jump. T3
  validates smoothness across 30/60/144 Hz and a stutter injection.
- **Underlying feed is sparse but the front is dense.** `DrClock.Resolve` interpolates/extrapolates the arc, so
  the facade sees a *smooth* front at frame rate (the favorable, high-effective-feed case). The `CoarseFeed`
  discriminator keys off the **straddle** heading divergence, which is large exactly when a junction is crossed
  within one sparse packet gap — the case it was built for. So `CoarseFeed=true` is correct for the viewers.
- **Extrapolation past the newest packet.** With a small playout delay (City3D 0.4 s) a fast decel can push
  `DrClock` into its extrapolate branch; `KinematicHeading` receives that extrapolated front. Its position
  tracker + reseed guard absorb the reconciliation when the next packet lands (same as IgBridge's coarse-feed
  path). The DR-error publishing already bounds this at the source. T3 checks a braking-at-red case.
- **Stopped vehicles.** `KinematicHeading`'s `HoldSpeed` (0.5 m/s) holds heading and eases onto the lane point
  at a stop — replaces `DrPoseSmoother`'s behavior; verify no creep at a red light (a known past bug).

---

## 4. Determinism, parity, and what must not change

- **Parity is untouched.** All of this is render-side (`Sim.Viewer.Motion`, the viewers, CityLib). `Sim.Core`
  is not edited. `dotnet test Traffic.sln` (654/4) + the `Sim.Bench` determinism hash stay at baseline —
  captured fresh at the start of each `src/`-touching task (other sessions may be live).
- **IgBridge v5 output byte-identical** after the extraction (T-extract gate).
- **Determinism of the viewers' reconstruction**: no `System.Random`, per-entity state, tau/`realDt`-based —
  reproducible given the same packet stream + frame schedule. Headless self-tests assert this.

---

## 5. Verification (headless is the gate; owner's eyes are the sign-off)

Provable on this VM (no GPU) — the acceptance gate:
1. **`CityLib.Tests`** (xUnit, no Godot): reconstructed poses over a scripted packet stream are smooth
   (bounded per-frame yaw-accel), no back-jumps, a lane change eases over ~1.2–1.5 s, a junction turn follows
   the connecting-lane arc (offset-from-centerline below threshold), a stopped vehicle does not creep. Center
   output sits ½·length behind the front (the pivot fix).
2. **DR trace harness** (`--trace-veh`, `SUMOSHARP-VIEWER-DR-SMOOTHING.md §7`) on the Raylib loopback:
   AUTHTRACE vs reconstructed, lateral/longitudinal decomposition — max lateral per-frame deviation and the
   IgBridge metric suite (yaw-accel reversals median 0, no-slip, offset-from-centerline) at/again better than
   the `DrPoseSmoother` baseline captured first.
3. **IgBridge regression**: emitted trace byte-identical to v5; the committed IgBridge metric check still green.
4. **Godot headless smoke** (`godot --headless`, `fetch-godot.sh`): scene builds, N frames, no exceptions;
   optional Xvfb software screenshot.
5. **Standing gates**: `dotnet test` 654/4, `Sim.Bench` hash unchanged, determinism byte-diff.

Owner (desktop): the actual Godot 3D and Raylib 2D motion looks right (the only thing not provable headless).

---

## 6. Staging (details + success conditions in the TASKS doc)
- **S0 — baseline capture + facade extraction.** Extract `KinematicReconstructor` from `IgBridgeSession`
  verbatim; IgBridge byte-identical. Capture the `DrPoseSmoother` metric baseline for both viewers.
- **S1 — Raylib swap** (in `src/`, has the trace harness). Straddle already present → smallest first step;
  metrics beat baseline.
- **S2 — City3D swap** (add straddle-lerp via the facade; center-pivot fix; package bump). Headless CityLib
  tests + Godot smoke.
- **S3 — tune + toggle + prove.** `--smoother` A/B toggle, render-rate sweep, lock defaults, commit the metric
  regression, docs. Owner desktop sign-off.

---

## 7. Open questions for the owner
1. **`DrPoseSmoother` fate:** keep it as a `--smoother=legacy` toggle for one release (my default proposal, for
   your A/B), or delete it outright now?
2. **2D `--mode local`:** leave the authoritative-snapshot path on its own interpolation (my proposal — it has
   exact x,y and no DR), or also route it through the kinematics for uniformity?
3. **Package flow for City3D:** OK to deliver the City3D change as a `SumoSharp.Viewer.Motion` **package bump**
   (the demo consumes packages, not `src/`), i.e. S2 depends on a local `dotnet pack`? (No `src/` behavior
   changes for the offline gate; the demo is out of `Traffic.sln`.)
