# SumoSharp.Viewer.Motion

Portable, engine-agnostic **render-side motion reconstruction** for a SumoSharp-driven simulation.
Targets `net8.0` and `netstandard2.1` — no renderer, no wire transport, no native dependency, so
Unity (Mono/IL2CPP), Godot, and any custom 3D engine can consume it directly.

**What it solves.** The simulation engine advances in discrete steps (as slow as 1 Hz in phase-1
parity mode), far below a display's 60 Hz, and a decoupled renderer typically only sees *sparse*,
adaptively-published samples (lane + arc-position + speed/accel, no `x,y`, no heading). Naively
drawing "the latest sample" teleports vehicles. This package turns that sparse stream into a smooth,
continuous per-frame pose `(x, y, heading)`.

This package is the shipped implementation of
[`SUMOSHARP-VIEWER-DR-SMOOTHING.md`](../../docs/SUMOSHARP-VIEWER-DR-SMOOTHING.md) — read it for the
full design rationale, the bugs each mechanism fixes, and the tunables table (§9). This README is the
condensed reimplementation guide (its §8 and §10).

## What's in the box

- **`DrClock`** — the render clock + sample resolver.
  - `Pump(newestSampleTime, hold)`: advances a strictly-monotonic `renderSim` clock from a
    long-baseline wall\<-\>sim rate fit, immune to per-packet jitter. Never steps backward; holds
    instead. `hold: true` (paused) freezes it. Restart detection re-anchors the fit.
  - `Resolve(history, delay, laneSource)`: given a vehicle's buffered per-vehicle history (newest
    last) and a playout `delay`, returns a `DrState` at render time — extrapolating forward past the
    newest sample, extrapolating backward past the oldest, or interpolating between the two
    bracketing samples. A same-lane bracket is a plain arc-length lerp; a lane-crossing bracket uses
    an **arc-window walk** (`ArcInWindow`) so a turn follows the real (possibly curved) internal-lane
    geometry instead of snapping; a lane-change (sideways) straddle returns *both* bracketing states
    for the caller to resolve independently and Cartesian-lerp.
  - Extrapolation delegates to `Sim.Replication.DrExtrapolation.Arc` — the SAME curve the publisher
    uses for its DR-error publish decision, so viewer prediction and publish-side prediction never
    diverge (see "Reuse DR-error-based publishing" below).
- **`DrPoseSmoother`** — a small per-vehicle, per-frame pose smoother you run *after*
  `DrClock.Resolve` + `PoseResolver.Resolve` have produced a target `(x, y, heading)` for the frame.
  `Smooth(handle, targetX, targetY, targetDeg, speed, frameDt)` returns the actual pose to draw this
  frame, and remembers it for the next call. The first observation of a handle returns the target
  unchanged.

Not in this package (by design, D2 of the packaging doc): `PoseResolver`, `ILaneShapeSource`, `Pose`,
`DrState` stay in `SumoSharp.Core` — they are dependency-light and shared by the engine's own opt-in
render mode as well as every viewer. `DrClock`/`DrPoseSmoother` consume them; they don't redefine them.

## The reconstruction pipeline

Per render frame, per vehicle:

```
drClock.Pump(newestSampleTime, hold: paused)                 // advance the render clock
resolved = drClock.Resolve(history, delay, laneSource)        // pick/interpolate a DrState
state    = resolved.State with { Length, Width }               // add dims from your registry
pose     = PoseResolver.Resolve(laneSource, state,             // DrState -> (x, y[, z], heading)
               resolved.Upcoming, dt: 0, RenderRealism.ChordHeading)
(x, y, heading) = smoother.Smooth(handle, pose.X, pose.Y, pose.HeadingDeg, state.Speed, frameDt)
emit draw(x, y, heading, length, width, speed)
```

Feed `PoseResolver` `dt = 0` — `Resolve` has already advanced the arc to render time. Use
`RenderRealism.ChordHeading` (correct chord heading, no lateral "swing-wide" bow); the alternative
`CornerCutCorrected` reintroduces a lateral jump on any coarsely-faceted internal-lane polyline unless
you densify the geometry (higher netconvert `internal-link-detail`).

### `DrPoseSmoother.Smooth` internals (as-built, supersedes an earlier plain low-pass)

