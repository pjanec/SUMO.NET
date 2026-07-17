# HIGH-DENSITY-P2G3-DESIGN.md — scenario-46 speedGain residual: diagnosis + deferred deep fix

**Status:** DIAGNOSIS COMPLETE — the originally-proposed fix (best-lanes continuation distance) was
implemented and INSTRUMENTED to be necessary-but-INSUFFICIENT; the binding gap is deeper (cross-junction
leader anticipation in the speedGain incentive). Reverted, not landed. Item **P2G-3** (from
`HIGH-DENSITY-P2G-DESIGN.md` §7). Grounds in `sumo/src/microsim/lcmodels/MSLCM_LC2013.cpp` +
`sumo/src/microsim/MSVehicle.cpp`. See §5.1 for the instrumented finding and the true remaining gap.

## 1. WHAT (the gap) — scenario 46's residual, correctly diagnosed

After the P2-G keep-right leader veto, `scenarios/46-reroute-multilane` still diverges from SUMO by
~7 m. The diagnostic (SUMO `--lanechange-output`) shows the residual is **NOT** cooperative LC (zero
cooperative / zero strategic changes occur in the whole scenario): it is a **`speedGain` overtake the
engine fails to make**. On the 2-lane detour, at the K junction the two turn lanes have different
internal geometry (lane 0's turn is slower), so a leader on lane 0 decelerates to ~6.2 m/s; SUMO's
follower does a `speedGain` change to the faster lane 1 to avoid the slowdown. The engine never does.

**Root cause (confirmed three ways — SUMO reason string, engine code, gate arithmetic):** the engine's
speedGain and keep-right decisions compute the target-lane distance as the **single lane's own remaining
length**, not the **multi-edge best-lanes continuation**. At t=91 vehicle `f.1` is at pos 584.53 on a
603.25 m edge, so the engine's `neighDist = 603.25 - 584.53 = 18.72 m`, and the fire gate
`neighDist / max(.1, speed) > 20` evaluates `18.72 / 13.55 = 1.38 < 20` → the change is suppressed.

## 2. SUMO reference

`MSLCM_LC2013::_wantsChange` (`MSLCM_LC2013.cpp:1112-1136`) sets `currentDist = curr.length` and
`neighDist = neigh.length`, where `curr`/`neigh` are the vehicle's best-lanes `LaneQ` entries. The
speedGain fire gate (`:1857-1859`) is `mySpeedGainProbability > threshold && relativeGain > EPS &&
neighDist / MAX2(.1, speed) > 20`. `LaneQ.length` (`MSVehicle.cpp:5911` base, accumulated backward at
`:6040` `j.length += bestConnectedNext->length`) is **the on-route continuation distance from the lane
start** — "the distance the vehicle can go on its route without changing lanes from that lane"
(`:1109-1110`). For `f.1`'s route `e_src → e_det1 → e_det2 → e_dest`, the continuation of `e_det1_1` is
~1703 m, so SUMO's gate is `1703 / 13.55 ≈ 126 > 20` → the speedGain fires.

## 3. HOW (the fix)

Replace the single-lane distance with the best-lanes continuation length in BOTH decisions, using the
engine's existing memoized `BestLanesCached(routeId, route.Edges, edgeId)` (the same `ComputeBestLanes`
the strategic / `KeepRightStrategicStay` paths already call), preserving each path's EXISTING position
handling so single-edge routes stay byte-identical.

