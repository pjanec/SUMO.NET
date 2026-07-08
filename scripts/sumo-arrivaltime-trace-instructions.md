# Instructions: capture the SUMO 1.20.0 `MSLink::opened`/`blockedByFoe` arrival-time trace

You are running in your **existing `pjanec/sumo` debug clone** (the one that already built the DEBUG
binary for the C3 minor-link and C4-iv merge traces). This reuses that build with **two debug
defines enabled** and captures **one short trace**.

## Why

The Traffic project has ported the sameTarget-merge *follow* (a vehicle follows a foe already **on**
the merge). What's still missing is the **junction arrival-time right-of-way**: a vehicle entering a
priority junction must **stop-line yield to a higher-priority foe that is still APPROACHING** the
conflict (not yet on it). SUMO decides this in `MSLink::opened` → `MSLink::blockedByFoe`
(`src/microsim/MSLink.cpp:747-1013`) by comparing **arrival-time windows**: ego is blocked iff the
foe's `[arrivalTime, leavingTime]` overlaps ego's `[arrivalTime, leaveTime]` within a `lookAhead`.
The reservations are filled by `MSVehicle::setApproaching` (`getArrivalTime`/`getLeaveTime`) during
`planMove`.

A **blanket** "stop for any approaching foe" over-yields when the foe is far (it breaks the C3
onramp, where the mainline is distant), so the port needs the **exact arrival/leave times and the
block decision** per step. Those are runtime values that defeat static reading — hence this trace.

The exact scenario is the committed **two-vehicle roundabout** (`scenarios/32-roundabout`): `vWest`
circulates through node `RS` while `vSouth` enters at `RS` toward the same exit and must yield.
`vWest`'s trajectory already matches the C# engine exactly; only `vSouth`'s yield needs this trace.

**Do not push to `main`. Do not open a PR.** Commit the log to a branch and report back.

---

## Step 0 — Start from the existing debug branch

```bash
cd <your pjanec/sumo checkout>
git checkout debug/c4iv-merge-trace     # or debug/c3-minor-link-trace — whichever has the DEBUG build
git switch -c debug/arrivaltime-trace
```

---

## Step 1 — Enable the opened/blockedByFoe prints, gate to `vSouth`

Two files.

### 1a. `src/microsim/MSLink.cpp` — turn ON `MSLink_DEBUG_OPENED` (near line 46)

Find:

```cpp
//#define MSLink_DEBUG_OPENED
```

and remove the `//`:

```cpp
#define MSLink_DEBUG_OPENED
```

### 1b. `src/microsim/MSVehicle.cpp` — keep `DEBUG_PLAN_MOVE` ON (near line 90)

From the prior builds this should already read (leave it as-is; uncomment if not):

```cpp
#define DEBUG_PLAN_MOVE
```

`DEBUG_PLAN_MOVE` is what flips `gDebugFlag1 = true` around the link-approach loop
(MSVehicle.cpp:3570 `gDebugFlag1 = true; // See MSLink_DEBUG_OPENED`), which is the flag the
`MSLink_DEBUG_OPENED` prints are gated on.

### 1c. `src/microsim/MSVehicle.cpp` — gate `DEBUG_COND` to `vSouth` (near line 108)

Change the `DEBUG_COND` define to the yielding vehicle:

```cpp
#define DEBUG_COND (getID() == "vSouth")
```

> `vSouth` is the entering vehicle that must yield to the circulating `vWest`. Gating to it keeps the
> log small and puts `gDebugFlag1` on only while `vSouth` processes the `RS` junction link.

---

## Step 2 — Rebuild the `sumo` binary

```bash
cmake --build build -j"$(nproc)" --target sumo
```

(Use whatever Debug binary path your prior builds produced — `build/bin/sumo` or the source-tree
`bin/sumoD`.)

---

## Step 3 — Scenario data (self-contained; also in the attached zip)