1. **Capped position error-smoothing** ("netcode projective" style, always on). The rendered position
   chases the DR target with a correction speed that is *capped*, not low-passed:
   - Smooth constant-speed motion passes through with **zero lag**.
   - A reconciliation snap (extrapolation overshoot corrected when a new sample lands) is absorbed
     over a few frames instead of teleporting.
   - **Forward-biased**: a backward correction is a gentle ~50% slowdown (floor `0.5 * trueStep`, cap
     `trueStep + 6 m/s`), never a freeze, never a reverse. Lateral catch-up is capped (~4 m/s) both
     ways. A >7 m gap snaps outright (handle reuse / respawn).
   - Decomposed in the lane-heading frame (`along`/`perp` relative to the target heading), so the cap
     behaves consistently regardless of world orientation.
2. **Motion-derived heading tilt.** Heading = the lane-forward heading, rotated by the tilt of the
   vehicle's *actual* per-frame render displacement: `tilt = atan2(perp, along)`, clamped ±25°, then
   **subtracted** from the lane heading (navi-degrees are clockwise; `atan2` is counter-clockwise —
   adding leans the car the wrong way). This makes the vehicle lean into a lateral slide uniformly in
   both directions, and is ~0 on straight cruise or a turn (motion already follows the lane there).
3. **Heading low-pass.** Eases the tilt-adjusted heading toward the previous frame's heading over
   `τ = 0.18 s` (`α = 1 - exp(-frameDt / τ)`); a jump >100° snaps instead of smearing (handle reuse).

### Playout delay

Use a **stable, manual** delay (recommended default ~1.0 s for a real-time feed, kept under 1 s).
Do **not** auto-drive it from the measured packet interval — since the sample instant is
`renderSim - delay`, a per-frame delay change is a per-frame position teleport, which showed up as
audible/visible speed pulsing. Interpolation (delay > the packet gap) is strictly smoother than
extrapolation (delay = 0); size the delay against your transport's actual publish cadence.

### Reuse DR-error-based publishing — the biggest lever, and it's not in this package

The single most effective fix for reconciliation snaps lives on the **publish** side, not here: have
your publisher run the same `Sim.Replication.DrExtrapolation.Arc` curve this package's `DrClock` uses
for extrapolation, and only emit a new sample when the true state diverges from that prediction beyond
a tolerance (`Sim.Replication.PublishScheduler` / `DrErrorPublishPolicy`). That bounds this package's
own extrapolation error at the source, so motion stays smooth at low delay with no bandwidth increase.
`DrPoseSmoother` then only mops up a small residual. See
`SUMOSHARP-DR-ERROR-PUBLISHING-DESIGN.md` for the full design.

### Pitfalls carried forward from the native viewer's own history

- **Under-sampling at junctions is the norm, not an edge case**, at low step/publish rates — implement
  the arc-window bracket (`DrClock.Resolve` does this for you), or turns will visibly snap.
- **Faceted lane geometry + per-segment tangents** produce a stair-stepping heading; `ChordHeading`
  avoids the artifact without densifying geometry.
- **Never let the render clock step backward** on jitter — `DrClock.Pump` holds instead; don't bypass
  this if you reimplement pacing yourself.
- **Clamp deceleration extrapolation at the predicted stop time** — an unclamped constant-accel
  extrapolation drives a stopped/braking vehicle backward past its stop. `DrExtrapolation.Arc` (in
  `SumoSharp.Replication`) already guards this.

### 3D-specific notes

- **Elevation**: sample `ILaneShapeSource.LaneShapeZ`; `Pose` carries `Z`. Interpolate it with the
  same fraction as the arc-length position.
- **Yaw/pitch/roll**: yaw = the navi-heading (convert to your engine's convention, e.g. negate for a
  counter-clockwise-positive engine); derive pitch from the Z gradient along travel; roll ≈ 0 unless
  you fake banking. Use shortest-arc interpolation (or a quaternion slerp) for yaw — never a raw linear
  lerp, which rotates the long way across a 350°→10° wrap.
- **Wheels / steering / brake lights**: `Speed`, `Accel`, and the frame's `Δheading/Δt` are already
  available from the resolved state; no additional data is needed.

## License & disclaimer

Dual-licensed **EPL-2.0 OR GPL-2.0-or-later** (SumoSharp is a derivative of Eclipse SUMO and cannot be
relicensed). Practical read: EPL-2.0 is a weak, file-level copyleft — a proprietary game may link this
package and keep its own source closed, but must keep SUMO-derived files under EPL and publish changes
to *those* files. This is not legal advice; get counsel for commercial use.

Unofficial, independent C# reimplementation of Eclipse SUMO's microscopic simulation core. Not
affiliated with or endorsed by the Eclipse SUMO project. "SUMO" is an Eclipse trademark.
