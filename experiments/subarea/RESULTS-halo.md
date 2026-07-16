# Halo-depth convergence experiment

**Question:** how much buffer ring ("halo") around a fixed 1800x1800 m inner box is needed
before traffic *inside* the box stops changing, relative to running the full macro network
(an effectively infinite halo)?

**Inner box (fixed in every run):** `xmin=3450, ymin=3450, xmax=5250, ymax=5250` (1800x1800 m,
centered in the 8700x8700 m macro grid). Half-width = 900 m. 168 directed edges (a 7x7 node
sub-grid) fall inside it; this edge set was computed once with `sumolib` on `synth_macro.net.xml`
and reused unmodified for every halo depth and for the h=INF baseline (edge IDs are stable
across `netconvert --keep-edges.in-boundary` crops).

**Halo depths tested:** h in {0, 300, 600, 1200, 2400} m, plus h=INF (full macro,
`synth_macro.net.xml` + `macro.rou.xml`).

## Method, as executed

1. For each h: crop `synth_macro.net.xml` to `INNER` expanded by h on all sides via
   `netconvert -s ... --keep-edges.in-boundary <region> -o halo_h<h>.net.xml`.
2. Cut the macro demand into that crop with `cutRoutes.py`, re-timing departures via
   `--orig-net synth_macro.net.xml`.
3. Run SUMO on the crop (`begin=0 end=3600 step=1 time-to-teleport=-1 default.speeddev=0`),
   collecting per-edge aggregates via an `edgeData` meandata additional file, plus FCD output
   filtered to the 168 inner edges (`--fcd-output.filter-edges.input-file`) for a ground-truth
   distinct-vehicle count.
4. h=INF: same, but the full macro net + full macro demand, no cropping.

Metrics, computed over the 168 inner-box edges only:
- **mean speed**: sampledSeconds-weighted average of edgeData `speed` across inner edges.
- **veh-distance** (m): sum over inner edges of `speed [m/s] * sampledSeconds [s]` — this
  is exactly total vehicle-meters driven on those edges during [0, 3600]s (a vehicle
  travelling at the recorded mean speed for the recorded sampled time covers that distance).
- **distinct vehicles**: count of distinct vehicle IDs physically present (per-timestep) on
  an inner edge at any point, from the filtered FCD trace.

## A methodological pitfall found and fixed

The first pass used the harness-provided `macro.vehroutes.xml` as the source for
`cutRoutes.py`, per the original instructions. That file was generated **without**
`--vehroute-output.write-unfinished`, so SUMO silently drops any vehicle that has not
*arrived* by t=3600 from the file entirely — 648 of 4500 demand vehicles (14.4%) never
appear in it, because the full macro is loaded enough that ~14% of trips are still en route
at the 1-hour mark.

Since every halo crop is cut from that same incomplete file, every halo depth — including
h=2400, which covers 6600x6600 m out of the 8700x8700 m map — inherited an identical,
h-independent shortfall of roughly 100 inner-box-touching vehicles. This showed up as a
**flat ~9.8% deficit in distinct-vehicle count and an ~8.7% deficit in veh-distance that did
not shrink as h grew to 2400** — which looks exactly like non-convergence, but isn't: it's a
demand-recording artifact, not a spatial effect. This was confirmed by checking, vehicle by
vehicle, that every vehicle common to a halo cut and the full run drove the *identical*
sequence of inner edges in both runs (zero mismatches) — the deficit came entirely from
vehicles absent from the cut file's source, not from anything spatial.

Additionally, vehroute-output's `<route edges="...">` for a vehicle still en route at
simulation end lists its full *intended* route, not just the edges it actually reached — so
naively parsing vehroute XML for "touched the inner box" over-counts unfinished vehicles that
were still stuck far away and never got there. This is why distinct-vehicle counting for this
experiment was ultimately done from **filtered FCD** (actual per-timestep position), not
vehroute-output.

**Fix applied:** regenerated `macro.vehroutes.full.xml` with
`--vehroute-output.write-unfinished --vehroute-output.exit-times` (4499/4500 vehicles
captured), re-ran `cutRoutes.py` against it for all five halo depths (clean, no new
warnings — `avg teleportFactor 0.0` in the logs is an informational stat, not an actual
teleport), and re-ran all five SUMO simulations. All numbers below are from this corrected
pipeline. h=INF is unaffected by the fix (it never went through `cutRoutes.py`).

## Results (corrected)

