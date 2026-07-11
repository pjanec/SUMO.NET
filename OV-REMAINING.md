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
- **OV4** — cooperative oncoming shift (the requested enhancement): an oncoming driver that sees a
  spilled overtaker closing head-on down its bidi lane within `CooperativeShiftReactionDist` (200 m)
  pulls to its OWN outer lane edge (`DetectCooperativeShift` → `VehicleRuntime.CooperativeShift`,
  exported; `ComputeLateralEvasion` drifts it to `-(laneHalfWidth - egoHalfWidth)`), then recentres.
  The exact ER3-detection + ER5-drift pattern, reading only the overtaker's already-committed
  LatOffset from the frozen snapshot (parallel-safe). Gated on `_anyLcOpposite` (inert everywhere
  else; bench hash unchanged). `scenarios/57-overtake-opposite/ov4-cooperative.rou.xml` +
  `RungOV4CooperativeShiftTests`. NOTE: because OV2's gap acceptance already guarantees the pass
  finishes before the head-on arrives, the cooperative shift is defence-in-depth margin during the
  approach window (the oncoming widens the corridor while the overtaker is spilled) — it is never
  what prevents a collision, and the two vehicles never reach a true side-by-side pass while spilled
  (the same conservatism that made D1 vacuous). A genuine side-by-side pass would require OV2 to
  commit optimistically on the strength of the oncoming's cooperation — a coupled decision left for a
  future rung (see D1/D3).

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

### D2 — OV3 RETURN-GAP enforcement — DONE
Past ~t=21 in `ov3b-adversarial`, once the oncoming cleared, ego re-committed and overtook the
now-fast leader a second time; its RETURN cut back in only ~3.6 m ahead of the 11 m/s leader (a body
overlap during the recenter). Cause: the return was triggered implicitly by "no longer held up"
(`DetectOvertake` returns false once `ego.Pos > leader.Pos`), i.e. it recentered the instant it
nudged ahead, without enforcing a safe re-entry gap. FIX (landed): `VehicleRuntime`
`OvertakePassedLeaderIndex` remembers the leader being passed; when the held-up decision drops but ego
has nosed AHEAD of that leader, `OvertakeReturnGapSafe` keeps `OvertakeActive` true (stay spilled)
until the re-entry gap is safe (`IsTargetLaneSafe`'s neighFollow secure-gap). An ABORT (ego still
BEHIND the leader) returns true there, so the OV3b abort-mid-spill behaviour is preserved. Inert for
non-`lcOpposite`. `RungD2ReturnGapTests` runs the FULL `ov3b-adversarial` run: no pair overlaps, and
ego recenters only with a safe gap (it now recenters ~15-20 m ahead, at t=27, not 3.6 m).

### D3 — coupled OV2/OV4 decision (a genuine side-by-side pass): NOT started
OV4 (above) widens the corridor but OV2 decides independently and conservatively, so the overtaker
never actually passes side-by-side with a spilled body while the oncoming is alongside — OV2 has
already completed or dropped the pass by then. To let the cooperation ENABLE a tighter overtake
(SUMO's opposite-overtake does let vehicles pass with reduced lateral clearance), OV2's gap
acceptance would need to account for the oncoming's shift — e.g. reduce `requiredClear` when the
oncoming is known/expected to cooperate, or model the reduced-clearance side-by-side envelope. This
couples two per-ego decisions across vehicles (an overtaker betting on an oncoming's future
cooperation), which needs care under the plan/execute contract and a fixture that forces a real
side-by-side pass. Do this together with re-adding the D1 cross-lane brake (which WOULD then bind).
