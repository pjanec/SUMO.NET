# Instructions: capture the SUMO 1.20.0 actuated-TLS trace (C6-ii)

You are running in your **existing `pjanec/sumo` debug clone** (the one that built the DEBUG binary
for the C3 / C4-iv / arrival-time / keepClear traces). This reuses that build with **two debug
defines enabled** and captures **one short trace**.

## Why

The Traffic project is porting **actuated (detector-driven) traffic lights** (C6-ii). Unlike a
static program (a pure function of time, already ported), an actuated program is STATEFUL: each
green phase EXTENDS while an induction-loop detector keeps seeing vehicles (detector gap < max-gap)
and ENDS when the gap opens, bounded by minDur/maxDur. The mechanism is
`MSActuatedTrafficLightLogic::trySwitch`/`gapControl`/`duration`
(`src/microsim/traffic_lights/MSActuatedTrafficLightLogic.cpp:710/834/814`), fed by `MSInductLoop`
detectors placed at `laneLength - inductLoopPosition`.

The engine has none of this: no detector model, no per-TLS phase state, no per-step switch logic. Two
things defeat static reading and need the instrumented prints:

1. the **detector placement** per lane (`ilpos` / `inductLoopPosition` / `detLength`), and
2. the **per-step `gapControl` / `duration` decision** (`actualGap` per detector vs `maxGap`, the
   returned `detectionGap`, and the extended `duration`).

The scenario is the committed `scenarios/35-actuated-tls`: a single actuated junction J (2 green
phases Gr/rG, minDur 5 / maxDur 50, + 3 s yellows). Four N-S vehicles stream over the SJ detector so
phase 0 (SJ green) extends from minDur 5 to t=13 before ending; ew0 waits at the WJ red and is
released when phase 2 turns green at t=16. The committed `phase-timeline.txt` is the target phase
sequence.

**Do not push to `main`. Do not open a PR.** Commit the log to a branch and report back.

---

## Step 0 — Start from the existing debug branch

```bash
cd <your pjanec/sumo checkout>
git checkout debug/keepclear-trace     # or any branch with the DEBUG build
git switch -c debug/actuated-tls-trace
```

---

## Step 1 — Enable the actuated-TLS prints, gate to junction `J`

Edit **`src/microsim/traffic_lights/MSActuatedTrafficLightLogic.cpp`**. Three changes near the top
(lines 42-44):

```cpp
#define DEBUG_DETECTORS
#define DEBUG_PHASE_SELECTION
#define DEBUG_COND (getID()=="J")
```

(uncomment the first two `//#define` lines, and change the `DEBUG_COND` id from `"C"` to `"J"`.)

---

## Step 2 — Rebuild

```bash
cmake --build build -j"$(nproc)" --target sumo
```

(Use whatever Debug binary path your prior builds produced — `build/bin/sumo` or `bin/sumoD`.)

---

## Step 3 — Scenario data (self-contained; also in the attached zip)

Unzip the attached `actuated-tls-trace.zip` into a fresh dir (it is the exact committed
`scenarios/35-actuated-tls` inputs), or write the files by hand. `RUN.sh` builds the net and runs:

```
actuated/
  nodes.nod.xml  edges.edg.xml  connections.con.xml  config.sumocfg  rou.rou.xml  add.add.xml  RUN.sh
```

`RUN.sh` (edit `SUMO=`/`NETCONVERT=` to your Debug binaries if needed):

```bash
#!/usr/bin/env bash
set -euo pipefail
SUMO=${SUMO:-../build/bin/sumo}
NETCONVERT=${NETCONVERT:-../build/bin/netconvert}
"$NETCONVERT" --node-files nodes.nod.xml --edge-files edges.edg.xml \
    --connection-files connections.con.xml --output-file net.net.xml \
    --no-turnarounds --tls.default-type actuated
"$SUMO" -c config.sumocfg --additional-files add.add.xml --fcd-output fcd.xml --precision 6 \
    2>&1 | tee actuated.log
echo "=== detector placement + phase decisions ==="
grep -nE "inductLoop|ilpos|detector|p=[0-9]+ .*dGap=|actDuration|maxDur|maxGap|newDuration|duration=" actuated.log | head -80
```

Run it:

```bash
cd actuated && bash RUN.sh
```

> The `add.add.xml` writes `tls-states.xml` (the phase-per-time timeline) as an independent check
> that your build reproduces the committed `phase-timeline.txt` (phase 0 green t=0..12, phase 2 green
> t=16..20).

---

## Step 4 — Verify the trace has what I need, then commit

The log must contain:

- **Detector placement** (from `DEBUG_DETECTORS`, at init): for each green lane (SJ_0, WJ_0) the
  induction-loop id, its lane, `ilpos` (position from the lane start) / `inductLoopPosition` (from
  the stop line) and `detLength`.
- **Per-step phase decision** (from `DEBUG_PHASE_SELECTION`, window t=0..16): the
  `SIMTIME p=<phase> trySwitch dGap=<detectionGap>` lines, the `gapControl` detail (per detector
  `actualGap` = `getTimeSinceLastDetection` vs `maxGap`, `isJammed`), and the `duration()` result
  (`actDuration`, `minDur`, `myDetectorGap`, `newDuration`, the maxDur/latest cap).

Grep to confirm before committing:

```bash
grep -nE "ilpos|inductLoopPosition|detLength|p=[0-9]+ .*dGap=|actualGap|getTimeSinceLastDetection|newDuration|actDuration=|maxDur=" \
  actuated/actuated.log | head -80
```

If those lines are **absent**, the gate didn't take — recheck Step 1 (both `#define DEBUG_DETECTORS`
and `#define DEBUG_PHASE_SELECTION` uncommented, and `DEBUG_COND` = `getID()=="J"`), rebuild, rerun.
Do not commit an empty log.

Then commit:

```bash
git add src/microsim/traffic_lights/MSActuatedTrafficLightLogic.cpp actuated/
git commit -m "debug: enable DEBUG_DETECTORS + DEBUG_PHASE_SELECTION, capture actuated-TLS trace (J)"
git push -u origin debug/actuated-tls-trace
```

---

## Deliverable — report back

1. the branch + commit SHA;
2. the committed log path (`actuated/actuated.log`);
3. an inline paste of (a) the detector-placement lines for SJ_0 and WJ_0, and (b) the per-step
   `p=0 ... dGap=` + `duration`/`newDuration` lines across **t=0..13** (the phase-0 extension) and the
   phase-2 lines t=16..20.

Those give me the exact detector positions and the per-step gap/duration decisions, which let me port
the induction-loop detector model + the actuated phase state machine (trySwitch/gapControl/duration)
to the 1e-3 parity bar and land `scenarios/35-actuated-tls`.

## Note on port scope (for context, not for the trace)

The port itself is large and stateful (unlike the pure-function static TLS): it needs (1) an
induction-loop detector model with `getTimeSinceLastDetection` updated each step from vehicle
positions, (2) per-TLS runtime state (`myStep` / `myLastSwitch`), (3) a new per-step system phase that
updates detectors and runs `trySwitch`, and (4) parser support for `type="actuated"` + per-phase
`minDur`/`maxDur`. The trace pins the detector/gap numbers; the architecture is the bulk of the work.
