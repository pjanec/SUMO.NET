# C4-vii remaining work — bootstrap for the next session

**Read `CLAUDE.md`, `DESIGN.md`, and the `C4-vii` block in `TASKS.md` first.** This doc is the
self-contained handoff for the two *unfinished* C4-vii sub-bugs. Both turned out to be **structural
right-of-way (RoW) core ports**, each roughly a full rung with high regression risk — not gate fixes.

## What is already DONE (on `main`, do not redo)

| Piece | Commit | What it does |
|---|---|---|
| C4-vii-b | `4d7e2d9` | Keep-right over-accumulation + final-edge arrival strand (vehicles no longer freeze at lane ends). Parity-reviewer ACCEPTED. Anchor `scenarios/45-multilane-keepright-arrival`. |
| Crash fix | `eac0a5b` | C4-vii-b's keep-right crashed on any multi-lane junction (`ComputeBestLanes` on an internal edge). Guarded: no keep-right on internal lanes. |
| Diagnostics | this doc's commit | `scenarios/_diag/c4vii-willpass-grid/` (the reproduction) + `C4viiWillpassGridDiagTests` (crash regression + C4-vii-c stuck-count guard). |

**Invariants for anything you commit** (CLAUDE.md rule 3): committed suite stays green
(currently **162 passed + 1 skipped**), `Sim.Bench` determinism hash **unchanged**
(`7291978050025285112`), a non-vacuous anchor, and a **parity-reviewer ACCEPT** before promoting to
`main`. Build the anchor + golden FIRST, then instrument + fix.

## Golden regeneration essentials (network side, ends in a commit)

SUMO 1.20.0 is at `/usr/local/bin/sumo`; vendored source at `sumo/`. Per-scenario golden command
(the committed anchors all use this):

```
sumo -c config.sumocfg --fcd-output golden.fcd.xml --fcd-output.acceleration --precision 6 \
     --save-state.times 1 --save-state.files golden.state.xml --no-step-log true
python3 scripts/dump-scenario-vtypes.py config.sumocfg golden.vtype.json
```

**Determinism flags every committed scenario/config MUST set** (a missing one silently diverges —
this bit me twice this session): in `<processing>`:
`<step-method.ballistic value="false"/>`, `<default.action-step-length value="1"/>`,
`<default.speeddev value="0"/>`, `<time-to-teleport value="-1"/>`; plus `<step-length value="1"/>`
and a `<random_number><seed value="42"/></random_number>`. `sigma="0"` on the vType.

`tolerance.json`: `{"parityMode":"exact","comparedAttributes":["lane","pos","speed"],"pos":0.001,"speed":0.001}`.
`provenance.txt`: mirror an existing scenario's (sha256 of every input+golden, sumo_version=1.20.0).

---

## C4-vii-c — willPass PRE-PASS (the -L2 flow blocker; highest impact)

### Symptom & reproduction (committed, deterministic)
`scenarios/_diag/c4vii-willpass-grid/` (6×6 -L2 priority grid, 75 trips). **SUMO: 0 of 75 stuck.
Engine: ~40 stuck.** `C4viiWillpassGridDiagTests` runs it and guards `stuck <= 45`. (The TLS variant
`gc.sumocfg` in the old scratch behaved the same; priority is the cleaner repro — no signal timing.)
Regen recipe if you want a fresh/smaller one:
`netgenerate --grid --grid.number=6 --grid.length=250 -L 2 --seed 7` (priority; add `--tls.guess`
for TLS) + `randomTrips.py -n net -e 300 -p 4 --fringe-factor 10 --min-distance 500 --seed 7`
+ `duarouter --named-routes --ignore-errors`.

### Root cause (instrumented this session — do not re-derive)
Ego yields forever to a foe that is itself yielding. SUMO's `MSLink::blockedByFoe` returns false for
`!avi.willPass` (`sumo/src/microsim/MSLink.cpp:935`); `willPass` = `setRequest =
(vNext > NUMERICAL_EPS_SPEED && !abortRequestAfterMinor) || leavingCurrentIntersection`
(`sumo/src/microsim/MSVehicle.cpp:2732`). **The load-bearing term is `vNext` — the foe's PLANNED
speed THIS step, not its start-of-step speed.**

Instrumenting the priority grid (add a temporary `public static Dictionary<...> DebugCross` in
`Engine.cs`, record `(foeId, foe.Speed, SeenToInternalLaneEntry(foe,...), egoPos)` whenever the
crossing arm applies a finite constraint, then dump it for stuck vehicles) showed:
- A **stopped-foe proxy** (`foe.startOfStepSpeed <= NumericalEps` ⇒ willPass=false) is faithful and
  helps (**40→32**, tls 48→37) but is **insufficient**. Not committed.
- The **residual root vehicles are only ~5–8** (the other ~24 stuck are just **queued behind them**
  by car-following — fix the roots and the queues drain). Those roots yield to foes that are **close
  (seen 1–5 m) and MOVING (3–13 m/s)** — but those foes are **braking to a stop this step** (they are
  themselves yielding), so their `vNext ≈ 0` and SUMO's willPass is FALSE. The engine's frozen-
  snapshot model reads *start-of-step* speed (> 0) and yields → gridlock.

