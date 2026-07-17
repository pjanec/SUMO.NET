# HIGH-DENSITY-P2H-DESIGN.md — max-depart-delay / insertion backlog eviction

**Status:** design (owner-approved to proceed, 2026-07-17). Item **P2-H** from `HIGH-DENSITY-HANDOFF.md` §4.
Grounds in vendored `sumo/src/microsim/MSInsertionControl.cpp`.

## 1. WHAT (the gap)

A vehicle that cannot find a safe insertion gap at its `departPos` is retried **every step, forever** —
`Engine.InsertDepartingVehicles` re-adds it as a candidate each step and `TryInsertOnLane` returns
false with no elapsed-wait bookkeeping, no drop. SUMO instead deletes a vehicle that has waited longer
than `--max-depart-delay`. The key is entirely **unparsed** in SumoSharp today (zero matches for
`max-depart-delay`/`maxDepartDelay` under `src/`). On a saturated origin edge this means the engine
holds an unbounded backlog of never-departing vehicles that SUMO would have dropped.

## 2. SUMO reference (exact semantics)

`MSInsertionControl::tryInsert` (`MSInsertionControl.cpp:155-188`): the vehicle is **first** offered
for insertion this step; only if that fails is the delay checked:

```cpp
if (myMaxDepartDelay >= 0 && time - veh->getParameter().depart > myMaxDepartDelay) {
    // remove vehicles waiting too long for departure
    myVehicleControl.deleteVehicle(veh, true);   // discard: loaded but never inserted
}
```

- **Gate:** `maxDepartDelay >= 0` (a negative value — the default `-1` — disables deletion entirely).
- **Condition:** strict `time - depart > maxDepartDelay`, measured from the vehicle's ORIGINAL depart
  time, not a running "first-attempt" clock.
- **Order:** insertion is attempted first; a vehicle that *can* insert on the very step it crosses the
  threshold still departs. Deletion only applies to a vehicle that failed to insert THIS step.
- **Effect:** `deleteVehicle(veh, true)` — the vehicle is removed from the pending queue and never
  appears on the road (absent from FCD). It counts as loaded-but-not-inserted, not as an arrival.

`--max-depart-delay` is a `<processing>` option in seconds; SUMO's internal `SUMOTime` is ms, but at
this repo's 1 s step granularity the comparison is in whole seconds.

## 3. HOW (the fix)

### 3.1 Config plumbing (Sim.Ingest)
- Add `double MaxDepartDelay = -1.0` to `ScenarioConfig` (default -1 = never delete → inert).
- Parse in `ScenarioConfigParser`: `MaxDepartDelay: ParseDouble(processingEl, "max-depart-delay", -1.0)`,
  alongside the existing `time-to-teleport` etc.

### 3.2 Eviction in `Engine.InsertDepartingVehicles`
Restructure the per-candidate loop so eviction runs after a failed insertion, matching SUMO's order
(attempt first, then delay check). A candidate is "not inserted this step" if it is FIFO-blocked
behind an earlier same-lane failure (`blockedLanes`) OR `TryInsertOnLane` returns false — both are
SUMO "refused emits" and both are delay-eligible:

```
foreach (var v in candidates)
{
    ... resolve laneId/laneHandle/departLaneIndex ...
    var inserted = false;
    if (!blockedLanes.Contains(laneId))
    {
        if (TryInsertOnLane(v, laneHandle, departLaneIndex)) inserted = true;
        else blockedLanes.Add(laneId);
    }
    if (!inserted
        && _config!.MaxDepartDelay >= 0.0
        && time - v.Def.Depart > _config.MaxDepartDelay)
    {
        EvictOverdueDeparture(v);   // MSInsertionControl.cpp:168 deleteVehicle(veh, true)
    }
}
```

`EvictOverdueDeparture` reuses the pending-removal idiom already used by `Despawn`:
`v.Inserted = true; v.Arrived = true;` — this drops the vehicle from `InsertDepartingVehicles`'
candidate scan and from every active scan, so it never emits an FCD point (exactly SUMO's "absent from
the road"). Optionally bump a per-run `_discardedDepartures` tally for observability (not compared by
any committed golden).

### 3.3 Counting is parity-safe as-is
- **FCD:** the evicted vehicle is absent — identical to SUMO. This is the acceptance gate.
- **Summary-output:** running/halting counts come from active vehicles only; an evicted (Arrived)
  vehicle is not active, so it is not counted as running. Correct.
- **Statistic-output:** the harness comparator reads only the `<teleports .../>` subset
  (`StatisticRecord`); eviction touches no teleport counter. Correct.
- The only side effect is the API lifecycle event stream emitting a bare `Arrived` for the evicted
  vehicle — identical to the pre-existing `Despawn`-of-a-pending-vehicle behaviour, and not a parity
  surface. A distinct "Discarded" lifecycle is deferred (not needed by any parity output).

## 4. Determinism / parity argument (additive · gated · byte-identical)

- `MaxDepartDelay` defaults to `-1.0`; the eviction branch's `>= 0.0` gate is false for every existing
  scenario, so the loop is byte-identical (the added branch never runs). The full `dotnet test` suite
  is the gate.
- The restructured loop is behaviourally identical to the original when the gate is off: same
  attempt order, same `blockedLanes` FIFO, same `TryInsertOnLane` calls. (Verified by the suite.)
- Eviction reads only `_config` + the vehicle's own immutable `Def.Depart`; it mutates only the
  vehicle's own `Inserted`/`Arrived`. `InsertDepartingVehicles` is already the serial, single-threaded
  insertion pass, so no concurrency concern.

## 5. Success conditions (acceptance gate)

1. **New anchor** `scenarios/50-max-depart-delay`: a single origin edge saturated so at least one
   vehicle cannot insert before `--max-depart-delay` elapses and is dropped, while others insert
   normally. `sigma=0`. Golden from vanilla SUMO 1.20.0 run WITH `--max-depart-delay <D>` (the value
   also set in the committed `config.sumocfg` so the engine reads it), `--precision 6`,
   `--fcd-output.acceleration`, `--time-to-teleport -1`. `tolerance.json` exact `lane`/`pos`/`speed`
   @1e-3. The test asserts (a) the trajectory matches the golden @1e-3, and (b) NON-VACUOUSLY that the
   dropped vehicle's id is ABSENT from the engine trajectory (present in the demand, absent from FCD) —
   i.e. it was evicted, not inserted. Without the fix this fails (the vehicle would eventually insert
   late / linger as an un-departed backlog and the trajectory would diverge).
2. **No regression:** full suite stays green and byte-identical (currently 558 → 560 with the anchor's
   tests). `Sim.Bench` determinism hash unchanged.

## 6. Explicitly deferred

- Distinct `Discarded` lifecycle / `--statistic-output` loaded-vs-inserted-vs-discarded counts (no
  committed golden compares them; the teleports subset is unaffected).
- SUMO's other `tryInsert` deletion branches (`isVaporizing`, `myAbortedEmits`, invalid start
  lane/permissions) — unrelated to `max-depart-delay` and not reachable from committed inputs.
