using System;
using System.Collections.Generic;
using Sim.Core.Orca;

namespace Sim.Pedestrians.Navigation.Bake;

// P8-1c Part 2 (docs/PEDESTRIAN-P8-1C-NAVMESH-CONTINUATION-DESIGN.md Section 5): the demand-side reachable-
// component filter. Adjacency bridging (P8-1b/P8-1c) can only take a real crop ~366 -> ~213 components -- the
// residual ~212 are Mode-3 isolated stubs, cropped-off network fragments >2 m from anything that adjacency
// legitimately cannot (and should not) reconnect. Drawing O/D endpoints uniformly over ALL components then
// wastes most draws on unroutable islands (the peakLive-far-below-cap symptom the sub-area session measured:
// ~93% of draws unroutable). This keeps demand on the DOMINANT connected component(s) -- the large reachable
// surface -- so the crowd fills it to the dialed density instead of being dragged down by unreachable islands.
//
// "Dominant" = every component whose total walkable |area| is at least MinAreaFraction of the LARGEST
// component's area -- a fraction, NOT top-1, so a crop with two genuinely-large disjoint reachable regions
// (the real Geneva crop has a ~623-poly core AND a ~151-poly region) keeps BOTH while the long tail of
// 1-3-poly island stubs is dropped.
//
// Deterministic (a pure function of the bake); INERT when the whole net is one component / fully dominant
// (AllReachable -> nothing dropped), so the witness subarea-pedfrag2 (now 1 component after Part 1) and every
// committed connected box are unchanged. The MinAreaFraction default is PROVISIONAL, tunable, pending the
// real-crop component-size distribution from the sub-area session.
public sealed class NavmeshReachability
{
    // A component is reachable/dominant if its total |area| >= this fraction of the largest component's area.
    // 0.05 keeps the real crop's ~151-poly region (~24% of the ~623-poly core) while dropping singletons.
    public const double DefaultMinAreaFraction = 0.05;

    private readonly IReadOnlyList<BakedPolygon> _polygons;
    private readonly int[] _labels;
    private readonly HashSet<int> _dominant;

    public NavmeshReachability(
        IReadOnlyList<BakedPolygon> polygons, int[] componentLabels, double minAreaFraction = DefaultMinAreaFraction)
    {
        if (componentLabels.Length != polygons.Count)
        {
            throw new ArgumentException("componentLabels length must match polygons count.", nameof(componentLabels));
        }

        _polygons = polygons;
        _labels = componentLabels;

        var areaByComponent = new Dictionary<int, double>();
        for (var i = 0; i < polygons.Count; i++)
        {
            var c = componentLabels[i];
            var a = Math.Abs(PolygonGeometry.SignedArea(polygons[i].Vertices));
            areaByComponent[c] = areaByComponent.TryGetValue(c, out var acc) ? acc + a : a;
        }

        var maxArea = 0.0;
        foreach (var a in areaByComponent.Values)
        {
            if (a > maxArea)
            {
                maxArea = a;
            }
        }

        _dominant = new HashSet<int>();
        var threshold = maxArea * minAreaFraction;
        foreach (var kv in areaByComponent)
        {
            // >= threshold keeps every large region; a zero-area maxArea (degenerate/empty bake) makes the
            // threshold 0 so nothing is dropped (inert).
            if (kv.Value >= threshold)
            {
                _dominant.Add(kv.Key);
            }
        }
    }

    // The component ids kept as reachable (dominant). Exposed for diagnostics / tests.
    public IReadOnlyCollection<int> DominantComponents => _dominant;

    // True iff every polygon is in a dominant component -- the filter would drop nothing, so a caller can skip
    // filtering entirely (the inert fast path for a well-connected crop; every committed box hits this).
    public bool AllReachable
    {
        get
        {
            for (var i = 0; i < _polygons.Count; i++)
            {
                if (!_dominant.Contains(_labels[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // Is `p` on the dominant reachable surface? Maps p to its containing polygon (an endpoint at a sidewalk-lane
    // midpoint sits inside its strip) or, if none contains it, the nearest polygon by boundary distance (a POI a
    // few cm off snaps to the nearest), then checks that polygon's component. False for an empty bake.
    public bool IsReachable(Vec2 p)
    {
        var idx = NearestPolygonIndex(p);
        return idx >= 0 && _dominant.Contains(_labels[idx]);
    }

    private int NearestPolygonIndex(Vec2 p)
    {
        for (var i = 0; i < _polygons.Count; i++)
        {
            if (PolygonGeometry.Contains(_polygons[i].Vertices, p))
            {
                return i;
            }
        }

        var best = -1;
        var bestSq = double.MaxValue;
        for (var i = 0; i < _polygons.Count; i++)
        {
            PolygonGeometry.NearestPointOnBoundary(_polygons[i].Vertices, p, out var dSq);
            if (dSq < bestSq)
            {
                bestSq = dSq;
                best = i;
            }
        }

        return best;
    }
}
