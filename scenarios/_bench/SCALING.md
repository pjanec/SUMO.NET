# Scaled-city benchmark — scaling curve (rungs 1-4, `-L 1`)

`BENCHMARK_SPEC.md` / `VIZ_BENCH_TASKS.md` VB-8. All four rungs share the SAME generator
(`scripts/gen-benchmark.sh <targetConcurrency> [gridNumber] [gridLength] [end]`), the same seed
(42), `sigma=0`, single-lane (`-L 1`) edges (see `city-30/NOTES.md` for why), and Euler
integration. Rungs 2-4 (city-300/3000/15000) additionally share ONE net (`netgenerate --grid
--grid.number=24 --grid.length=500 -L 1 --tls.guess --seed 42`, 1,104 lane-km / 576 junctions),
sized once at the 15,000-concurrent rung per `BENCHMARK_SPEC.md`'s stated preference — see
`city-300/NOTES.md` "Net-sizing choice" for the full reasoning and the empirical capacity probing
that led to this size. `city-30` keeps its original small dedicated 3x3/200m net (unchanged,
already committed).

**Headline (FIXED): the benchmark surfaced a genuine `Sim.Core` defect, which is now patched.**
The first `-L 1` scale-up (at ~300 concurrent) exposed a real engine bug — `Engine.FindFoeVehicle`
treated ANY vehicle whose route passed through a priority-junction foe lane AT ANY FUTURE POINT as
an "approaching foe" with no proximity/time-window filter, so on the 576-junction net almost every
approach lane found a false foe and vehicles stalled (city-300: engine 46 arrived vs SUMO 238; SUMO
ran the identical demand at free flow). It was reported (not patched here) via
`/NEED-priorityjunction-farrouted-foe-falsepositive.md` and subsequently **fixed on `main` as
`C4-vi` (gate the approaching-foe yield by reservation distance).** Post-fix, the engine tracks
SUMO's aggregates across the ladder (see the table + "Headline update" below) — the benchmark did
its job: surfacing a correctness gap the small parity scenarios can't reach, then confirming the fix
at scale.

## Scaling table

| rung | target N | measured peak concurrent (SUMO) | tuned period (s) | engine RTF | engine steps/s | engine peak RSS | engine stuck (ever / still-at-end) | SUMO teleports | arrived (SUMO / engine) | mean duration s (SUMO / engine) | KS distance | PASS/FAIL |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| rung | target N | peak concurrent | period (s) | engine RTF | peak RSS | stuck (ever/end) | arrived (SUMO/engine) | mean dur s (SUMO/engine) | KS | result |
|---|---|---|---|---|---|---|---|---|---|---|
| city-30   | 30    | 41 (S) / 37 (E)     | 2.0736  | ~1300-2300x | ~55 MiB   | 0 / 0 | 255 / 256  | 67.60 / 63.74   | 0.218 | **PASS** |
| city-300  | 300   | 485 (S)             | 1.2448  | 35.9x†      | 159 MiB   | 0 / 0 | 238 / 241  | 424.65 / 426.30 | pass  | **PASS** |
| city-3000 | 3000  | 4365 (S) / 4175 (E) | 0.15725 | 0.28x†      | 624 MiB   | 0 / 0 | 3260 / 3458 | 514.49 / 508.05 | 0.028 | **PASS** |
| city-15000| 15000 | 17639 (S)           | 0.05837 | RUNNING     | —         | —     | 8010 / —   | 616.33 / —      | —     | RUNNING |

(S)=SUMO reference, (E)=engine. All SUMO references show 0 teleports.

**Headline update (post-`C4-vi` fix):** the `FindFoeVehicle` false-positive-foe defect that this
benchmark surfaced is FIXED on `main` (`C4-vi`: gate the priority-junction approaching-foe yield by
reservation distance). Re-running the rungs shows the engine now tracks SUMO's aggregates across two
orders of magnitude of concurrency: **city-300 went from 46 arrived (FAIL) to 241** (SUMO 238), and
**city-3000 passes at ~4,175 peak concurrent** (arrived 3458 vs 3260, mean duration within 1.3%, KS
0.028) with **0 stuck vehicles** — no gridlock. city-15000 (the headline rung) is running.

**† perf numbers are provisional.** RTF/RSS for city-300 and especially city-3000 were measured with
engine-FCD writing on and/or under concurrent CPU load (viz work in the same container), so the
throughput figures understate real performance — the 0.28x at city-3000 is not a clean number. The
correctness/stability columns (arrived, stuck, aggregate) are deterministic and unaffected. A clean
perf pass (`--fcd-out ""`, no concurrent load) is pending to produce a trustworthy RTF-vs-scale curve;
city-15000 is being run that way.

## Net-capacity / stability findings (SUMO reference side)

All rungs' SUMO reference reached their target concurrency band via the collapse-aware Little's
law tuning in `scripts/gen-benchmark.sh` (see that script's "COLLAPSE DETECTION" comment, added
after directly reproducing several candidate nets going into unbounded queueing collapse during
this session's net-sizing exploration — not merely theorized). None of the four committed rungs'
FINAL tuned configuration collapsed; city-15000's SUMO reference settles into a busy but
non-collapsing plateau (`meanSpeedRelative` ~0.45-0.5, `halting`/`running` ~45-47% at the end of
the 1500s window, `arrived` still climbing steadily — congested, not gridlocked).

## Engine-side stability findings

See the headline finding above and `/NEED-priorityjunction-farrouted-foe-falsepositive.md`. The
engine's stuck-count is expected to grow sharply from rung to rung because the underlying defect's
severity scales with network size (fixed at 576 junctions for rungs 2-4) and concurrent vehicle
count (which increases every rung) — this is a real correctness gap exposed at scale, not a
benchmark-harness artifact, and no amount of net-size or insertion-period retuning works around it
(confirmed: city-300 fails despite its SUMO reference being near-free-flow).
