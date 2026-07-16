# Sub-area traffic experiment — findings, recommendation & port gap map

**Status: pipeline proven end-to-end in pure SUMO (synthetic net).** Exploratory session.

Goal (A): a pipeline for "user selects a ~3×3 km sub-area of a bigger network → believable SUMO
traffic in that box, fast (ideally instant, ≤10 min worst case)", with the **hard rule: no visible
cheating** — cars appear/disappear only at the network **FRINGE** (roads cut by the boundary) or at
internal **SINKS** (parkingArea). No popping on visible roads.

Goal (B): map what our C# port (**SumoSharp**) is missing to drive this workflow (recorded for later;
we are **not** implementing port features this session).

All SUMO usage is **authoring/investigation only** (CLAUDE.md allows it); nothing here is a
`dotnet test` dependency. Reproduce with `experiments/subarea/run-experiment.sh` +
`experiments/subarea/auto_parking.py`. Generated nets/routes live in `experiments/subarea/scratch/`
(gitignored — only the scripts and this doc are committed).

---

## TL;DR — the recommendation

**Use L2 "macro-crop" + automated parking sinks. There is essentially no simplicity/speed-vs-
believability tradeoff to make**, because the cost splits cleanly:

- **Once per macro map (slow, reused):** obtain/generate believable demand on the big network and
  run it once with per-edge exit times. ~19 s on the synthetic 30×30 macro (3852 vehicles).
- **Per sub-area selection (instant):** crop the net + cut+re-time the routes + auto-generate the
  parking layer = **~3.3 s total** on the synthetic net (crop 1.1 s, cutRoutes 2.0 s, parking 0.2 s).
  Comfortably "instant", and orders of magnitude under the 10-min worst case even once a real Geneva
  net is 10× larger.

The believability win (local trips that park/emerge instead of popping) costs **0.2 s** and is
**fully automated** from the cut routes — so you do not trade speed for it. The only genuine upstream
cost is the *quality of the macro demand* (synthetic randomTrips here; the user's tuned Geneva
scenarios or an L3 OD model later), and that is a one-time, per-map concern independent of sub-area
selection.

L1 "weighted random on a standalone grid" is a **dead end** for this goal (see below): a standalone
generated grid has no fringe at all, so it cannot honor the fringe-only rule. The fringe is *created
by cropping*, which is L2 — so even the "simple" path is L2. L3 (procedural OD) is not needed to hit
the believability bar on this evidence.

---

## Environment (this VM)

| Tool | Provisioning (ephemeral, VM-volatile) |
|---|---|
| SUMO 1.20.0 — `sumo`, `netgenerate`, `netconvert`, `duarouter`, `od2trips` | `pip install eclipse-sumo==1.20.0` → `$SUMO_HOME/bin` |
| `randomTrips.py`, `route/cutRoutes.py`, `sumolib` | `$SUMO_HOME/tools/…` (set `PYTHONPATH`) |
| .NET 8 SDK (to run the port) | `apt-get install -y dotnet-sdk-8.0` (8.0.129) |

Neither is committed or required by the offline test loop.

---

## The approach ladder — what held up

### L1 "weighted random" — cannot satisfy no-cheating on a raw grid

`netgenerate --grid` is a **closed** network: every node has matched in/out degree (corners 2/2,
edges 3/3, interior 4/4), so it has **zero fringe edges** (`sumolib is_fringe() == 0/360`).
`randomTrips --fringe-factor` is therefore a no-op and **100 %** of trips depart *and* arrive on
internal roads → pure popping (audited: 2185/2185 off-fringe both ends). **A fringe does not exist
until you cut a box out of a bigger net** — which is L2.

### L2 "macro-crop" — the workable pipeline ✅

Exact commands (full driver in `run-experiment.sh`):

```bash
# macro map (done once); the standalone box is only used to demonstrate L1's failure
netgenerate --grid --grid.number=30 --grid.length=300 -o synth_macro.net.xml     # 8700x8700 m

# (1) CROP the box — THIS creates the fringe (cut edges become dangling entry/exit stubs)
netconvert -s synth_macro.net.xml --keep-edges.in-boundary 2850,2850,5850,5850 -o sub.net.xml
#     -> 440 edges, 80 is_fringe()==True   (was 0 before the cut)

# (2) demand on the FULL macro, routed, dumped WITH per-edge exit times (cutRoutes needs them)
python3 randomTrips.py -n synth_macro.net.xml -r macro.rou.xml --period 0.8 --end 3600 --validate
sumo -c macro.sumocfg --vehroute-output macro.vehroutes.xml --vehroute-output.exit-times

# (3) CUT demand into the box; departures re-timed to when each car reaches the boundary
python3 route/cutRoutes.py sub.net.xml macro.vehroutes.xml \
        --routes-output sub.rou.xml --orig-net synth_macro.net.xml
#     -> 3852 macro vehicles -> 1608 kept (those crossing the box)
```