| h (m) | mean speed (m/s) | veh-distance (m) | distinct vehicles | err speed % | err dist % | err veh % |
|------:|------------------:|------------------:|-------------------:|------------:|-----------:|----------:|
| 0     | 12.7025 | 1,765,167 | 1044 | 0.574 | 0.148 | 0.772 |
| 300   | 12.6286 | 1,754,736 | 1031 | 0.012 | 0.739 | 0.483 |
| 600   | 12.6209 | 1,753,665 | 1029 | 0.072 | 0.799 | 0.676 |
| 1200  | 12.6266 | 1,758,597 | 1031 | 0.027 | 0.520 | 0.483 |
| 2400  | 12.6157 | 1,752,096 | 1027 | 0.113 | 0.888 | 0.869 |
| INF   | 12.6300 | 1,767,791 | 1036 | 0.000 | 0.000 | 0.000 |

All figures are % relative error vs. the h=INF ground truth.

## h*

**h\* = 0 m** (0 as a fraction of the inner-box half-width, 900 m). All three metrics are
already within 1% of the h=INF ground truth at the *smallest* halo tested (h=0, i.e. no
buffer ring at all beyond the inner box itself); every larger halo tested (300–2400 m)
stays in the same sub-1% band, with no visible trend — the residual differences look like
run-to-run noise from the different re-timed departures cutRoutes assigns at each crop
boundary, not a shrinking bias. **Convergence is fast**: not merely "small h suffices" but
"h=0 already suffices," to well inside the 5% tolerance for every metric.

## Sanity checks

- **No SUMO errors, warnings, or teleport events** in any of the 5 halo runs or the h=INF
  run (checked both the original and corrected pipelines; `time-to-teleport=-1` disables
  teleporting outright, and none of the sumo logs contain any error/warning text).
- **`cutRoutes.py`** ran cleanly for every halo depth; disconnected-route counts (routes
  discarded because they briefly leave and re-enter the crop) grow mildly with h, as
  expected for a larger, more convoluted boundary — from 6 at h=0 to 63 at h=2400 — but this
  is immaterial to the inner-box measurement since those discards are far from the inner box.
- **Vehicle counts per run** (vehicles in each cut route file / full demand):

  | h | vehicles in demand file |
  |---|---:|
  | 0 | 1095 |
  | 300 | 1517 |
  | 600 | 1890 |
  | 1200 | 2672 |
  | 2400 | 3959 |
  | INF (macro.rou.xml) | 4500 |

  These counts scale with crop area, as expected, and are unrelated to the (roughly
  constant, ~1030–1044) count of vehicles that actually touch the 168 inner-box edges.
- Edge speed limit is uniform across the synthetic grid (13.89 m/s / 50 km/h); observed
  inner-box mean speeds (~12.6–12.7 m/s, ~91% of free flow) indicate mild but not severe
  congestion, consistent with rapid spatial decorrelation.

## Interpretation

For this synthetic, topologically uniform grid network under spatially-uniform random-trip
demand, the traffic **inside a fixed sub-area is essentially decoupled from everything
outside a very thin margin around it** — the zero-halo crop (literally just the inner box,
routed on its own with re-timed insertions) already reproduces the full-macro inner-box mean
speed, vehicle-distance, and distinct-vehicle-count to within 1%, far inside the 5% bar, and
that agreement doesn't measurably improve as the halo is grown all the way out to 2400 m
(over 2.5x the inner-box half-width, covering most of the map). This supports the underlying
claim — "the macro you must run scales with the box, not the terrain" — about as strongly as
a single scenario can: here the box needs no macro-scale companion at all, only itself, to
get the right in-box answer. The caveat is that this is a best case (homogeneous grid,
homogeneous demand, mild congestion); a network with strong directional bottlenecks or
demand concentrated outside the box feeding congestion into it would be expected to need a
non-zero halo, and this experiment doesn't probe that regime. The experiment also surfaced a
real trap for any future golden/regeneration work: `vehroute-output` without
`--vehroute-output.write-unfinished` silently discards not-yet-arrived vehicles, which can
masquerade as a spatial non-convergence signal if that file is later used to cut sub-regions;
any future cropping pipeline should regenerate vehroutes with that flag from the start.

## Artifacts

All generated files live under `experiments/subarea/scratch/halo/` (gitignored scratch, not
committed):
- `compute_inner_edges.py`, `inner_edges.txt`, `inner_edges.sel.txt` — inner-box edge set.
- `run_halo.sh` — original crop/cut/run driver (first-pass pipeline, exhibits the artifact).
- `macro.vehroutes.full.xml` — corrected vehroutes source
  (`--vehroute-output.write-unfinished --vehroute-output.exit-times`).
- `halo_h<h>.net.xml`, `halo2_h<h>.rou.xml`, `cfg2_h<h>.sumocfg`, `md2_h<h>.add.xml` — corrected
  per-halo crop/demand/config.
- `edge2_h<h>.xml`, `fcd2_h<h>.xml` — corrected per-halo edgeData / filtered FCD outputs.
- `edge_hINF.xml`, `fcd_hINF.xml` — h=INF ground truth outputs.
- `fcd_distinct.py` — distinct-vehicle extraction from filtered FCD.
- `final_table.json` — the numeric results table above, as JSON.
