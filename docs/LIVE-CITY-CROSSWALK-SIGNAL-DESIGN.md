# Phase 2b — low-power pedestrians respect the crosswalk signal (Option A). Design + tasks

**Goal.** A low-power pedestrian must **not step onto a signalized crossing against its pedestrian
signal**. It waits at the kerb on red and crosses on the walk phase — so at a signalized junction peds
cross while cars are stopped on *their* red, and the "car drives over a ped on green" artifact disappears
at the source. Unsignalized crossings keep the "cars give way no matter what" gate as the safety net.

Chosen over the safe-interim (B): the owner wants the realistic behaviour and **no non-deterministic TLs**.
Feasible because every TL in the demo-city box is `type="static"` (51/51) — a pure, periodic function of
time — so a ped's wait can be computed analytically and **server==IG is preserved**.

Reference: `SUMOSHARP-LIVE-CITY-DECISIONS.md` (Q1 class table), `LIVE-CITY-CROSSING-YIELD-DESIGN.md`
(Phase 2, the occupancy gate). HOW only.

## 1. The determinism argument (why this stays server==IG)
A static `<tlLogic>` phase is `phase(t) = program.PhaseAt((t − offset) mod cycleLength)`. For a crossing
with controlling `LinkIndex k`, the pedestrian has **walk** exactly in the phases whose state char at
index `k` is `G`/`g`. So the crossing's walk windows are a **known periodic set**, and
`NextWalkStart(tArrive)` is closed-form. A ped's arrival time at a kerb is itself deterministic (sum of
earlier segment/wait durations), so the inserted wait — and hence the whole `ActivityTimeline` — remains a
pure function of time. The image-generator reconstructs the identical wait from the same TL program: no new
per-frame state on the wire (same "broadcast the timeline once" as the weave, W3).

**Guard:** if a crossing's TL is `actuated` (not a function of time), we do NOT plan a wait — that crossing
falls back to the always-yield stop-line (Phase 2 gate). The box has zero actuated TLs, so this is inert
here; it just keeps the code honest.

## 2. Mechanism
### (a) Crosswalk signal schedule — `CrosswalkSignalSchedule` (NEW, `Sim.Pedestrians/Crossing/`)
Built once from the net. For each **signalized** crossing (a crossing whose controlling `<connection>` has
a `tl` + `linkIndex`, and whose TL is `static`): store the `TlProgramSpec` + `LinkIndex`, and expose
`double NextWalkStart(crossingId, double tArrive)` — the earliest time ≥ `tArrive` at which the ped may
step on (start of the walk window covering/next-after `tArrive`; if already inside a walk window with
enough time to clear, returns `tArrive`). Reuses `CrossingTlReader.LoadPrograms` / `FindCrossingLink`. A
crossing that is unsignalized, actuated, or unknown returns `tArrive` (no wait — the gate handles it).

### (b) Wait insertion in the route→timeline build — `PedDemand.BuildLivelyTimeline`
Behind a new opt-in `PedDemandConfig.CrosswalkSignals` (an injected `CrosswalkSignalSchedule?`, default
null → **byte-identical to today**, so every committed ped test is unaffected). When present, as the
timeline is assembled segment-by-segment and the running clock advances:
- when the next path segment **enters a signalized crossing polygon**, split it: `Walk` up to the near
  kerb (clock advances to `tArrive`), insert `Pause(NextWalkStart(id, tArrive) − tArrive)` at the kerb
  (a real Pause with an "wait" anim tag), then `Walk` across.
- Mapping a path point → crossing polygon → crossing id is a point-in-polygon over the (few) signalized
  crossings the route touches; the nav route already threads crossing polygons, so this is a lookup, not a
  re-route.
This keeps the low-power ped a pure `ActivityTimeline` — no runtime signal polling, no promotion.

### (c) Car give-way, class-aware (tightening Phase 2's gate)
- **Signalized** crossing: peds now only occupy it during walk (car red), so the car is already stopped by
  its own TL — no gate disc needed there. The `CrossingOccupancySource` **skips signalized crossings**
  (they're handled by compliance), which also removes the phantom-jaywalker stops.
- **Unsignalized**: the gate stays, but upgraded from a point-disc to a **tunneling-proof stop-line** — a
  virtual stopped leader on each crossed lane just before the crosswalk while it is occupied, so a fast car
  at the coarse demo tick cannot jump the gate between steps. Needs crossing→crossed-lane(s) from the net
  `crossingEdges` + geometry.
- **Discouraged**: safety brake only (disc when a ped is physically in the lane), as Q1.

## 3. Files
- `src/Sim.Pedestrians/Crossing/CrosswalkSignalSchedule.cs` (NEW) + `CrossingTlReader` (reused).
- `src/Sim.Pedestrians/Demand/PedDemand.cs` — `PedDemandConfig.CrosswalkSignals`; wait insertion in
  `BuildLivelyTimeline` (inert when null).
- `src/Sim.Pedestrians/Crossing/CrossingOccupancySource.cs` — skip signalized crossings; stop-line form for
  unsignalized (needs the crossed-lane mapping; may split into its own small type).
- `src/Sim.Viz/SceneGen.cs` — `BuildLiveCity` builds a `CrosswalkSignalSchedule` from the net and passes it
  into the demand config; classify crop crossings (signalized vs not) for the gate.

## 4. Determinism & parity
- `PedDemandConfig.CrosswalkSignals` default null → every committed ped test byte-identical.
- `Engine.CrowdSource` still null for every vehicle golden → parity/hash unmoved.
- Waits + gates are pure functions of time → two demo runs byte-identical; server==IG holds.

## 5. Success conditions
1. **Peds obey the signal:** in a hermetic test, a ped routed across a signalized crossing has, in its
   timeline, a Pause at the kerb, and its on-crossing interval lies entirely within a walk window of that
   crossing's TL program. A ped at an unsignalized crossing has no such wait.
2. **No car drives through a ped:** in the `--live-city` run, sample car positions vs. ped-occupied
   crossings — zero cars traverse a crossing while a ped is on it (the metric that today is non-zero).
3. **Traffic still flows** (cars aren't needlessly blocked at signalized crossings by phantom jaywalkers —
   the gate skips signalized crossings).
4. **Determinism + parity:** two runs byte-identical; full `dotnet test` green; hash unmoved.

## 6. Tasks
- [ ] **P2b-T1 — `CrosswalkSignalSchedule`** (walk-window / `NextWalkStart`) + hermetic test on POC-0's
      signalized west crossing (known program).
- [ ] **P2b-T2 — wait insertion** in `BuildLivelyTimeline` behind `CrosswalkSignals` (inert default) +
      test (success condition 1).
- [ ] **P2b-T3 — gate class-awareness:** `CrossingOccupancySource` skips signalized crossings; unsignalized
      upgraded to a tunneling-proof stop-line (crossed-lane mapping).
- [ ] **P2b-T4 — wire into `BuildLiveCity`** + verify success conditions 2–4 (no drive-through, flow,
      determinism, gate green).

## 7. Out of scope
- Actuated-TL compliance (none in the box; falls back to the gate).
- Non-crossing ped signals / vehicle-actuated ped calls. City3D (Phase 3).