**Demand decomposition (the key structural insight), from the cropped box:**

| | count | share | handled by |
|---|---|---|---|
| through-traffic (fringe→fringe) | 1195 | ~73 % | fringe, for free — clean already |
| internal ORIGIN (pops in) | 433 | ~27 % | **parking sink** (pull-out) |
| internal DESTINATION (pops out) | 413 | ~26 % | **parking sink** (pull-in + stay) |
| internal on BOTH ends | 48 | ~3 % | parking on both ends |

Through-traffic inherits realistic flow phasing for free (re-timed to boundary arrival). The internal
O/D share is **not a bug** — it is the demand that *must* be absorbed by internal sinks, and the audit
quantifies how much (~1/4 of demand here).

### L3 "procedural OD" — not needed

`od2trips` is present if we escalate, but L2 already meets the believability bar. Deferred.

---

## Parking sinks — the no-cheating half, fully automated ✅

`experiments/subarea/auto_parking.py` turns the cut routes into a **no-popping** scenario with zero
manual work (input: cropped net + cut routes; output: a parkingArea additional-file + rewritten
routes). Rules it applies:

- **Internal destination** → append `<stop parkingArea=… duration=100000/>` (outlasts the sim): the
  car pulls off the running lane into a lot and **stays** — a believable sink, never a mid-road vanish.
- **Internal origin** → `departPos="stop"` + a short leading parkingArea stop: the car is inserted
  **already parked** (off-road) and pulls out into traffic — a believable source.
- One `<parkingArea>` per internal-endpoint edge (lane `_0`), `roadsideCapacity` sized to that edge's
  demand. (A `<rerouter>` overflow-to-adjacent variant is a later refinement.)

Run: 331 parkingAreas, 798 vehicles rerouted to parking, **zero SUMO warnings**.

**No-cheating audit of the parking-enabled run:**

| check | result |
|---|---|
| through-trips that arrived on a running lane | 1195, **0 off-fringe** ✅ |
| non-parking vehicles first appearing on an internal lane | **0** ✅ (every internal appearance is a car in a parking area, off-road) |
| all 1608 vehicles accounted for | ✅ (433 pull-out origins, 413 park-and-stay destinations) |

→ **Every appearance/disappearance on a visible road is at the fringe. All internal births/deaths
happen off-road inside a parkingArea.** The hard rule is met.

**Believability refinements noted (not blocking):** dest cars currently park forever (lots fill and
stay — fine as a sink; add turnover / finite dwell + a `<rerouter>` for overflow for extra realism);
`roadsideCapacity` is sized exactly to demand (no overflow modeled yet).

---

## Timing (why "instant" holds)

| stage | when | cost (synthetic net) |
|---|---|---|
| macro sim + exit-times | once per macro map | 18.7 s |
| crop (`netconvert`) | per box | 1.1 s |
| `cutRoutes` | per box | 2.0 s |
| `auto_parking.py` | per box | 0.2 s |
| **per-box total** | | **~3.3 s** |

Density is tuned independently of the macro via box size and macro `--period` (headway); a denser box
than the full map = smaller window / shorter period, without touching the macro run.

---

## Port (SumoSharp) gap map — recorded for later

Fed the cropped box (through-traffic only, no parking) to `Sim.Run`: presence parity is **exact** —
1608 vehicles, 3600 steps, peak 136 concurrent, identical to SUMO — **once the parser blockers below
are worked around**. So the macro-crop pipeline already runs through the port for through-traffic; the
parking-sink half does not, and is the bulk of the gap.

Priority order (blockers first). Layer = Ingest (parser) vs Core (engine).

