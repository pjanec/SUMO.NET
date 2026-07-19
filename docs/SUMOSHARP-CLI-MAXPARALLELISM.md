# SUMOSHARP — serve CLI `--max-parallelism` (design + tasks + tracker)

**Audience:** the SumoSharp engine session. Small, well-scoped engine-CLI change that unblocks the
SumoData preprocessing **performance** sweep (`docs/SUMOSHARP-PREPROCESSING-PERF-TEST.md`, run on the
Windows box). It is **not** a correctness issue — the drop-in is already accepted and merged
(`docs/SUMOSHARP-SERVE-PATH-DROP-IN.md` GAP-1/2/3). This is a small feature, so per `CLAUDE.md` the
design and task-description are folded into this one doc; the tracker is the checklist at the end.

## WHAT (the requirement, from the incoming ask)

Expose **`--max-parallelism N`** on the `Sim.Sumo` serve CLI (`SumoShim`), reusing the exact flag name
the `Sim.BenchCity` / `Sim.BenchCrowd` / `Sim.BenchPedLod` bench tools already use.

- Semantics: `N <= 0` → all cores (`-1`, the current default, unchanged); `N >= 1` → cap to `N` threads.
- **Parsed order-independently** — must work whether it appears before or after `-c`. SumoData shells
  out as `[*SUMO_BINARY, "-c", cfg, …fixed flags…]`, so the flag rides ahead of `-c` in the
  `SUMO_BINARY` prefix (`SUMO_BINARY="dotnet …/sumosharp.dll --max-parallelism 4"`) and **SumoData needs
  no code change**.
- Genuinely-unknown flags keep the existing warn-and-ignore behaviour.
- **Hard invariant:** `--max-parallelism` is a *performance* knob, never a semantics knob. The engine
  output (fcd / summary / statistic / tripinfo) must be **byte-identical regardless of the value**. This
  is what lets the SumoData timing sweep be trusted and protects the committed goldens.

## HOW (the mechanism)

The control already exists in the engine: `Sim.Core/Engine.cs` → `Engine.MaxParallelism`, backed by a
cached `ParallelOptions` whose setter maps any non-positive value to `-1` (TPL's all-cores default).
The engine's plan / willPass / emit loops are **order-independent** (this is the property the 622
committed goldens are byte-identical proof of, and how `Sim.BenchCity --max-parallelism` sweeps a
scaling curve without moving any trajectory). So the whole change is argument parsing + one assignment;
no engine algorithm is touched.

Seam touched: `src/Sim.Sumo/SumoShim.cs` only.
- Add a `--max-parallelism` case to the existing order-independent flag loop (same `TakeValue()`
  mechanism as every other value-taking flag → works before/after `-c`, and `--flag=value` too).
- Parse with a new `ParseInt` helper mirroring the existing `ParseTime` (a bad value → `CliError` →
  exit 1 with a message, never an unhandled throw).
- Set `engine.MaxParallelism = maxParallelism;` immediately after `new Engine()` and before
  `LoadScenario` / `Run`. `maxParallelism` defaults to `-1`, so an omitted flag is exactly today's
  behaviour.
- Note the flag in the `--help`/usage text and the class-header flag contract.

## Determinism / parity argument

`--max-parallelism` never enters any algorithm — it only sets `ParallelOptions.MaxDegreeOfParallelism`,
which bounds *how many* worker threads the already-order-independent loops may use, not *what* they
compute or in *what order results are combined*. Below `Engine.ParallelPlanThreshold` (256 vehicles)
the plan phase is serial regardless, so the committed parity scenarios (all far smaller) are unaffected;
above it the parallel path is order-independent by construction. Therefore output is byte-identical for
any value, which the invariance test asserts directly.

## Tasks

- **T1 — parse & wire the flag.** `SumoShim.cs`: `--max-parallelism N` case (order-independent), a
  `ParseInt` helper, and `engine.MaxParallelism = maxParallelism` before the run. Update the usage text
  and class-header contract.
  **Success:** `sumosharp -c <cfg> --max-parallelism 4 …` runs capped to 4 threads; `--max-parallelism 1`
  is single-threaded; omitted / `<=0` = all cores. Flag works before or after `-c`. Unknown flags still
  warn-and-ignore. A non-integer value exits 1 with a message (no unhandled throw).
- **T2 — parallelism-invariance parity test.** `tests/Sim.ParityTests/RungHDgap4MaxParallelismTests.cs`:
  run one scenario (`41-multifile-cfg`) at `--max-parallelism` {1, 2, 4, omitted} and assert the
  fcd/summary/statistic bytes are identical to the default; assert the flag placed *before* `-c`
  produces identical bytes (the SumoData prefix case); assert `0`/`-1` == default; assert a non-integer
  value is reported not thrown.
  **Success:** the new test is green and all existing goldens/tests stay green (byte-identical).
- **T3 — docs.** Note the flag on the serve CLI in `README.md` and cross-link this doc from
  `SUMOSHARP-SERVE-PATH-DROP-IN.md`.
  **Success:** `--help` and README mention the flag and its perf-only, all-cores-default semantics.

## Tracker

- [x] T1 — `--max-parallelism` parsed order-independently and wired to `Engine.MaxParallelism`
- [x] T2 — parallelism-invariance parity test (byte-identical across {1,2,4,default}, before/after `-c`)
- [x] T3 — `--help` + README + drop-in-doc cross-link note the flag

## After it lands

Tell the SumoData hub; it bumps the submodule pointer to the new `main`, and the Windows box re-runs
**Test 2** (the `--workers` × `--max-parallelism` co-tuning sweep) to find the optimal setup per box
size. **Test 1 (correctness) does not need this flag** and can run without it.
