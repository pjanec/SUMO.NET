using System.IO;
using System.Linq;
using Sim.Pedestrians;
using Xunit;

namespace Sim.Pedestrians.Tests;

// P8 (docs/COORDINATION-pedestrian-x-subarea.md §3): SubareaManifest reads the durable half of the sub-area
// data contract from the committed handoff box. Pins it against scenarios/_ped/subarea-box/manifest.json so
// the host wiring (fringe -> PedSpawnPolicy; box frame; density knee) consumes a validated contract.
public class SubareaManifestTests
{
    private static string BoxManifest()
    {
        var dir = System.AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Traffic.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!, "scenarios", "_ped", "subarea-box", "manifest.json");
    }

    [Fact]
    public void Load_ReadsTheHandoffBoxContract()
    {
        var m = SubareaManifest.Load(BoxManifest());

        // 48 walkable fringe edges (the P8-2 appearance boundary), all distinct.
        Assert.Equal(48, m.WalkableFringeEdges.Count);
        Assert.Equal(48, m.WalkableFringeEdges.Distinct().Count());

        // Shared coordinate frame: 0,0 -> 800,800 m.
        Assert.Equal(0.0, m.BoxBounds.XMin);
        Assert.Equal(0.0, m.BoxBounds.YMin);
        Assert.Equal(800.0, m.BoxBounds.XMax);
        Assert.Equal(800.0, m.BoxBounds.YMax);

        // Calibrated vehicle-density knee the P8-4 ped density knob anchors against.
        Assert.Equal(13.741, m.KneeVehLkm, precision: 3);
    }

    [Fact]
    public void FringeEdges_SeedAPedSpawnPolicy_ThatAllowsThemOnCamera()
    {
        var m = SubareaManifest.Load(BoxManifest());

        // The host builds the durable fringe set once; here every fringe edge is on-camera to prove the gate
        // still admits fringe appearances in full view (the whole point of the fringe carve-out).
        var policy = new PedSpawnPolicy(
            fringeEdgeIds: m.WalkableFringeEdges,
            visibleWalkableEdgeIds: m.WalkableFringeEdges);

        foreach (var edge in m.WalkableFringeEdges)
        {
            Assert.True(policy.MaySpawnOrDespawn(edge), $"fringe edge {edge} must be a legitimate on-camera appearance");
        }

        // A made-up on-camera non-fringe edge is still forbidden.
        Assert.False(new PedSpawnPolicy(m.WalkableFringeEdges, new[] { "not-a-fringe-edge" })
            .MaySpawnOrDespawn("not-a-fringe-edge"));
    }
}
