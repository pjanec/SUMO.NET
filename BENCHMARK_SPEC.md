# BENCHMARK_SPEC.md — Scaled city benchmark scenario

A self-contained briefing for building the performance/stability benchmark. Read `DESIGN.md`
and `CLAUDE.md` first. This is **not a parity test** — do not wire it into the `dotnet test`
parity path. It is a scaling/throughput/stability harness with *statistical* (not
vehicle-for-vehicle) comparison to SUMO.

## Purpose and non-goals

Prove the engine runs a non-trivial network at scale — up to **~15,000 concurrent peak
vehicles** — to completion, fast, without deadlock. Success is measured by throughput and
stability and by *aggregate* agreement with SUMO, NOT by exact trajectory parity. Realistic
demand/real-city topology is explicitly a non-goal; a synthetic network is sufficient and
preferred for reproducibility.

## Hard constraint: self-service, no external download

Everything must be generatable from the pinned pip SUMO install alone — no fetching external
scenarios, no network access beyond what's already available. Therefore the network is
**generated with `netgenerate`** (ships with SUMO) and demand with **`randomTrips.py`** /
**`duarouter`** (also shipped). This keeps the whole benchmark reproducible on a blank
volatile VM with just `install-sumo.sh`. (Optional realism upgrade, only if the Overpass host
is later allowlisted: swap the network for an OSM district via `osmGet.py`/`osmBuild.py`.
Leave this as a documented option, don't build it now.)

## Concurrency semantics (the number that matters)

"15k" means **peak concurrent active vehicles** (simultaneously in the network), not total
trips. This is what drives per-step cost. Use a steady-state plateau, not a rush-hour spike:
insert at a constant rate long enough that arrivals balance insertions, producing a stable
concurrency plateau that's clean to measure and benchmark on.

**Tuning heuristic (Little's law):** concurrent ≈ insertion_rate × mean_trip_duration. So to
hit a target concurrency N, set insertion rate ≈ N / (mean trip time). Estimate mean trip
time from a short pilot run, set the rate, then **measure** actual concurrency from
`--summary-output` (it logs running/halting counts per step) and adjust. Iterate 2–3 times to
land the plateau in the target band.

## The scaling ladder (tune small first)

Build and tune the whole pipeline — generate → route → run → measure → feed viz — at tiny
scale, then scale the same pipeline up. Each rung is the identical process with one knob (the
insertion rate) changed:

1. **~30 concurrent** — pipeline bring-up. Verify generation, routing, engine run to
   completion, summary/tripinfo outputs parse, and the FCD feeds `Sim.Viz` into a watchable
   `replay.html`. Get everything wired here where it's cheap to debug.
2. **~300 concurrent** — first real multi-junction interaction; confirm no early deadlock,
   check RTF, commit the viz replay (still small enough to commit).
3. **~3,000 concurrent** — stress junctions/TLS/lane-changing; watch teleport count as the
   stability signal; FCD now large — stop committing it (see outputs below).
4. **~15,000 concurrent** — the headline benchmark. Measure throughput and stability; compare
   aggregates to SUMO.

Parameterize the scenario so the rung is a single argument (target concurrency → derived
insertion rate), not four hand-built scenarios. One generator script, one knob.

## Network generation

Use `netgenerate` to produce a network with enough junction variety to be non-trivial:
- A randomized network (`netgenerate --rand`) with a controlled node count, or a grid with
  random perturbation — either gives real junctions, multiple lanes, and turn structure.
- Enable multiple lanes per edge (so lane-changing/overtaking is exercised) and
  traffic-light junctions (`--tls.guess` or the default TLS assignment) so the TLS path and
  the viz signal rendering are exercised.
- Size the network to the *largest* rung (15k) so the same net serves all rungs; only demand
  changes between rungs. A network too small for 15k will gridlock artificially; too large
  wastes memory. Rough target: enough lane-km that 15k vehicles sit around typical urban
  density, not bumper-to-bumper. Tune net size once at the 15k rung.
- Commit the generated `*.net.xml` plus the exact `netgenerate` command and seed in
  `provenance.txt` so it's reproducible.

## Demand generation

- `randomTrips.py` over the generated net with a fixed seed, `--fringe-factor` to bias trips
  to enter/exit at the network edge (realistic through-traffic), and the insertion `period`
  set from the Little's-law estimate for the rung's target concurrency.
- Pre-route with `duarouter` (or `randomTrips.py --route-file`) to `*.rou.xml` for
  deterministic, reproducible runs rather than on-the-fly routing.
- Fixed seed everywhere; commit the seed and commands in `provenance.txt`.
- For phase-appropriate determinism you may keep `sigma=0` for cleaner comparison, or enable
  a realistic `sigma` — but if `sigma>0`, comparison is statistical only (it always is here
  regardless). Note the choice in the scenario config.

## Dependency gate — when this can actually run

A 15k-vehicle city exercises multi-lane changing, priority junctions, traffic lights, and
routing. The engine can't run it meaningfully until roughly **rung 10 (traffic lights)** of
the parity ladder is done. So: design and script this now; the small-scale bring-up (rung 1,
~30 vehicles) can run as soon as junctions+TLS exist; the full 15k rung waits until the core
is complete. This is the integration/stress milestone that proves the whole system holds
together after the unit-level ladder validates each piece.

## Reference generation and committed-vs-regenerated outputs

Run stock SUMO 1.20 once per rung to produce the aggregate ground truth, following the same
goldens discipline but adapted for scale:

**Commit (small, aggregate):**
- `*.net.xml`, `*.rou.xml`, `config.sumocfg`, `provenance.txt` (all rungs)
- SUMO `--summary-output` (per-step running/halting/mean-speed counts) — the throughput
  ground truth
- SUMO `--tripinfo-output` (per-trip duration/speed/waiting) — the distribution ground truth
- The generated `replay.html` **only for rungs 1–2** (tens/hundreds of vehicles)

**Do NOT commit (too large, regenerate locally):**
- Full `--fcd-output` at rungs 3–4 (15k vehicles × an hour is huge). Regenerate on demand;
  never commit. The viz for large rungs is a local, regenerated artifact.

## Success criteria

**Stability (must pass):** the run completes; teleport count stays below a threshold you set
per rung (teleports are the deadlock/gridlock signal — a spike means the engine is locking
up, not that it's slow). Record teleport count as a first-class metric.

**Statistical agreement with SUMO (should track):** total vehicles arrived, mean trip
duration, mean network speed, and the trip-duration *distribution* (bucketed histogram or a
KS-style comparison) should be close to SUMO's — "close" is a per-rung tolerance you set, far
looser than parity tolerances. This confirms the engine produces the *same kind of traffic*,
not the same exact traffic.

**Performance (record, don't gate):** wall-clock runtime, steps/sec, **real-time factor**
(sim-time ÷ wall-time), peak concurrent vehicles achieved, and peak memory (RSS). These are
the benchmark numbers; track them across rungs to get a scaling curve and across commits to
catch regressions.

## Viz integration

The benchmark run emits FCD which feeds `Sim.Viz` (see `VIZ_SPEC.md`) to produce a
`replay.html`. This is the primary way to *watch* the benchmark and spot gridlock visually.
Caveats by scale:
- Rungs 1–2 (tens/hundreds): full FCD → viz, committed. Watchable on the phone.
- Rungs 3–4 (thousands+): FCD is large and Canvas-2D rendering of 15k boxes at 60fps will
  strain. Feed viz a **downsampled** FCD (every Nth vehicle, or crop to a camera bounding
  box, or coarsen the timestep) for spot-checking gridlock; don't commit it. Note the viz
  performance ceiling and prefer aggregate `--summary-output` plots for the full-scale
  quantitative view.

## Done-condition (initial)

A single parameterized generator script (committed under `scripts/`, e.g.
`gen-benchmark.sh <targetConcurrency>`) that, from the pinned SUMO install alone, produces the
net + routes + config for a given rung, plus a runner that executes SUMO to emit the committed
aggregate goldens and (for small rungs) the viz replay. Bring-up proven at ~30 concurrent:
pipeline runs end to end, `replay.html` watchable, summary/tripinfo committed with provenance.
Larger rungs are the same script with a bigger argument, runnable once the engine reaches the
TLS milestone.

## Placement in the queue

Independent of the parity ladder; runs after ~rung 10. Script and tune it now at tiny scale
(it validates the generation/viz pipeline early and cheaply); scale up as engine capability
lands. Not a `dotnet test` parity gate — it's a separate benchmark harness with its own
statistical + performance criteria.
