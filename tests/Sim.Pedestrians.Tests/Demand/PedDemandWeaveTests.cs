using Sim.Core.Bridge;
using Sim.Core.Orca;
using Sim.Pedestrians.Demand;
using Sim.Pedestrians.Lod;
using Sim.Pedestrians.Navigation.Bake;
using Xunit;

namespace Sim.Pedestrians.Tests.Demand;

// W2 (docs/PEDESTRIAN-WEAVE-PRODUCTION-DESIGN.md): the deterministic weave wired into the production
// lively-demand path behind PedDemandConfig.EnableWeave. Runs the SAME POC-0 nav mesh + always-pauses
// liveliness as PedDemandLivelinessTests, then compares a weave-ON run against a weave-OFF (centreline)
// run of an otherwise identical config. Because the weave seed comes from a SEPARATE salted stream and
// MakeWalk consumes no RNG, the spawn/pause structure is identical between the two runs -- so at each
// (id, now) the weave-off pose IS the centreline and the weave-on pose is centreline + lateral. Asserts:
//  1. Clamp safety: |on - off| never exceeds the baked sidewalk half-width at that point (+eps).
//  2. Active: somewhere the weave visibly moves the ped off the centreline (guards a vacuous pass).
//  3. Determinism: two weave-ON runs with the same seed are bit-identical.
public class PedDemandWeaveTests
{
    private const double MaxSpeed = 1.4, Radius = 0.3, ArriveRadius = 0.3, ArrivalRadius = 0.5, Dt = 0.1, DwellSeconds = 0.5;
    private static readonly Vec2 WestNorthArm = new(112.6, 140.0);
    private static readonly Vec2 EastNorthArm = new(127.4, 140.0);

    private static SumoNavMesh BuildNav()
    {
        var polygons = WalkablePolygonBaker.Bake(LoadPoc0Network());
        return new SumoNavMesh(polygons, new SumoWalkableSpace(polygons));
    }

    private static PedNetwork LoadPoc0Network() => PedNetworkParser.Load(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "net.net.xml"),
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "walkable.add.xml"));

    private static PedLivelinessConfig LivelyAlwaysPauses() => new()
    {
        PauseProbability = 1.0,
        MinPauseSeconds = 1.0,
        MaxPauseSeconds = 3.0,
        MaxPausesPerTrip = 2,
        PauseAnimTag = "phone",
    };

    private static PedDemandConfig BuildConfig(bool enableWeave) => new()
    {
        Origins = new[] { WestNorthArm, EastNorthArm },
        Destinations = new[] { WestNorthArm, EastNorthArm },
        SpawnRatePerSecond = 2.0,
        PopulationCap = 8,
        Seed = 20260720UL,
        MaxSpeed = MaxSpeed,
        Radius = Radius,
        ArrivalRadius = ArrivalRadius,
        Liveliness = LivelyAlwaysPauses(),
        EnableWeave = enableWeave,
    };

    // (id, frame) -> world pose, over a fixed run.
    private static Dictionary<(int, int), Vec2> Run(bool enableWeave, SumoNavMesh nav)
    {
        var publisher = new PedPublisher();
        var manager = new PedLodManager(nav, publisher, ArriveRadius, DwellSeconds);
        var demand = new PedDemand(BuildConfig(enableWeave), nav, manager);
        var field = new InterestField();
        var noEntities = Array.Empty<WorldDisc>();

        var poses = new Dictionary<(int, int), Vec2>();
        for (var frame = 0; frame < 200; frame++)
        {
            var now = frame * Dt;
            demand.Step(now, Dt, field, noEntities);
            foreach (var id in demand.LiveIds)
            {
                poses[(id, frame)] = manager.PositionOf(id, now);
            }
        }

        return poses;
    }

    [Fact]
    public void WeaveOn_StaysWithinSidewalkHalfWidth_AndLeavesCentreline()
    {
        var nav = BuildNav();
        var off = Run(enableWeave: false, nav);
        var on = Run(enableWeave: true, nav);

        var maxLateral = 0.0;
        var compared = 0;
        foreach (var (key, centre) in off)
        {
            if (!on.TryGetValue(key, out var woven))
            {
                continue; // only compare matching (id, frame): spawn/pause structure is identical, so most match
            }

            compared++;
            var lateral = (woven - centre).Abs;
            maxLateral = System.Math.Max(maxLateral, lateral);

            // Clamp safety: the weave never pushes the ped further off the centreline than the baked
            // sidewalk half-width at that point allows.
            var halfWidth = nav.HalfWidthAt(centre);
            Assert.True(lateral <= halfWidth + 1e-6,
                $"woven pose {lateral:F3} m off centreline exceeds baked half-width {halfWidth:F3} m at {centre.X:F1},{centre.Y:F1}");
        }

        Assert.True(compared > 50, $"expected many matching (id,frame) samples; got {compared}");
        Assert.True(maxLateral > 0.2, $"weave should visibly leave the centreline; max lateral was {maxLateral:F3} m");
    }

    [Fact]
    public void WeaveOn_IsDeterministic()
    {
        var a = Run(enableWeave: true, BuildNav());
        var b = Run(enableWeave: true, BuildNav());

        Assert.Equal(a.Count, b.Count);
        foreach (var (key, pa) in a)
        {
            Assert.True(b.TryGetValue(key, out var pb), $"run B missing sample {key}");
            Assert.Equal(pa.X, pb.X, precision: 15);
            Assert.Equal(pa.Y, pb.Y, precision: 15);
        }
    }
}