Unzip the attached `arrivaltime-trace.zip` into a fresh dir, **or** write these files by hand. It is
the exact committed `scenarios/32-roundabout` inputs (a single-lane priority roundabout, radius 20,
ring priority 10 > approach priority 1). `RUN.sh` runs it and greps the trace.

```
arrivaltime/
  nodes.nod.xml
  edges.edg.xml
  connections.con.xml
  config.sumocfg
  rou.rou.xml
  RUN.sh
```

`RUN.sh` (edit `SUMO=` / `NETCONVERT=` to your Debug binaries if needed):

```bash
#!/usr/bin/env bash
set -euo pipefail
SUMO=${SUMO:-../build/bin/sumo}
NETCONVERT=${NETCONVERT:-../build/bin/netconvert}
"$NETCONVERT" --node-files nodes.nod.xml --edge-files edges.edg.xml \
    --connection-files connections.con.xml --output-file net.net.xml --no-turnarounds
"$SUMO" -c config.sumocfg --fcd-output fcd.xml --precision 6 2>&1 | tee arrivaltime.log
echo "=== grep the decision lines ==="
grep -nE "opened\?|blocked|aT=|foeArrival|leaveTime|lookAhead|approaching link|arrivalTime|setApproaching" arrivaltime.log | head -80
```

Run it:

```bash
cd arrivaltime && bash RUN.sh
```

---

## Step 4 — Verify the trace has what I need, then commit

The key window is roughly **t=15..21** (as `vSouth` nears `RS` at pos ~171 and `vWest` circulates
through). The log must contain, per step for `vSouth`'s `RS` link:

- the `approaching link=...` line (from `DEBUG_PLAN_MOVE`) with `seen=`, `arrivalTime`,
  `arrivalSpeed`, `arrivalSpeedBraking`, `leaveSpeed`;
- the `opened? link=... red=... havePrio=...` line;
- the `blocked by <vWest> arrival=<egoAT> foeArrival=<foeAT>` line **and** the `blockedByFoe`
  detail block: `foeVeh=vWest req=<willPass> aT=<foeArrivalTime> lT=<foeLeavingTime>` and
  `imp=<impatience> fAT2=... fASb=... lA=<lookAhead> egoAT=<arrivalTime> egoLT=<leaveTime>
  egoLS=<leaveSpeed>` and the `blocked (cannot follow|cannot lead|hard conflict)` verdict.

Grep to confirm before committing:

```bash
grep -nE "blockedByFoe|blocked \(|foeVeh=vWest|aT=|lA=|egoAT=|egoLT=|opened\?|approaching link" arrivaltime/arrivaltime.log | head -80
```

If those `blockedByFoe` / `foeVeh=vWest` lines are **absent**, the gate didn't take — recheck Step 1
(both `#define MSLink_DEBUG_OPENED` AND `#define DEBUG_PLAN_MOVE` uncommented, and `DEBUG_COND` =
`getID() == "vSouth"`), rebuild, rerun. Do not commit an empty log.

Then commit:

```bash
git add src/microsim/MSLink.cpp src/microsim/MSVehicle.cpp arrivaltime/
git commit -m "debug: enable MSLink_DEBUG_OPENED, capture arrival-time opened/blockedByFoe trace (vSouth)"
git push -u origin debug/arrivaltime-trace
```

---

## Deliverable — report back

1. the branch + commit SHA;
2. the committed log path (`arrivaltime/arrivaltime.log`);
3. an inline paste of the `opened?` + `blockedByFoe` (`aT=`, `foeArrival`, `lT=`, `lA=`, `egoAT=`,
   `egoLT=`, and the `blocked (...)` verdict) lines for `vSouth` across **t=15..21**.

Those give me the exact per-step `arrivalTime`/`leaveTime`/`lookAhead`/foe-arrival and the block
decision, which let me port `MSLink::opened`/`blockedByFoe` + the approach-reservation to the 1e-3
parity bar and land the two-vehicle roundabout anchor (`scenarios/32-roundabout`).