### 3.1 speedGain (`DecideSpeedGainForVehicle`)
Currently:
```
var currentDist = lane.Length - v.Kinematics.Pos;
var neighDist  = leftLane.Length - v.Kinematics.Pos;
```
Becomes (continuation length for the respective lane index, still minus position — the path's existing
convention):
```
var bestLanes  = BestLanesCached(EffectiveRouteId(v), route.Edges, lane.EdgeId);
var currentDist = ContinuationLength(bestLanes, lane.Index)     - v.Kinematics.Pos;
var neighDist   = ContinuationLength(bestLanes, leftLane.Index) - v.Kinematics.Pos;
```
`ContinuationLength` finds the `LaneContinuation` with matching `LaneIndex` and returns its `.Length`
(falling back to the lane's own `Length` if, defensively, no entry matches). These feed both
`AnticipateFollowSpeed` (`thisLaneVSafe`/`neighLaneVSafe`) and the `>20` gate, exactly as today.

### 3.2 keep-right (`ApplyKeepRightDecision`)
Currently `var neighDist = rightLane.Length;` (no position term — its existing convention, matching
SUMO's `neigh.length`). Becomes the right lane's continuation length:
```
var neighDist = ContinuationLength(BestLanesCached(routeId, route.Edges, lane.EdgeId), rightLane.Index);
```
feeding the existing `fullSpeedGap`/`fullSpeedDrivingSeconds` incentive math unchanged. (The
`KeepRightStrategicStay` suppressor already uses the continuation length; this aligns the incentive
distance with it.)

## 4. Determinism / parity argument (byte-identical for single-edge; ≤ SUMO for multi-edge)

- **Single-edge routes** (every vehicle whose current edge is the last route edge): `ComputeBestLanes`'
  base case sets each lane's continuation `Length` = its own lane length, so `ContinuationLength(...) ==
  laneLength` and both formulas reduce EXACTLY to the current code. Every committed single-edge scenario
  is therefore byte-identical.
- **Multi-edge routes**: the continuation length is longer, so `neighDist` grows. Since SUMO's own
  `neigh.length` is longer still (it does not subtract position), the engine's gate can only become
  *closer to* SUMO's, never fire a change SUMO would not — the change is monotone toward SUMO. The full
  `dotnet test` suite is the gate: every prior golden must stay green/byte-identical.
- Reads only the immutable route/network via the memoized best-lanes cache + the frozen post-move
  snapshot; writes only ego's own fields. No new cross-vehicle coupling; the region-parallel plan phase
  stays byte-identical to serial. `BestLanesCached` is a `ConcurrentDictionary` memo (pure function of
  immutable route+network), so the added per-vehicle lookup is cache-hot and thread-safe.

## 5.1 INSTRUMENTED FINDING — neighDist was necessary but NOT the binding constraint

The §3 fix was implemented (speedGain + keep-right continuation distance) and instrumented on
scenario 46. Results:

- **The continuation distance works as designed:** for `f.1` at the K junction, `neighDist` went from
  ~18.7 m (single-lane) to **~1145 m** (continuation), so the `>20 s` gate now passes trivially
  (82.5 ≫ 20). But `f.1` **still does not fire the speedGain**, and the scenario-46 trajectory is
  **byte-for-byte unchanged**.
- **Why:** the speedGain accumulator `mySpeedGainProbability` never builds, because the engine's
  `thisLaneVSafe == neighLaneVSafe == 13.89` (so `relativeGain ≈ 0`, accumulator decays). The engine's
  `f.1` **does not see the slow leader `f.0`** in the speedGain incentive: `thisLaneVSafe` uses a
  same-lane leader lookup (`postMoveNeighbors.GetLeader`), which loses `f.0` the moment `f.0` crosses
  onto the junction internal lane (`:K_0_0`). SUMO's `thisLaneVSafe` (`getRawSpeed`) anticipates the
  leader **across the best-lanes continuation** (through the junction), sees `f.0` at 6.18 m/s, and
  accumulates → fires at t=91. (The engine's *car-following* phase does brake `f.1` for `f.0` across
  the junction — via the junction-link leader path — so `f.1` still slows; it is only the *lane-change
  incentive's* `thisLaneVSafe` that is blind to the cross-junction leader.)
- **The keep-right half of the §3 fix separately regressed** the saturated `-L2` diagnostic
  (`willpass-saturation`, 0 → 90 stuck), because a longer keep-right incentive distance over-fires
  keep-rights in dense multi-edge traffic — the same cooperative-LC coupling P2-G hit. So even the
  keep-right continuation is not landable alone.

**Conclusion:** scenario 46's residual is a **cross-junction leader-anticipation** gap in the speedGain
incentive's `thisLaneVSafe`/`neighLaneVSafe` — SUMO's `getRawSpeed`/best-lanes-continuation leader
look-ahead (`MSLCM_LC2013::_wantsChange`'s `getSlowest`/anticipated-speed path). The engine's LC
decision reads only same-edge leaders. Closing it means giving the LC incentive a continuation-aware
leader lookup (look across the vehicle's best-lanes continuation for the constraining leader), a
genuine LC-model extension comparable in depth to cooperative LC — NOT the small distance tweak first
scoped. **Deferred pending an explicit owner decision** (§7). The neighDist continuation change was
reverted (byte-identical everywhere → no distinguishing anchor → not landable on its own).

## 5. Success conditions (acceptance gate) — NOT MET by the neighDist fix (see 5.1); retained for the eventual deep fix

1. **scenario 46 residual closes:** with the fix, the engine performs the `speedGain` change moving
   `f.1` onto `e_det1_1` at t=91 (matching SUMO), and the scenario-46 engine-vs-golden max position
   error drops from ~7.09 m toward ~0. Re-measure and report.
2. **New bit-exact anchor** `scenarios/51-multilane-speedgain-continuation`: a minimal 2-lane →
   (junction) → 2-lane route where a follower must `speedGain` past a slow leader across the edge
   boundary — impossible under the single-lane distance (gate fails), correct under the continuation
   distance. `sigma=0`, SUMO 1.20.0 golden (`--precision 6`, `--fcd-output.acceleration`), tolerance
   exact `lane`/`pos`/`speed` @1e-3. The engine matches @1e-3 only with the fix; the test asserts the
   follower reaches the faster lane like SUMO.
3. **No regression:** full suite stays green and byte-identical for every prior golden (561 → 561 + new
   anchor tests). `Sim.Bench` determinism hash unchanged.
4. **If scenario 46 still shows a material residual after the fix** (e.g. the keepRight-back on e_det2
   is also involved), report it and localise the next facet rather than chasing it.

## 6. Explicitly deferred (unchanged)

- Cooperative LC (LCA_COOPERATIVE / `informFollower`) — a separate, heavier gap (P2G-2), not scenario
  46's residual; pursue only if a scenario proves it binding (the saturated-grid follower-block case).
- SUMO's terminal-edge nonzero best-lane offsets for unequal-length terminal lanes (`ComputeBestLanes`
  base-case simplification) — no committed anchor needs it.
