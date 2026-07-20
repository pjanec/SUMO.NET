# Real-net ped-navmesh residual fragmentation — geometry-free mode description

The P8-1b fix (area-overlap + 5 cm near-abutment portal bridging, area-anchored) took a
real ~2 km cropped pedestrian box from 1010 -> **366** connected walkable components under
the fixed recorder — still fragmented, still routing-limited to ~4.5% of the density cap
(peakLive 124 vs cap 2773). This describes the residual as geometry-free patterns, ranked,
so it can be reproduced with no real geometry. (Diagnosed with a temporary component +
near-pair dump added to `PolygonGraph`, since reverted.)

## Component shape of the 366 residual components
- 1 giant core (623 poly) + 1 large (151) + a handful of medium (31, 12, 8, 7, 5, 5, 4).
- **279 singletons — every one a `SidewalkSegment`.**
- 68 components of size 2, 10 of size 3. **347 of the 357 small (<=3-poly) components are
  pure sidewalk**; the other 10 are `SidewalkSegment|WalkingArea`.

## Near cross-component polygon pairs (boundaries within 2 m): 192 total
| bucket | count | note |
|---|---|---|
| gap <= 5 cm | 152 | within the fix's 5 cm abutment reach — blocked only by area-anchoring |
| 5–20 cm | 12 | beyond the 5 cm cap |
| 20–50 cm | 11 | |
| 50 cm–1 m | 7 | |
| 1–2 m | 10 | |

Kind of the pair: **189 / 192 are `SidewalkSegment` <-> `SidewalkSegment`** (both non-area);
only 3 involve a `WalkingArea`. Corner multiplicity: mostly 2 (genuine two-party touch).

## Ranked residual modes

**Mode 1 — sidewalk <-> sidewalk abutment with NO walkingArea between them (dominant, ~95%).**
Two buffered sidewalk strips meet within ~1 mm–5 cm, but there is no surviving `WalkingArea`
/ `WalkablePolygon` between them, so the fix's **area-anchored** overlap/abutment pass (adds
a portal only when at least one polygon is an area) refuses the bridge. The strips also do
not share vertices to 1 mm (buffering slop), so the exact-edge and 1 mm vertex passes miss
them. Result: isolated sidewalk singletons and pure-sidewalk 2/3-poly chains. Carries 189/192
near pairs and 347/357 small components. Two sub-shapes feed it:
  - a degenerate **zero-area walkingArea** that SUMO emits at a simple continuation and the
    baker's `MinArea` filter drops — leaving the two sidewalks it "should" have joined with
    nothing between them;
  - sidewalk strips that come close at an irregular seam where no walkingArea exists at all.

**Mode 2 — near-abutment gap wider than the 5 cm cap (minor, ~20% of near pairs).**
Sidewalk<->sidewalk (or the 3 sidewalk<->walkingArea) pairs 5 cm–2 m apart: even where
area-anchoring would allow it, `AbutProximityEps = 5 cm` cannot reach.

**Mode 3 — genuinely isolated stubs (>2 m from anything).** Bridging *every* near pair
within 2 m still leaves ~213 components, so ~212 are sidewalk stubs cropped off the network,
farther than 2 m from any other walkable polygon — a real-crop artifact that adjacency
bridging legitimately cannot (and should not) reconnect. This bounds what a navmesh
connectivity fix alone can achieve on the real crop (~366 -> ~213 by near-abutment bridging);
reaching ~1 additionally needs the demand side to tolerate isolated islands, or a snap step.

## What the synthetic witness reproduces
The witness (`synthetic_pedfrag2`) embeds **Mode 1** deliberately and bridgeably: a
connected `netgenerate --rand` core (populates) + radial chains of stub edges separated by
**3 cm gaps between distinct nodes**, i.e. sidewalk<->sidewalk seams with no walkingArea.
Under the fixed recorder it bakes to **83 components** (1 core + 82 sidewalk singletons),
peakLive 13 of cap 124 — components >> 1 and routing-limited far below cap EVEN WITH THE
FIX. Because every seam is a <=5 cm sidewalk<->sidewalk pair, a correct P8-1c fix collapses
it to **1** component (unlike the real crop's Mode-3 tail, this witness is fully bridgeable
by design, making it a clean fix target).
