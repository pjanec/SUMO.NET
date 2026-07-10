# OV-REMAINING.md — opposite-direction overtaking: landed + deferred

The opposite-direction overtaking arc (OV1–OV3) is landed on `main` and green. This records what
is done, what was deliberately deferred (with the diagnosis), and the design for the rest — so a
follow-up session can resume without re-deriving it. Same discipline as `C4-VII-REMAINING.md`.

## Landed (on `main`, parity-reviewer ACCEPT each)

- **OV1** — detection: an `lcOpposite` vehicle held up behind a slower same-lane leader forms an
  overtake intent (`VehicleRuntime.OvertakeActive`, exported) when the oncoming (bidi) lane is clear
  ahead. `scenarios/57-overtake-opposite` (hand-written two-way `bidi` road net).
- **OV2** — gap acceptance: replaced the fixed clear-ahead with a closing-speed / time-to-complete
  formula (`requiredClear = (egoFreeSpeed + oncomingSpeed)·overtakeTime + safety`), refusing when the
  head-on closes before the pass could finish. Isolated by identical-geometry / different-speed
  fixtures.
- **OV3** — execution: while `OvertakeActive`, ego spills laterally toward the oncoming lane; the
  ER5 `!FootprintsOverlap` same-lane leader bypass carries it past the slow leader; when the intent
  drops (passed the leader, or gap acceptance refused) ego recenters. Collision-free in the tested
  cases. `ov3-clear.rou.xml`.
- **OV3b (this note)** — adversarial abort-mid-spill SAFETY TEST only (no engine change):
  `ov3b-adversarial.rou.xml` + `RungOV3bAbortSafetyTests`. The leader accelerates (maxSpeed 11) so
  OV2's gap acceptance commits early then ABORTS while ego is already spilled; the test asserts ego
  recenters collision-free (all pairs, exported world X/Y), through the abort window (~13 steps).

## Deferred, with diagnosis (do these next)

### D1 — cross-lane hard-brake backstop: prototyped, REVERTED (untestable under conservative OV2)
A `OppositeOncomingConstraint` (brake while spilled if a laterally-overlapping oncoming is close) and
a hard-safety intent drop (`LatOffset > 0.8 && nearestAhead < 60 → abort`) were implemented and are
inert-safe, but **never bind** in any constructible fixture: OV2's gap acceptance is conservative
enough that it drops the intent long before an oncoming reaches the hard-abort/brake range (in the
adversarial fixture it aborted with the oncoming still ~238 m away). Adding defense-in-depth code
with no non-vacuous test is speculative, so it was reverted (Engine.cs left byte-identical to OV3).
If a future scenario shows OV2 committing optimistically (e.g. a *dynamic* new oncoming appearing
inside the committed window), re-add the constraint AND a fixture that forces it to bind.

### D2 — OV3 RETURN-GAP enforcement (a real pre-existing OV3 bug the adversarial run surfaced)
Past ~t=13 in `ov3b-adversarial`, once the oncoming clears, ego re-commits and overtakes the
now-fast leader a second time; its RETURN cuts back in only ~4 m ahead of the 11 m/s leader (a body
overlap during the recenter). Cause: the return is triggered implicitly by "no longer held up"
(`GetLeader` returns null once `ego.Pos > leader.Pos`), i.e. it recenters the instant it nudges
ahead, without enforcing a safe re-entry gap. Fix sketch: keep `OvertakeActive` true (stay spilled)
until ego is a safe following-gap AHEAD of the just-passed leader — which needs tracking the passed
leader for a step or two after `GetLeader` stops returning it (small per-ego state, e.g. an
`OvertakePassedLeaderPos` remembered until the gap is safe). Then extend the OV3 no-collision test to
the full run (not just the abort window).

### OV4 — cooperative oncoming shift (the requested enhancement; not started)
Mirror of give-way ER3/ER5: an oncoming vehicle detects a spilled overtaker encroaching into its lane
(a bidi-lane vehicle with `OvertakeActive` / large `LatOffset` approaching head-on within range) and
drifts to its OWN outer edge (reuse `DriftToward` + a `GiveWayEdgeTarget`-style target) to widen the
corridor, recentering after it passes. Reads only the frozen snapshot, writes only the ego's
`LatOffset` via `MoveIntent` — the exact ER3-detection + ER5-drift pattern. Behavioral property test:
the oncoming shifts, both pass, no footprint overlap, both recenter. Gate on `_anyLcOpposite` for
inertness.
