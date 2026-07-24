# `demo_city/box` — committed live-city dataset & regression fixture

This directory is the **single committed input** for the live-city demo AND for several automated tests.
It is a *regression fixture*: it is checked into git, and **multiple tests assert against its exact
contents/behaviour**, so it is NOT freely editable — changing a file here re-baselines those tests (see
[Changing this dataset](#changing-this-dataset)). Treat it like the parity goldens: deliberate,
regenerated-not-hand-edited, and committed.

## Files (13, ~2.4 MB)
| file | what it is | consumed by |
|---|---|---|
| `net.xml` | SUMO road network (the downtown crop) | engine (`LoadNetwork`), ped nav mesh |
| `scenario.rou.xml` | vehicle demand, routes, vTypes | live-city spawn edges, IgBridge runner, SUMO |
| `scenario.add.xml` | additional infrastructure (parking areas, etc.) | SUMO / scene |
| `scenario.sumocfg` | SUMO config tying the above together | SUMO (golden regeneration / investigation) |
| `vType.config.xml`, `vTypeDist.config.xml` | vehicle type + distribution config | demand generation |
| `vType_pedestrians.xml` | pedestrian vTypes | ped demand |
| `pois.add.xml`, `pois.json` | points of interest (parking, parks, …) | scene overlay, `PedPoiReader` |
| `buildings.json` | building footprints | scene overlay (viewers) |
| `zones.json` | district polygons | scene overlay (viewers) |
| `edge_fields.json` | per-edge metadata | scene / analysis |
| `manifest.json` | dataset manifest | scene load |

## Who depends on this data (change any of these files and re-run all of these)
**Tests (offline, no SUMO — part of `dotnet test`):**
- `tests/Sim.LiveCity.Tests/LiveCitySceneTests.cs` — asserts **exact** zone / building / POI counts and
  per-record invariants. A schema or content change breaks this loudly.
- `tests/Sim.LiveCity.Tests/LiveCitySimTests.cs` → **`DenseFlow_OverAThousandSeconds_KeepsDischarging_NoGridlock`**
  — the #15 dense-flow **liveness / throughput regression** test (see below). Threshold-based, so it is
  robust to small scene tweaks but sensitive to a net/demand change that alters throughput.
- `tests/Sim.IgBridge.Tests/*` — run the fixed-10 Hz IG runner against this net+demand and assert metrics.
- `tests/Sim.Pedestrians.Tests/PedPoiReaderTests.cs` — reads `pois.json`.

**Apps (not tests, but they render/run this dataset):**
- `src/Sim.Viewer` (Raylib 2D + City3D live-city), `src/Sim.Viz`, `src/Sim.IgBridge.Host`.

The dataset directory is hard-wired in `src/Sim.LiveCity/LiveCityConfig.cs` → `ForRepoRoot`
(`scenarios/_ped/demo_city/box`).

## The dense-flow liveness / throughput regression test
**Why it exists.** The parity gate (`tests/Sim.ParityTests`) structurally **cannot** catch the #15
junction-gridlock class: every #15 fix is demo-gated and inert on every parity golden, so a change that
silently reforms the dense-flow gridlock still passes parity byte-for-byte. This test is the guard.

**Where.** `tests/Sim.LiveCity.Tests/LiveCitySimTests.cs`, test
`DenseFlow_OverAThousandSeconds_KeepsDischarging_NoGridlock`.

**What it does.** Runs the coupled live-city sim **2000 steps (1000 s at dt=0.5)** headless (no SUMO) with
the dense-flow config **pinned in the test** (160 cars, teleport off, cooperative LC + the into-occupied
knobs on — immune to `LIVECITY_*` env vars), then asserts the sim keeps **discharging**:
- final `ArrivedTotal` ≥ **450** (measured healthy ≈ 736; a #15 gridlock flatlines ≈ 360),
- arrivals growth in the last 400 steps ≥ **40** (healthy ≈ +145; gridlock ≈ +2) — the anti-flatline, the
  sharpest gridlock signal,
- late stopped-fraction ≤ **0.85** (healthy ≈ 0.35; a frozen sim ≈ 1.0).

The thresholds sit with **wide margin on both sides** of the healthy/gridlock gap, so healthy flow never
flakes red while any gridlock regression trips it. It is deterministic (same seed + config ⇒ identical run),
so the thresholds are stable, not statistical.

### How to run
```bash
# just the liveness test (~11 s):
dotnet test tests/Sim.LiveCity.Tests -c Release \
  --filter "FullyQualifiedName~DenseFlow_OverAThousandSeconds"

# the whole live-city test project (22 tests incl. scene + per-area-LOD + determinism):
dotnet test tests/Sim.LiveCity.Tests -c Release
```

### Reproduce the same run with full diagnostics (the headless demo the thresholds came from)
```bash
dotnet build src/Sim.Viewer -c Release
LIVECITY_LCLOG=1 LIVECITY_WITNESS=1 LIVECITY_TELEPORT=0 LIVECITY_CARS=160 \
  dotnet run --project src/Sim.Viewer -c Release --no-build -- --mode live-city --smoke --frames 2000 \
  2>&1 | grep -E "LIVECITY-GRIDLOCK"
# arrivals + stoppedFrac per 20 s; the test reads the same ArrivedTotal / displacement-stopped signal.
```

## Changing this dataset
Because the files above are a pinned fixture, treat a change like regenerating a golden:
1. **Prefer not to.** If you only need a different scene for experimentation, point a *new* dataset dir at a
   copy and pass it via config — don't edit this one in place.
2. If a change here is intended (new crop, new demand, net fix), **re-run every dependant** in
   [Who depends on this data](#who-depends-on-this-data) and update their baselines together in the same
   commit:
   - `LiveCitySceneTests` exact counts (loud, obvious failures) — update the asserted numbers.
   - The **liveness test thresholds** — only if throughput genuinely shifts. Re-measure with the
     diagnostics command above; the numbers `arrivals ≥ 450`, `growth ≥ 40`, `stoppedFrac ≤ 0.85` were set
     from a healthy baseline of ≈ 736 / +145 / 0.35. Keep the wide margins (the test must still trip on a
     ~360-arrival gridlock).
   - IgBridge + PedPoiReader assertions.
3. Commit the regenerated data **and** the updated baselines atomically, with a note on what changed and why
   (mirrors `provenance.txt` on the parity goldens).

> Rule of thumb: **behavioral files** (`net.xml`, `scenario.rou.xml`) move the sim/throughput and are the
> load-bearing regression inputs — change them only deliberately. **Cosmetic files** (`pois*`,
> `buildings.json`, `zones.json`) mainly affect the viewers and the `LiveCitySceneTests` exact counts.
