# HIGH-DENSITY-CALIBRATION-TRACKER.md — checklist

Goal: SumoSharp auto-calibrates the highest believable traffic density (matches vanilla's knee).
See `-DESIGN.md` (HOW), `-TASKS.md` (success conditions), `DENSE-FLOW-THROUGHPUT-DIAGNOSIS.md` (Gap 1
evidence). NEEDs: `SUMOSHARP-NEED-dense-flow-gridlock-vs-vanilla.md`,
`SUMOSHARP-NEED-serve-calibration-parity-gaps.md`.

- [x] **Stage 0** — reproduce Gap 1 gridlock (2× dense synthetic: vanilla 0 tp/290 arr/drains vs SumoSharp
      10 tp/275 arr/~45 stuck) + root-cause (wrong-lane strand + clamp at `TryReResolveFromActualLane`
      ~9080, no reroute fallback) + rule out overlap fix + confirm Gap 2/Gap 3 sites. Diagnosis committed.
- [x] **Stage 1 — Gap 3** departPos="base" → SUMO `basePos` (faithful, not "0"). DONE: `DepartPosSpec.Base`
      + `Engine.BasePos` (MIN(vType.Length+0.1, laneLength), capped to first-edge stop endPos). Anchor
      `scenarios/75-base-depart` (veh0→7.1, veh1→3.0) matches vanilla golden to 1e-13. Full suite green (655
      pass); all pre-existing goldens byte-identical (Base arm inert). Box `"base"` error gone → now blocks
      only on Gap 2 parking (as predicted).
- [x] **Stage 2 — Gap 2** parkingArea runtime lowest-free-lot reuse (MSParkingArea::computeLastFreePos).
      DONE: lot assignment moved from static load-time to runtime — `_parkingLotOccupied` table,
      claim-lowest-free on the park (Reached) transition, free on pull-out (Resume), provisional brake
      target from the start-of-step snapshot in `StopLineConstraint`, wait-when-full reached-gate,
      departPos="stop" origins claim at insertion. Fixed a regression (scenario 69: the LC-toward-stop
      `driveToNextStop` distance also read the deferred EndPos). All parking goldens (48/66-72)
      byte-identical; anchor `scenarios/76-parking-lot-reuse` (cap-1, veh0 pulls out → veh1 reuses lot 0)
      matches vanilla golden. Full suite green (656 pass). Full `demo_city/box` LOADS + runs to t=800 with
      no "lot index out of range"; two box runs byte-identical (deterministic).
- [x] **Stage 3 — Gap 1** dead-lane gridlock. **SOLVED (session 2, branch `claude/dense-lane-overlap-fix-5tr4ha`).**
      2× dense synthetic drains to **0 teleports / 290 arrivals = vanilla parity** (was 10 tp / 275 arr /
      ~45 stuck); 1× **5→1 teleport / 290 arr** (guard `<=2`). Fix = three faithful parts, all gated on the
      car being on a DEAD LANE (byte-identical for every golden): (1) `DeadLaneMergeBrakeConstraint` — port
      of `MSLCM_LC2013::informLeader` `stopSpeed(myLeftSpace)`, decelerate to re-try the merge; (2) boundary
      reroute (existing, 5 s); (3) `TryRerouteStuckDeadLane` — reroute a yield/red-held dead-lane car just
      before teleport (90 s gate, separate from the boundary's 5 s; an eager stuck-reroute churns the dense
      case). Full suite green (657 pass), all goldens byte-identical, deterministic (serial == parallel).
      Anchor: `scenario.dense.{rou,sumocfg}` + `DenseFlowDeadLaneDrainTests`. See design §2.3.5. Below is the
      earlier partial-fix history (superseded):
- [~] ~~Stage 3 partial (superseded by §2.3.5)~~ reroute-on-wrong-lane. PARTIAL FIX after 4 passes.
      See design §2.3.1 for the full evidence. Summary:
      - Passes 1-2: an INSTANT reroute drains 2× fully (arrived ≈292) — confirms clamp=gridlock — but
        cascades at low density (a small perturbation tips SumoSharp's fragile LC/junction behaviour, so ~100
        cars strand at 1×; some loop, e.g. veh 58 on 109_1 whose every route returns to 109_1). Instant fails
        the ≤5 guard (6-18 tp). Diagnosis (Try 2): vanilla COMPLETES the 109_1→109_0 change at 1× (veh 58) and
        REROUTES the truly-blocked car at 2× (veh 295 on 30_1 → its lane's own connection, arrives t=412) —
        so reroute is directionally right, but SumoSharp cascades because its substrate is fragile, not because
        the reroute is wrong.
      - Passes 3-4: gate the reroute as a LAST RESORT — only after a car has been clamped/blocking ~5 s
        (`DeadLaneRerouteWaitSeconds`), + U-turn skip + a per-car cap. This keeps 1× EXACTLY at baseline
        (5 tp / 287 arr, guard green) while improving 2× (teleports **10→3**, arrivals **275→281**, halting
        45→42). LANDED: full suite 656 green, all goldens byte-identical, deterministic.
      - STILL OPEN: 2× does not fully drain. **ROOT CAUSE + EXACT FIX NOW DIAGNOSED from SUMO source** —
        see `docs/GAP1-RESUME.md` (single entry point) and design §2.3.4. It is `ignore-route-errors` +
        lane-continuation: SUMO plans the car's move along its ACTUAL lane's connection (`30_1→44`) and
        crosses WHILE MOVING; SumoSharp pins the pool exit lane and HOLDS the car (→ queue → gridlock). The
        fix = port `getBestLanesContinuation` "continue along the lane you're on" semantics (byte-identical
        for goldens by construction). NOT congestion rerouting (124 is cheaper than 44 in vanilla) and NOT
        cooperative LC (retired). **Next session: implement per GAP1-RESUME.md; do not re-investigate.**
- [ ] **Stage 4** — end-to-end: full box (and crop if reachable) on SumoSharp ≈ vanilla (teleports ≈ 0,
      knee within tolerance). Hand-off note back to SumoData.

## Standing measurements (baseline, main 8bb8219, 2× dense synthetic)
| | teleports | arrivals | halting steady |
|---|---|---|---|
| vanilla | 0 | 290 | 0 (drains) |
| SumoSharp | 10 (8 yield) | 275 | ~45 (gridlock, meanSpeed 0) |
