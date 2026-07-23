# c1-perm-turn — SumoData's verified C1 witness: the deficit is the STEM THROUGH, via turn-lane mis-segregation at d_4_2 (NOT gap-acceptance)

**Date:** 2026-07-23. **Source:** SumoData's `c1-perm-turn-starvation` witness package (a netconvert crop of
their real box around junctions `d_4_1`/`d_4_2`, static routes, no `device.rerouting`, `sigma=0.5`). This is
the FAITHFUL box-derived anchor we asked for. Files here are their `net.xml` / `routes.rou.xml` /
`scenario.sumocfg` verbatim + their `README.md` / `MECHANISM-STATUS-and-how-to-use.md`.

## Reproduced on `claude/dense-lane-overlap-fix-5tr4ha` (rule-2 HEAD)
`--end 1200`, final step: vanilla **253 running / 1301 arrived / 2.27 m/s**; SumoSharp **316 / 1081 / 1.50**
(+25% running, −17% arrived, −34% meanSpeed). Same direction as the witness's `1a908ee` numbers (390/1009/1.04);
milder because rule-2 fixed part of the d_4_1 segregation and sigma adds run variance.

## What we INSTRUMENTED (per SumoData's ask — stop attributing from aggregates)
Progressive per-constraint `vPos` trace on head-of-lane stopped vehicles. Findings, in order:

1. **The deficit is the STEM THROUGH movement, not the permissive left.** Per-movement discharge (distinct
   vehicles crossing each junction internal lane, whole run):

   | movement | vanilla | SumoSharp |
   |---|---|---|
   | `:d_4_1_1_*` opposing through | 269/268 | 274/261 (parity) |
   | **`:d_4_1_4_0` stem THROUGH** | **252** | **70** (−72%) |
   | `:d_4_1_6_0` permissive LEFT | 27 | **26 (parity!)** |
   | **`:d_4_2_4_0` stem THROUGH (mirror)** | **243** | **55** (−77%) |

   The permissive LEFT discharges identically to vanilla. **Gap-acceptance is NOT the deficit** — the
   head-of-lane binder histogram shows `JunctionYieldConstraint` binds ~0–3 steps; the binders are
   car-following (`leader`, `crossJxnLeader`) + `redLight`. This is the FOURTH failed "gap-starvation"
   attribution (after C3-rerouting, clean-C1, local-starvation) — all now retired.

2. **The through fails to discharge into a FREE exit.** Through-chain occupancy (mean veh/step): SumoSharp's
   exit `e_d_4_2_d_4_3` is nearly EMPTY (1.3 vs vanilla 4.8) while its upstream edges are MORE jammed
   (`e_d_4_1_d_4_2` 46.7 vs 37.8). So it is not downstream backpressure — the through is blocked at the
   junction while the exit sits empty.

3. **ROOT CAUSE: turn-lane mis-segregation at d_4_2.** Lane distribution on the d_4_2 approach `e_d_4_1_d_4_2`
   (only lane2 connects to the d_4_2 left `e_d_4_2_d_3_2`):

   | flow | vanilla | SumoSharp |
   |---|---|---|
   | `flow_left2` (left) | **L2 = 100%** | **L1 = 35%**, L2 = 65% |
   | `flow_through` | L1 91% / L2 9% | L1 63% / L2 37% |

   **35% of left-turners sit on lane1 — the through lane they cannot turn from — blocking the stem through
   behind them** (`crossJxnLeader`-bound, following the jammed left destination `e_d_4_2_d_3_2` which the
   opposing2 stream saturates). A stem-through car queued behind a mis-laned left-turner reads as
   `leader`/`crossJxnLeader`-blocked → the through under-discharges → `e_d_4_0_d_4_1` / `e_d_4_1_d_4_2` pile up
   while the exit starves.

## Why d_4_1 is (mostly) fixed but d_4_2 is not
Rule-2 got `flow_left` 100% onto lane2 at d_4_1 — it DEPARTS on the long, initially-clear approach
`e_d_4_0_d_4_1` and settles on lane2. `flow_left2` DEPARTS on `e_d_4_1_d_4_2`, which is already **saturated**
by the transiting `flow_through` (occupancy 46.7), so 35% insert on / are stuck on lane1 and cannot reach the
only left-capable lane2. This is the **merge-into-the-turn-capable-lane failure under saturation** — the same
mechanism the arterial exposed, and it DOES map to the shared-lane box (lane2 is shared through+left, but it is
the ONLY lane connecting to the left, so a left-turner must reach it).

## Conclusion / hand-off
The knee deficit on this verified box witness is **turn-lane segregation under saturation** (left-turners not
reaching the only turn-capable lane when they insert into a jammed shared-lane edge), manifesting as a **stem
THROUGH under-discharge** because mis-laned turners block the through lane. It is **not** permissive-turn
gap-acceptance (parity), **not** a through-release gate (C3, retired), **not** keep-right drift alone (that is
downstream). The fix direction: get a left-turner onto the only turn-capable lane under congestion —
strategic-LC-toward-the-turn-lane with the urgency/cooperation that lets it complete even when inserting into a
saturated edge (LCA_URGENT blocker-cooperation is the piece SumoSharp still lacks; see
`Engine.cs` TryStrategicLaneChange's "future work" note). Golden-sensitive (LC model) → design-first + nod
before implementing; validate on THIS witness (through discharge → vanilla) + box discharge, byte-identical
goldens.
