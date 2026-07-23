# #15 into-occupied cut-in fix — design

Live-city demo only; **inert on every parity/bench golden** (the engine knob defaults to 0). Parity is the
iron law: this design changes nothing byte-wise on any golden. See `LIVE-CITY-15-ATTEMPT-LOG.md` for the
per-car measurement trail and the (rejected) attempt-1 gridlock result that shaped the shipped scope.

## WHAT (the problem)
After the cooperative-LC fix removed the pure-lateral float, one residual remained: **"into-occupied"
cut-ins** — a car slots into the target lane close to a STANDING car there. Owner rule: *never swap into an
occupied lane; if a merge is required it must be cooperative*.

Per-car measurement (`LIVECITY-LCDETAIL`, side × gap bucket) established the precise shape:
- Every such commit is by a **moving** changer (the float is gone) — a forward+lateral diagonal, which the
  owner explicitly permits.
- Dominant case: **follower-side, gap 2–5 m** — a moving ego slots in 2–5 m *ahead of a stopped follower*.
- **Nothing** lands within 2 m; no car lands on top of another.
- Split by trigger path: **discretionary** (speed-gain, keep-right) vs **required** (strategic turn-lane).

## HOW (mechanism)
`IsTargetLaneSafe` is a **braking-gap** check. A stopped follower needs ≈no secure gap, so the safety test
passes no matter how tightly ego lands ahead of it — that is the loophole that lets a moving ego cut in.

**The veto.** `Engine.MergeStoppedMinGap` (double; `0` = OFF = byte-identical on every golden). When `> 0`,
a lane-change commit is vetoed if it would put ego's back bumper within that absolute distance **ahead of a
nearly-stopped (<0.5 m/s) target-lane FOLLOWER** — `Engine.WouldCutInAheadOfStoppedFollower`, using the same
netto-gap convention as `IsTargetLaneSafe`'s follower branch (a negative/overlap gap is vetoed too).

**Follower side ONLY.** Merging *behind a stopped leader* is a normal queue-join; vetoing it would strand
any car approaching the back of a stopped target-lane queue.

**Applied to DISCRETIONARY paths only** (speed-gain, keep-right). The **strategic** path is a REQUIRED merge
(ego's lane must reach its turn) and is deliberately exempt: when the target turn lane is a saturated stopped
queue, *every* merge point is within the floor of a stopped follower, so vetoing the required change strands
ego at the approach → it blocks its through lane → the #15 gridlock reforms. This was **measured directly**
(attempt 1: veto-all collapsed arrivals 1085→361, stoppedFrac→1.00 by t≈900) — the same structural mistake
as suppressing the load-bearing keep-right sort. A discretionary change that is declined simply leaves ego in
its already-fine lane (zero strand risk); a required merge into a saturated queue IS the realistic queue-join
and must be allowed.

**Gating.** Callers additionally gate on `CooperativeInformFollower` (the high-realism master switch), and the
demo sets `MergeStoppedMinGap` only when `CooperativeLaneChange` is on. So high-realism areas refuse the tight
discretionary cut-in; low-realism areas keep the cheap merge — consistent with the per-area LOD requirement.

## Determinism / parity argument
- `MergeStoppedMinGap` defaults to 0 ⇒ `WouldCutInAheadOfStoppedFollower` returns false unconditionally ⇒
  every commit site is byte-identical to before ⇒ every golden unchanged. Verified: parity 657/4
  byte-identical, bench hash `D96213B7BB4021A7`, parallel==single.
- The veto is a pure function of frozen start-of-step neighbor state (the same snapshot the existing safety
  checks read) — no `System.Random`, order-independent, so serial==parallel is preserved.

## Knobs
- `Engine.MergeStoppedMinGap` (m). Demo default 5.0 (covers the measured 2–5 m band). Env `LIVECITY_MERGEGAP`.
  0 disables. `LIVECITY_COOP=0` also disables it (low-realism fallback).

## Result (verified first-hand, t≈1400)
| metric | baseline | attempt-1 (all paths) | SHIPPED (discretionary only) |
|---|---|---|---|
| arrivals | 1085 | 361 (GRIDLOCK) | 1060 (no regression) |
| stoppedFrac late | 0.2–0.5 | 1.00 | 0.2–0.5 |
| speedGain foll<5 cut-ins | 11 | 0 | 0 |
| keepRight foll<5 cut-ins | 3 | 0 | 0 |
| strategic foll<5 cut-ins | 46 | 0 | 44 (required queue-joins, left) |

## Follow-up (open)
Reduce the strategic follower-side merges *cooperatively* without stranding — e.g. urgency-gated deferral
(defer a tight cut-in only where ample road remains to merge more cleanly, allow it once urgent). Separate,
measured step; back off on any flow regression.
