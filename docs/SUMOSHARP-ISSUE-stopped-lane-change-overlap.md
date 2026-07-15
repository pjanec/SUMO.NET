# Issue: stopped car lane-changes into an occupied space (vehicle overlap)

**Status:** ISOLATED + documented; **fix deferred to a later session.** **Type:** engine (parity)
correctness bug — NOT a viewer/DR artifact (proven below). **Reported:** user, 2026-07-15, seen in the
native DR viewer. **Related memory:** `issue-lane-change-into-occupied-space`.

---

## 1. Symptom

At a red light with two adjacent lanes of cars waiting side by side, a WAITING (stopped) car changes
lane INTO the adjacent lane where another car is ALREADY stopped, ending up **physically overlapping**
it (impossible given both vehicles' length). First noticed in `--mode loopback --fleet … samples/
junctions/cross`.

## 2. Determination — ENGINE, not the DR viewer (proven)

An authoritative-snapshot overlap detector (scan `SimulationSnapshot` each sim step for two vehicles on
the same `LaneHandle` whose bodies intrude) found the overlaps **in the authoritative engine state
itself** — no dead-reckoning involved. So the DR viewer is faithfully drawing a real engine overlap;
the fix belongs in `Sim.Core`, not the viewer.

**Detector note (important for whoever re-runs this):** `Pos` is the **front-bumper** arc position, NOT
the center (confirmed in `Engine.cs`: `leaderBackPos = leaderPos - leaderLength`, e.g. ~lines
2231-2232 / 5984-6055, matching SUMO's convention). So the correct intrusion test for a same-lane pair
is, with `ahead` = larger `Pos`:
```
overlapBy = Pos[behind] - (Pos[ahead] - Len[ahead])   // > ~0.10 m  => real physical overlap
```
A center-symmetric `|Pos_i - Pos_j| < 0.5*(Len_i+Len_j)` test gives massive FALSE positives whenever a
long vehicle legally trails a short one — do not use it.

## 3. Deterministic repro

The seeded sandbox reproduces it identically across runs:
```
dotnet run -c Release --no-build --project src/Sim.Viewer -- --mode publish --fleet 1500 --seconds 60 samples/junctions/cross
```
(with the §2 detector temporarily added to `RunPublish`, once per sim step). Clearest instance:

- **t=51.0, lane 28 (`EC_0`):** `__veh24` (len 12 m, spd 0) is stopped in the adjacent lane 29 (`EC_1`)
  at arc-pos 109.50 at the red light; `__veh5` (len 5 m, spd 0) is stopped in lane 28 at the **same**
  arc-pos 109.50. At t=51 `__veh24` **lane-changes EC_1 → EC_0, landing at pos 109.50 fully on top of
  `__veh5`** (`overlapBy = 12.00 m`). It stays overlapped for the rest of the run (never resolves).
- Second instance, **t=48.0, lane 30 (`NC_0`):** `__veh47` (5 m, stopped in `NC_1`) lane-changes into
  `NC_0` at pos 108.60, overlapping `__veh53` (still decelerating in `NC_0`) by 4.78 m.

Both are on the cross intersection's adjacent approach lanes (`EC_0/EC_1`, `NC_0/NC_1`) — exactly the
"two lanes waiting side by side at the red light" the user described. Deterministic (same numbers on
repeat). **Not** reproduced on `scenarios/evac-grid-tls` in a 60 s / ~590-vehicle window (its junctions
/ demand don't drive this queue geometry) — so `samples/junctions/cross` is the repro net.

## 4. Mechanism (hypothesis, to confirm when fixing)

A vehicle stopping/stopped at the front of a queue commits a lane change into the target lane **without
validating clearance against the vehicle already occupying the target lane at that arc-position**. The
gap-acceptance / safety check that should block the change (or bound the landing position behind the
target-lane leader's back bumper) is not firing when both ego and the target-lane occupant are at ~zero
speed at the same stop line.

**Where to look (Sim.Core/Engine.cs):**
- `TryStrategicLaneChange` (~7623) and `TryGiveWayLaneChange` (~3887) — the change *decision* sites.
- `CommitLaneChange` (~3910, ~7741) — where the change is applied; check what gap/leader-clearance
  guard (if any) precedes it, and whether it uses the target lane's occupant *back bumper*
  (`leaderPos - leaderLength`) as the limit.
- `AdvanceLaneChanges` (~2029) for the continuous-maneuver variant.
Port/compare against the corresponding SUMO lane-change safety check (`MSLaneChanger` /
`LCM::checkChange` gap acceptance) in the vendored `/sumo/` source.

## 5. Open question that scopes the fix — parity vs SUMO

Is this **our reimplementation diverging** from SUMO (a parity bug to fix), or does **SUMO itself** do
it (a model behavior we're faithfully copying, which we might document rather than "fix")? The user's
instinct is that it's engine-side; that's confirmed, but the SUMO-vs-ours question is still open. To
answer it the fixing session should:
1. **Build a MINIMAL committed repro** (`*.net.xml` + `*.rou.xml` + `*.sumocfg`): a 2-lane approach to a
   `traffic_light` junction where a vehicle in one lane is *motivated/forced* to change into the other
   lane (e.g. a connection/route that requires the occupied lane), with a car already stopped there at
   red. Aim for a deterministic, few-vehicle case (unlike the 1500-fleet sandbox).
2. **Run our engine vs SUMO** on it via the golden loop (`scripts/regen-goldens.sh` / direct
   `sumo` run — SUMO is available in this repo) and diff FCD: does SUMO also overlap?
   - SUMO overlaps too → it's SUMO's model; document, likely accept (or match exactly).
   - SUMO keeps them apart → **our parity bug**; fix the §4 gap check to match SUMO, and commit the
     minimal scenario as a new golden that asserts no same-lane overlap.

## 6. Constraints for the fix (CLAUDE.md)

This is `Sim.Core` — the parity engine. Any fix is bound by the iron tolerance law: it must not push
any existing scenario out of its `tolerance.json`, and should land with a new committed scenario/golden
covering this case. Because it touches car-following/lane-change ordering, treat it as behavioral —
follow SUMO's algorithm and calculation order, don't invent a guard.