### The fix (structural): a willPass PRE-PASS
Add a phase, from the frozen start-of-step snapshot, that computes for each vehicle whether it
**intends to enter its upcoming junction link this step** (its `vNext`-at-the-link > ~0). Then the
crossing approaching-foe arm of `JunctionYieldConstraint` (`Engine.cs`, the
`foeInternalSeqIndex > foe.LaneSeqIndex` branch, ~line 1737 — where `foeStoppedAtStopLine` was
prototyped) blocks ego **only** on a foe whose willPass is true.

- Break the circularity the way SUMO does (`setApproaching` runs before `opened()`): the pre-pass
  computes each vehicle's intended entry **without** the foe-willPass refinement — one level of
  approximation. Practically, `willPass(foe) ≈ foe's planned CF/cautious-approach speed at its own
  stop line > NUMERICAL_EPS_SPEED`. The engine already computes each vehicle's `vNext` in
  `PlanMovements`; the pre-pass is essentially "plan speeds ignoring foe-willPass, cache them, then
  do the yield decisions reading the cache."
- The **visibility case** folds in for free: a moving foe beyond its own minor link's 4.5 m
  foe-visibility (`abortRequestAfterMinor`, `MSVehicle.cpp:2730`) has its `vNext` limited by the
  cautious approach, so its willPass comes out false without a separate branch.
- ECS/zero-alloc (DESIGN.md): the willPass result is one bool per vehicle — store it on
  `VehicleRuntime` (like `KeepRightProbability`), written once per step in the pre-pass, read in the
  yield pass. No per-step allocation.

### Done-condition
Grid `stuck` drops to ~0 (tighten `C4viiWillpassGridDiagTests`); a NEW minimal FIXED-ROUTE
deterministic anchor (a small 2×2/3×3 -L2 grid or a hand-built 3–4-vehicle cycle) where SUMO flows
and the pre-fix engine gridlocks, gauged by stuck-count (a gridlocked state is not a per-step FCD
golden, so this anchor asserts stuck-count/arrival, not exact FCD); committed suite stays green;
`Sim.Bench` hash unchanged; parity-reviewer ACCEPT.

### Traps
- A single-crossroads **continuous 4-left-turn stream does NOT reproduce** it (engine+SUMO both flow,
  0 stuck) and a **dense mixed stream over-saturates** (SUMO also queues). You need multi-junction
  density or a hand-built cycle.
- Do **not** use start-of-step speed as willPass — it misses braking foes (that's why the proxy only
  got 40→32).

---

## C4-vii-a part 2 — internal-junction RoW (cont turns)

### Status
Part 1 (the cont-lane SEQUENCE fix) is on branch `claude/handoff-docs-i5a9vm` (commit `1cb6c12`),
byte-identical, NOT on main. Part 2 is open.

### Root cause (this session)
For a `cont` link (a turn split by an INTERNAL JUNCTION into two internal lanes, e.g. `NC→CE` =
`:C_3_0` then `:C_16_0`), SUMO's minor-link cautious slowdown happens at the **internal junction
`:C_16`'s conflict zone, NOT the junction entry**: the vehicle enters `:C_3_0` at full speed and
brakes hard on `:C_3_0` (golden: `13.89 → 10.04 → 5.54 → 8.14 → 9.26`) for `:C_16`'s foes. Two coupled
problems, both verified byte-identical on the suite but insufficient alone:
- **2a (lane resolution).** `JunctionYieldConstraint`'s `egoInternalLaneId` scan (`Engine.cs:~1498`)
  finds the link's internal lane `:C_16_0` (junction C's `IntLanes[3]`), so `approachLane =
  pool[idx-1]` resolves to the intermediate internal lane `:C_3_0`, not the normal lane `NC_1` → `seen`
  goes negative → the cautious arm never fires. Candidate fix: walk `approachLane` back over `:`-edge
  pool lanes to the last normal lane; set `egoOnInternal = LaneSeqIndex > approachSeqIndex`.
- **2b (speed gate).** The cautious arm gates the whole braking on `brakeDist < seen`; SUMO computes
  `stopSpeed` unconditionally and only gates the `laneStopOffset` clamp on
  `canBrakeBeforeLaneEnd = seen >= brakeDist` (`MSVehicle.cpp:2648`).
- **BUT** 2a+2b together still don't match: with `approachLane=NC_1` the engine brakes at the ENTRY
  (t=15: 12.99 vs golden 13.89), because SUMO doesn't brake at the entry at all — it brakes for the
  internal junction. **The real fix is to model each cont link's internal junction (`:C_16`, an
  `type="internal"` junction with its own `<request>`/foes) as a FIRST-CLASS minor link** with its
  own cautious approach + RoW. The 9b/C4 model assumes one internal lane + one conflict zone per link.

### Anchor
Lone `NC→CE` left turn on the `scenarios/44-multilane-junction-turn` crossroads net; config
ballistic=false + speeddev=0; SUMO golden `:C_3_0`@t17 → `:C_16_0`@t18 → `CE_1`@t19 with the dip
above. (Scratch was `scratchpad/c4vii-a/` — ephemeral; regenerate.) This is a big port for a *subset*
of junctions (multi-lane turns) — re-evaluate priority vs C4-vii-c, which unblocks -L2 flow generally.

---

## Git state at handoff
- `main` = `eac0a5b` (C4-vii-b + crash fix) — the shippable, testable state.
- Branch `claude/handoff-docs-i5a9vm` = all of main + C4-vii-a part 1 (`1cb6c12`) + the diagnosis
  commits. Decide per sub-bug whether to keep building on this branch or restart from `main`.
