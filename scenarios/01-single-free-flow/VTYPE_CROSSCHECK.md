# vType resolved-defaults cross-check — rung 1 (default passenger)

**Purpose.** Settle *where authoritative resolved vType defaults come from* before Task 2/3
build on them. `--save-state` does **not** expand implicit vType defaults (it echoes only
attributes explicitly set in `rou.xml` — here just `sigma="0"`; see `golden.state.xml`), so
the resolved defaults must come from another source. We cross-check two independent methods
once; on agreement we trust the empirical dump going forward.

**Result: the two methods agree on every parameter.** The empirical (libsumo/TraCI) dump is
therefore validated as the authoritative resolved-defaults source; the offline source-read is
the fallback when SUMO is unavailable.

SUMO version: `1.20.0` (vendored source and pip binary both at this pin).

## Method A — offline source-read (no SUMO)

Read straight from the vendored source at `sumo/` (the docs' `/sumo/` shorthand). The default
passenger type is `SVC_PASSENGER`, whose car-following model is Krauss and whose values come
from the `SVC_PASSENGER`/default branches of the vClass defaults tables.

| param            | value        | source (file:line) & note |
|------------------|--------------|---------------------------|
| vClass           | passenger    | scenario declares `vClass="passenger"` |
| carFollowModel   | Krauss       | `SUMOVTypeParameter.cpp:331` `cfModel(SUMO_TAG_CF_KRAUSS)` |
| length           | 5.0 m        | `SUMOVehicleClass.cpp` `getDefaultVehicleLength` default branch `return 5;` |
| minGap           | 2.5 m        | `SUMOVTypeParameter.cpp:61` `minGap(2.5)` (passenger does not override) |
| maxSpeed         | 55.5556 m/s  | `SUMOVTypeParameter.cpp:63` `maxSpeed(200. / 3.6)` (passenger does not override) |
| accel            | 2.6 m/s²     | `SUMOVTypeParameter.cpp` `getDefaultAccel` default branch `return 2.6;` |
| decel            | 4.5 m/s²     | `SUMOVTypeParameter.cpp` `getDefaultDecel` default branch `return 4.5;` |
| emergencyDecel   | 9.0 m/s²     | `getDefaultEmergencyDecel` default option → `MAX2(decel 4.5, vcDecel 9.0)` = 9.0 |
| apparentDecel    | 4.5 m/s²     | `MSCFModel.cpp:61` `getCFParam(SUMO_ATTR_APPARENTDECEL, myDecel)` → defaults to decel |
| sigma            | 0.5          | `SUMOVTypeParameter.cpp` `getDefaultImperfection` default branch `return 0.5;` |
| tau              | 1.0 s        | `MSCFModel.cpp:63` `getCFParam(SUMO_ATTR_TAU, 1.0)` |
| speedFactor mean | 1.0          | `SUMOVTypeParameter.cpp:317` `speedFactor("normc", 1.0, 0.0, 0.2, 2.0)` |
| speedFactor dev  | 0.1          | `SUMOVTypeParameter.cpp:272` passenger sets `speedFactor.getParameter()[1] = 0.1` |
| width            | 1.8 m        | `SUMOVTypeParameter.cpp:65` `width(1.8)` |
| height           | 1.5 m        | `SUMOVTypeParameter.cpp:66` `height(1.5)` |

## Method B — empirical libsumo/TraCI dump (SUMO required)

`scripts/dump-vtype-defaults.py` builds a throwaway scenario whose single `<vType>` declares
only `vClass="passenger"` (no overrides), loads it (libsumo preferred, TraCI fallback — this
run used TraCI, as the eclipse-sumo 1.20.0 wheel ships no `libsumo` Python module), inserts a
probe vehicle, and reads the resolved type parameters via the `vehicletype` getters. Output is
committed at `golden.vtype.json`.

## Cross-check

| param          | source-read | libsumo/TraCI dump | agree |
|----------------|-------------|--------------------|-------|
| length         | 5.0         | 5.0                | ✅ |
| minGap         | 2.5         | 2.5                | ✅ |
| maxSpeed       | 55.5556     | 55.55555555555556  | ✅ |
| accel          | 2.6         | 2.6                | ✅ |
| decel          | 4.5         | 4.5                | ✅ |
| emergencyDecel | 9.0         | 9.0                | ✅ |
| apparentDecel  | 4.5         | 4.5                | ✅ |
| sigma          | 0.5         | 0.5                | ✅ |
| tau            | 1.0         | 1.0                | ✅ |
| speedFactor    | 1.0         | 1.0                | ✅ |
| speedDeviation | 0.1         | 0.1                | ✅ |
| width          | 1.8         | 1.8                | ✅ |
| height         | 1.5         | 1.5                | ✅ |
| vClass         | passenger   | passenger          | ✅ |

**No discrepancies.** The shipped source and what libsumo/TraCI resolves are identical for the
default passenger type — no "source-vs-shipped drift" for these parameters at this version.

## Scenario-specific overrides (NOT in golden.vtype.json)

`golden.vtype.json` records the **pure vClass defaults**. The rung-1 scenario deliberately
overrides two of them for determinism, in its own inputs (not in the defaults dump):

- `rou.rou.xml`: `sigma="0"` (Krauss dawdling off → deterministic).
- `config.sumocfg`: `default.speeddev="0"` → speedFactor deviation forced to 0, so the drawn
  speedFactor is exactly 1.0.

So the engine, loading the rung-1 inputs, must resolve the passenger defaults above and then
apply these two overrides: effective `sigma=0`, effective `speedFactor=1.0` (deviation 0).

## Task 3 init cross-check

Before running the rung-1 trajectory parity test, diff the engine's resolved passenger vType
defaults against `golden.vtype.json` (with the scenario's `sigma=0` / `speeddev=0` overrides
applied) as a fast fail — this separates init bugs from algorithm bugs.
