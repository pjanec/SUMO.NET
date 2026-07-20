using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Orca;
using Sim.Pedestrians;
using Sim.Pedestrians.Navigation.Bake;
using Xunit;

namespace Sim.Pedestrians.Tests.Navigation;

// P8-1c Part 2 (docs/PEDESTRIAN-P8-1C-NAVMESH-CONTINUATION-DESIGN.md Section 5): the demand-side reachable-
// component filter keeps O/D demand on the dominant connected component(s) so a real crop's unbridgeable
// island stubs don't waste draws. These pin the filter logic on hand-built polygon sets (no SUMO geometry).
public class NavmeshReachabilityTests
{
    private static BakedPolygon Rect(int index, double x0, double y0, double x1, double y1)
        => new(index, $"p{index}", BakedPolygonKind.WalkablePolygon,
            new[] { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) });

    private static NavmeshReachability Reach(IReadOnlyList<BakedPolygon> polys, double frac = NavmeshReachability.DefaultMinAreaFraction)
    {
        var nav = new SumoNavMesh(polys);
        return new NavmeshReachability(polys, nav.ComponentLabels(), frac);
    }

    [Fact]
    public void DropsTinyIsland_KeepsDominantComponent()
    {
        // A big 10x10 surface (area 100) + a tiny 0.5x0.5 island (area 0.25) 100 m away -> two components.
        // At the 5% fraction the island (0.25 << 5) is dropped; the big component is reachable.
        var polys = new List<BakedPolygon>
        {
            Rect(0, 0, 0, 10, 10),
            Rect(1, 100, 100, 100.5, 100.5),
        };
        var reach = Reach(polys);

        Assert.False(reach.AllReachable);
        Assert.True(reach.IsReachable(new Vec2(5, 5)));      // inside the big surface
        Assert.False(reach.IsReachable(new Vec2(100.2, 100.2))); // inside the tiny island -> dropped
    }

    [Fact]
    public void KeepsTwoGenuinelyLargeComponents()
    {
        // Two 10x10 surfaces (area 100 each), 100 m apart -> two components, both >= 5% of the max -> BOTH
        // dominant (the real Geneva crop's 623-poly core + 151-poly region case). Nothing dropped.
        var polys = new List<BakedPolygon>
        {
            Rect(0, 0, 0, 10, 10),
            Rect(1, 100, 100, 110, 110),
        };
        var reach = Reach(polys);

        Assert.True(reach.AllReachable);
        Assert.Equal(2, reach.DominantComponents.Count);
        Assert.True(reach.IsReachable(new Vec2(5, 5)));
        Assert.True(reach.IsReachable(new Vec2(105, 105)));
    }

    [Fact]
    public void ConnectedNet_AllReachable_Inert()
    {
        // One connected surface (two abutting rects sharing an exact edge) -> 1 component -> AllReachable,
        // every point reachable. This is the inert case every committed connected box hits.
        var polys = new List<BakedPolygon>
        {
            Rect(0, 0, 0, 10, 10),
            Rect(1, 10, 0, 20, 10), // shares the x=10 edge
        };
        var reach = Reach(polys);

        Assert.True(reach.AllReachable);
        Assert.True(reach.IsReachable(new Vec2(5, 5)));
        Assert.True(reach.IsReachable(new Vec2(15, 5)));
    }

    [Fact]
    public void SubareaDemand_FiltersUnreachableEndpoints()
    {
        // Big reachable surface + tiny island; fringe endpoints on each. The filter drops the island endpoint.
        var polys = new List<BakedPolygon>
        {
            Rect(0, 0, 0, 10, 10),
            Rect(1, 100, 100, 100.5, 100.5),
        };
        var reach = Reach(polys);

        var fringe = new List<(string, Vec2)>
        {
            ("onBig", new Vec2(5, 5)),
            ("onIsland", new Vec2(100.2, 100.2)),
        };

        var filtered = SubareaDemand.Build(Array.Empty<PedPoi>(), fringe, fringeWeight: 1.0, reachable: reach.IsReachable);
        Assert.Equal(1, filtered.Count);
        Assert.Equal("onBig", filtered[0].EdgeId);

        // Inert with no filter: both endpoints kept (bit-identical to before Part 2).
        var unfiltered = SubareaDemand.Build(Array.Empty<PedPoi>(), fringe, fringeWeight: 1.0);
        Assert.Equal(2, unfiltered.Count);
    }

    [Fact]
    public void SubareaDemand_FilterEmptyingAll_FallsBackToUnfiltered()
    {
        // A predicate that rejects everything must not produce an empty demand -- it falls back to the full set
        // so a pathologically-fragmented crop still spawns instead of stalling.
        var fringe = new List<(string, Vec2)> { ("a", new Vec2(0, 0)), ("b", new Vec2(1, 1)) };
        var demand = SubareaDemand.Build(Array.Empty<PedPoi>(), fringe, fringeWeight: 1.0, reachable: _ => false);
        Assert.Equal(2, demand.Count);
    }
}
