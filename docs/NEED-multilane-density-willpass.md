# NEED — multi-lane high-density gridlock: the deferred willPass pre-pass

**For the SUMO parity coding session.** After `C4-vii-c` (`d1e8fb5`, the route→lane over-constraint
fix), moderate-density multi-lane (`-L 2`) now runs clean and matches SUMO. But a **higher-density**
`-L 2` city still gridlocks — ~15% of vehicles stuck at junction stop lines while SUMO runs the
identical net with **0 teleports**. Root cause is exactly the **willPass pre-pass** that was scoped
during C4-vii-c but deferred (the actual C4-vii-c fix was a different, smaller bug). Verified on
`main@f899f4e`. Parity-track bar (exact `@1e-3`, anchor + golden + gate). **High regression risk —
this is a new phase in the RoW core.**

## Reproduced (engine vs SUMO on the identical net+demand)

```
export SUMO_HOME=/usr/local/lib/python3.11/dist-packages/sumo
netgenerate --grid --grid.number=6 --grid.length=300 -L 2 --tls.guess --seed 11 -o net.net.xml
python3 $SUMO_HOME/tools/randomTrips.py -n net.net.xml -e 400 -p 2 --fringe-factor 12 --min-distance 600 --seed 11 -o trips.xml
duarouter -n net.net.xml -r trips.xml -o rou.rou.xml --seed 11 --named-routes --ignore-errors
```
- **Engine** (`Sim.BenchCity`, peak concurrent ~126): 200 departed, **142 arrived, 30 stuck**
  (>=120 s at <0.1 m/s). ~36 of the stopped vehicles sit AT a junction stop line (<15 m from the
  lane end, both lanes); the rest queue behind them.
- **SUMO** (`sumo -n net.net.xml -r rou.rou.xml --end 600`): **200 inserted, 0 teleports**, avg
  duration 198 s, only 21 still in transit at the window end. Free-to-moderate congestion, no
  gridlock.

**Density is the trigger.** The SAME generator at MODERATE density (`--grid.length 250`, period 4,
seed 7 → ~52 concurrent) now runs **clean: 74/75 arrived, 0 stuck**. Only the saturated case fails,
which is the tell for a start-of-step-speed ordering bug rather than a static RoW error.

## Mechanism (your own C4-vii-c diagnosis — it was deferred, not fixed)

The root blockers are the ~5-8 vehicles actually at a crossing arm; the rest are queued behind them.
A root blocker yields to a foe that is **close AND moving at start-of-step (3-13 m/s)** — but that
foe is itself **braking to a stop this step** (it is yielding to someone else), so its `vNext ≈ 0`
and SUMO's `willPass` for it is **false**. The engine reads the foe's **start-of-step speed** (still
> 0), so it treats the foe as "will pass" and yields → both sit → gridlock cascades as traffic
queues behind. At moderate density few foes are simultaneously braking-to-stop, so it rarely fires;
at saturation it fires everywhere. This is SUMO's `setApproaching`-before-`opened()` ordering
(`MSLink`): a vehicle registers its intended pass with its `vNext`-at-link BEFORE any crossing-yield
decision reads it. The engine has no such pre-pass — the crossing-yield decision reads raw
start-of-step speed.

## The fix: a willPass pre-pass phase

Add a phase to the step loop, BEFORE the crossing-yield decisions, that computes each vehicle's
`vNext`-at-its-approaching-link from the **frozen start-of-step snapshot** (mirroring
`MSLink::setApproaching` populating `myApproaching` before `MSLink::opened()` is consulted). Then in
`Engine.JunctionYieldConstraint`, block ego only on a foe whose **willPass is true** (foe's computed
`vNext` > halting threshold at its link), not on a foe whose raw start-of-step speed is > 0. A
stopped-foe gate (`foe.speed <= eps → willPass false`) alone was tried and helps (cut stuck 40→32 on
the priority grid) but is insufficient — the braking-to-stop foes have `speed > 0` this step; only
their `vNext` reveals the truth, hence the pre-pass is required.

## Definition of done

1. **Dense `-L 2` city flows.** The repro above runs with stuck-count comparable to SUMO (≈0) and
   arrived within the benchmark's aggregate tolerance. Moderate density stays clean (no regression).
2. **New anchor + golden.** A small fixed-route deterministic 2×2 or 3×3 `-L 2` grid tuned to the
   saturation point where two root blockers mutually brake-to-stop, so the willPass ordering is
   exercised (distinct from the C4-vii-b/c anchors). `sigma=0`, SUMO golden `--precision 6`, match
   `lane`/`pos`/`speed` `@1e-3`.
3. **Inert / no regressions.** All ~30 committed single-lane + moderate multi-lane junction scenarios
   stay green (`dotnet test`, currently **166**); `Sim.Bench` hash unchanged. The pre-pass must be a
   no-op wherever no vehicle is braking-to-stop at a crossing (which is every current scenario).
4. **Gate — HIGH regression risk.** A new phase in the RoW core touches every junction scenario;
   hard-gate through parity-reviewer, and re-run the full suite plus a moderate-density `-L 2` sanity.

## Why it matters

This is the last thing between "multi-lane works at low density" and "multi-lane works at scale." It
gates the scaled-city benchmark's **dense** multi-lane rungs and any busy multi-lane demo. Low- and
moderate-density multi-lane already work and are demonstrated.
