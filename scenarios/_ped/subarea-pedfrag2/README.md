# Synthetic ped-navmesh RESIDUAL-fragmentation witness (SumoSharp P8-1c)

A geometry-free, shareable reproduction of the **residual** pedestrian navmesh-bake
fragmentation that the **P8-1b fix does NOT resolve** on real geometry. Successor to
`synthetic_pedfrag`: the P8-1b fix (area-overlap + 5 cm near-abutment portal bridging)
now bakes that older witness to **1** component, so it no longer bites. This witness
**still fragments under the fixed recorder**, giving the ped session a geometry-free
target to drive the real box's residual (1010 -> **366** components, still routing-limited
to ~4.5% of the density cap) down to 1/few.

## Numbers under the FIXED recorder (branch `claude/pedestrian-dds-transport-c8w2gf`, tip `d942def`)

`dotnet run --project src/Sim.Viz -c Release -- --ped-subarea-fcd ped.fcd.xml --dial 0.05 --seconds 60 --box <this>/box`

| box | polygons | components (fixed recorder) | peakLive | pop cap | verdict |
|---|---|---|---|---|---|
| **this witness** (`synthetic_pedfrag2/box`) | 438 | **83** (1 core=356 + 82 sidewalk singletons) | **13** | 124 | fragments UNDER the fix; routing-limited far below cap |
| `synthetic_pedfrag` (old witness) | 492 | 1 | 22 | 115 | fix connects it |
| uniform grid (`scenarios/_ped/subarea-box`) | 456 | 1 | 33 | 203 | stays connected |
| real ~2 km box (reference) | 1291 | 366 | 124 | 2773 | the residual this reproduces |

peakLive **13 of cap 124** is **routing-limited, not dial-limited** — the exact
fragmentation direction of the real residual (there: 366 components, peak 124 vs cap 2773).

## The residual mode this embeds (geometry-free)

Diagnosed on the real box under the fix: of its 366 residual components, **279 were
singleton `SidewalkSegment` polygons** and **347 of 357** small (<=3-poly) components were
pure sidewalk; of the near cross-component polygon pairs (boundaries within 2 m),
**189/192 were `SidewalkSegment`<->`SidewalkSegment`** and **152/192 sat within 5 cm**.

**Root cause.** The P8-1b area-overlap / near-abutment pass is **AREA-ANCHORED**: it adds a
portal only when at least one polygon is an AREA kind (`WalkingArea`/`WalkablePolygon`), so
a junction always bridges *through* a walkingArea, never sidewalk<->sidewalk directly (the
POC-0 no-shortcut invariant). But real netconvert geometry is full of seams where two
sidewalk strips abut within 5 cm with **no surviving walkingArea between them** — SUMO
emits a degenerate zero-area walkingArea at simple continuations (dropped by the baker's
`MinArea` filter), and the buffered strips come within ~1 mm–5 cm without sharing vertices
to 1 mm (so the exact-edge and 1 mm vertex passes miss them). With no area to anchor to,
the bridge is refused and each such sidewalk strip is left a disconnected island.

> **Pattern:** *two sidewalk strips meet within 5 cm with no walkingArea between them ->
> area-anchored bridging has no area to anchor to -> the seam never connects.*

**How this witness reproduces it (no real geometry, no place data):**
- **Connected core** — `netgenerate --rand` with full ped infra. Its irregular geometry
  makes sidewalks overlap the junction walkingAreas, so the area-anchored pass connects it
  into ONE component that *does* populate (peakLive > 0) — the analogue of the real box's
  large connected core (623 + 151 poly).
- **Fragmented tail** — radial **chains of short "stub" edges** anchored at core junctions.
  Consecutive stubs are separated by a **3 cm gap between distinct (un-joined) nodes**, so
  each stub's sidewalk strip abuts the next within 3 cm with no walkingArea between — exactly
  the residual mode. Every stub past the first bakes to a disconnected `SidewalkSegment`
  singleton.

All 83 fragmenting seams are sidewalk<->sidewalk pairs **within 5 cm**, so the fragmentation
is genuinely *bridgeable*: unioning the <=5 cm sidewalk<->sidewalk pairs collapses 83 -> **1**.

## Fix acceptance (P8-1c)

A correct P8-1c fix — bridge sidewalk<->sidewalk near-abutment (e.g. relax area-anchoring
for the <=5 cm sidewalk<->sidewalk case, or don't drop the degenerate zero-area walkingArea
that should sit between them) — must:

1. **Connect THIS witness to 1 / a few components** and **populate to the dial** (peakLive
   -> toward cap 124, not 13), while
2. **`synthetic_pedfrag` and the uniform grid stay at 1 component** (the fix must not be a
   blanket "connect anything close" that also re-triggers the POC-0 3-polygon-corner
   shortcut — those two boxes are already correctly connected and must remain so).

## Regenerate

Requires SUMO 1.20.0 (`netgenerate`/`netconvert` on PATH), `SUMO_HOME`/`PYTHONPATH` set, python3.

```
python3 experiments/subarea/synthetic_pedfrag2/build.py --out /tmp/pedfrag2
```

Deterministic: fixed `--seed 7` on netgenerate (core) and `--seed 42` on `deduce_pois`; the
chain layout is a pure function of the core junction coordinates. Same seeds -> byte-identical
box -> identical recorder numbers. The committed `box/` (net.xml + manifest.json + pois.json)
is the ready-to-run box; you need SUMO only to regenerate from scratch, not to re-run the
recorder against the committed box.

No real geometry is used anywhere — the net is 100% `netgenerate --rand` + a synthetic
plain-XML stub tail.
