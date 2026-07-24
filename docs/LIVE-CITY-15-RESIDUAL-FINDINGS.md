# Issue #15 residual — diagnosis FINDINGS (chase branch)

**Branch:** `claude/livecity-15-turnlane-segregation` (off the merged live-city tip incl. `184fb31`).
**Status: root cause CONFIRMED + localized. Fix is design-first + owner-nod (HIGH parity risk) — not
yet implemented.** Companion to `docs/LIVE-CITY-15-RESIDUAL-REPRO.md` (the repro spec this answers).

## Method
Two independent lines of evidence, cross-checked:
1. **Engine-authoritative witness** (`LIVECITY_WITNESS=1` on `--mode live-city --smoke`, added this
   branch): `LiveCitySim.WitnessAuthoritative()` reads the live `Engine`'s per-vehicle columns
   (lane id, longitudinal `pos`, `posLat`, `speed`, controlling-TL char, gap-ahead) and classifies
   every stopped car by *why* it is stopped. Read-only, parity-untouched.
2. **Code + SUMO-source analysis** of the lane-change / best-lanes path (getBestLanes, keep-right
   stayOnBest, strategic change) against the vendored `/sumo/` reference.

## What the witness shows (cap 160, t≥60)
```
LIVECITY-WITNESS: t   stuck  stuckOnGreenClear  stuckRed  stuckBehindLeader
             80    122        12                 92        18
            120     80        12                 62         6
            180     83        16                 24        43
```
- **`posLat = 0.00` for every stuck car, at every checkpoint.** There is **no lateral "float" in the
  engine.** The jockeying seen on the GPU is a **render/DR-reconstruction artifact** (lane-change
  smoothing over a dead-stopped car), not engine motion — this reframes the GPU verdict.
- The **majority** of stopped cars are at **red lights** (`stuckRed`) or **queued behind a stopped
  leader** — i.e. the *consequence* of a jam, normal propagation.
- The **anomaly** is `stuckOnGreenClear` (7–16 cars): `speed≈0`, TL **green**, **no leader within
  15 m**, at `pos≈226–233` (the stop line). Several are on **protected green `G`** (not just
  permissive `g`), so it is **not** legitimate minor-green yielding. These are cars that *could*
  discharge but don't. Each one **serial-blocks the whole queue behind it** → the small stranded set
  is the *root*, the large red/leader queues are downstream of it.

## Root cause (code-localized)
A turner that needs a turn lane but finds it **saturated** can never complete the merge, strands at the
stop line, and is clamped to `Speed=0`:
- getBestLanes IS correctly ported (`NetworkModel.ComputeBestLanes`, offsets correct); keep-right
  stayOnBest **rule 2** IS landed/active (`Engine.cs:10486-10510`). So "wrong turn-lane selection" is
  **falsified** — the pool targets the right lane.
- **Gap 1 (primary):** `TryStrategicLaneChange` (`Engine.cs:11043-11052`) — when the wanted strategic
  change is blocked by target-lane traffic, it does a bare `return false`. SUMO's **`LCA_URGENT`**
  machinery is **not ported**: no ego brake-to-wait for a gap (`MSLCM_LC2013.cpp:1467-1517`,
  `myLeftSpace`/`stopSpeed` + `myLeadingBlockerLength`), and no cooperative gap-opening by the
  target-lane follower (`informFollower`, `.cpp:1642-1655`). So under saturation the turner re-wants /
  re-vetoes every step and never merges.
- It then reaches the stop line on a through-lane with **no connection to its next (turn) route edge**;
  the only rescue `TryReResolveFromActualLane` returns false (the lane genuinely doesn't connect) →
  `Pos=laneLength; Speed=0; break` (`Engine.cs:9587-9611`). = the on-green freeze.
- **Gap 2 (secondary, deferred):** `LaneQ.occupation` is not ported (`best.occupation=0`,
  `Engine.cs:10993-10997`), so a strategic change only becomes urgent from distance-to-lane-end, never
  from how jammed the target lane is — SUMO would trigger the merge farther upstream. This was designed
  (`ad8d738`) and **measured low-ceiling then reverted** (`965fc45`); leave deferred until Gap 1 lands.

## Fix direction (for the design doc — not yet built)
Port SUMO's **URGENT strategic cooperation** at the `Engine.cs:11048-11052` veto: instead of a bare
`return false` when the turn lane is blocked, (a) brake the ego to wait for a gap, and (b) signal the
target-lane follower to cooperatively open one, so the merge completes in the queue instead of
stranding at the stop line. Interim/cheaper alternative flagged in `DENSE-FLOW-THROUGHPUT-DIAGNOSIS.md`:
generalize `TryReResolveFromActualLane` to fire on the stop-line *approach* (reroute onto the
connection the actual lane offers, as vanilla does) — but that changes routing, not segregation.

## Parity risk — HIGH (why this is design-first + owner-nod)
The lane-change / cooperation path is the **most golden-dense area in the repo** (LANE-CHANGE-OVERLAP,
keep-right rung 8b, cooperative-LC HIGH-DENSITY-P2G2/P2G3, every dense-LC rung). URGENT/cooperative LC
touches **follower** behavior directly, so it can move those trajectories. Non-negotiable per CLAUDE.md:
keep every committed golden byte-identical (`Sim.ParityTests` 657/4) or gate behind an explicit
fast-mode flag; verify serial == `--max-parallelism 8`; validate at **sustained** density (the
`c1-perm-turn` / `art.sumocfg` through-discharge witnesses), never sparse-probe.

## Objective gauge for any fix (unchanged)
The `LIVECITY-GRIDLOCK` / `LIVECITY-WITNESS` probes: a real fix drives peak `stoppedFrac` well below
~0.6, pushes `arrivals@200s` past 81 toward the hundreds, and collapses `stuckOnGreenClear` toward 0 —
while keeping parity 657/4 and the bench hash unchanged.

## Anchors
- `src/Sim.Viewer/Program.cs` `RunLiveCitySmoke` — the `LIVECITY-GRIDLOCK`/`LIVECITY-WITNESS` probes.
- `src/Sim.LiveCity/LiveCitySim.cs` `WitnessAuthoritative()` — the engine-authoritative accessor.
- `src/Sim.Core/Engine.cs:11043-11052` (the veto = fix seam), `:9587-9611` (Speed=0 clamp),
  `:10486-10510` (rule-2 keep-right, landed — do not re-touch), `:10993-10997` (Gap 2, deferred).
- `src/Sim.Ingest/NetworkModel.cs:609-876` (getBestLanes port), `:219-223` (`LaneContinuation`).