| # | Gap | Layer | Severity | Evidence / note |
|---|---|---|---|---|
| **G1** | **Symbolic depart attrs rejected.** `departSpeed="max"`, `departLane="best"`, and (parking) `departPos="stop"` throw `FormatException` in `DemandParser.ParseNullableDouble/Int`. | Ingest | **Blocker** | `cutRoutes` emits `departSpeed="max"`/`departLane="best"` on *every* cut vehicle; `departPos="stop"` is exactly what origin-parking needs. SUMO's symbolic vocab: speed `max/desired/speedLimit/last/avg/random`; lane `best/free/random/allowed/first`; pos `random/free/base/last/stop/splitFront`. Port accepts only numerics. Worked around by `sed`-stripping. |
| **G2** | **No additional-file handling at all.** The sumocfg `<additional-files>` is never read; `<parkingArea>`, `<rerouter>`, detectors are invisible to Ingest (`grep`: zero `additional` handling). | Ingest | **Blocker (parking)** | The entire internal-sink half has no ingestion path. Needs: read `<additional-files>` from the cfg, parse `<parkingArea id lane startPos endPos roadsideCapacity>`. |
| **G3** | **`<stop>` support is lane-only.** Parser reads `lane/startPos/endPos/duration`; `parkingArea=`, `busStop=`, `triggered`, `until`, waypoint (`speed>0`) explicitly out (`DemandParser.cs:78-80`). Engine mirror `StopRuntime` models only lane stops. | Ingest + Core | **Blocker (parking)** | Need `<stop parkingArea=… duration=…>` in both parser and engine. |
| **G3b** | **Parked vehicle must be OFF the running lane.** A parkingArea stop pulls the car off the carriageway (lateral offset); it must not act as a lane leader/obstacle while parked, and must re-merge on pull-out. Today's lane `<stop>` keeps the vehicle *on* the lane. | Core (semantics) | **Blocker (parking)** | This is the behavior that makes parking "not cheating" (off-road birth/death). Distinct from just parsing the stop. |
| **G4** | **No `<rerouter>` element.** Rerouting exists only as an internal Dijkstra/`ReplaceRoute` code knob, not the XML `<rerouter>` (parkingAreaReroute / overflow-to-adjacent). | Ingest + Core | Medium | Needed for auto-park + overflow; can be deferred if parking capacity is pre-sized to demand (as the generator does). |
| **G5** | **`departSpeed="max"` is a believability lever, not just a parse issue.** Fringe cars are through-traffic already moving; entering at `max` vs. the port's default 0 = "flows in at the boundary" vs. "materializes stopped at the boundary". | Core (semantics) | Medium | Fold into G1's fix: resolve symbolic `departSpeed`, at least `max`/`speedLimit`. |
| — | **No native crop/cut/park seam.** bbox → crop net → cut+re-time routes → map internal O/D to parking currently *must* shell out to `netconvert` + `cutRoutes` + `auto_parking.py`. The port has no authoring API for this. | design | — | Open question: is a SumoSharp-native cropping/authoring seam worth building, or does it stay a SUMO-tool preprocessing step feeding the port? |

**Bottom line for the port:** through-traffic sub-areas work today modulo G1. Delivering the *insisted-
on parking-sink believability* requires **G2 + G3 + G3b** (parse additional-files + parkingArea, parse
parkingArea stops, and represent a parked vehicle off-lane), with G1 (`departPos="stop"`) and G4
(`<rerouter>`) alongside. G3b is the load-bearing engine behavior; the rest is parsing.

---

## Reproduce

```bash
python3 -m pip install "eclipse-sumo==1.20.0"     # ephemeral
apt-get install -y dotnet-sdk-8.0                  # ephemeral, to run the port
bash experiments/subarea/run-experiment.sh         # L1 demo + full L2 pipeline + port run
# parking-sink layer (pure SUMO):
source experiments/subarea/env.sh
export PYTHONPATH="$SUMO_HOME/tools"
cd experiments/subarea/scratch
python3 ../auto_parking.py sub.net.xml sub.rou.xml sub_parking.add.xml sub_parking.rou.xml
sumo -c sub_parking.sumocfg --fcd-output sub_parking.fcd.xml --tripinfo-output sub_parking.tripinfo.xml
```

## Next steps

1. (Optional) `<rerouter>`-based overflow + finite parking dwell for extra realism / turnover.
2. Run the same pipeline on the user's real manually-tuned Geneva net + scenarios (the pipeline already
   takes an arbitrary net + routes + bbox).
3. When we choose to close the port gaps, start with G2/G3/G3b (parking ingestion + off-lane parked
   semantics) — that is what unlocks the parking-sink believability through SumoSharp.
