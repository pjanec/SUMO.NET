using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core;
using Sim.Core.Bridge;
using Sim.Core.Orca;
using Sim.Pedestrians.Demand;
using Sim.Pedestrians.Lod;
using Sim.Pedestrians.Navigation.Bake;
using Xunit;
using Xunit.Abstractions;

namespace Sim.Pedestrians.Tests.Demand;

// P8-3b (docs/PEDESTRIAN-P8-3-DEMAND-DESIGN.md §4/§5): wiring SubareaDemand into PedDemand behind the
// optional WeightedEndpoints. Drives the REAL box crop (scenarios/_ped/subarea-box) end-to-end: POIs +
// walkable fringe -> SubareaDemand -> PedDemand -> baked navmesh. Proves (a) inert-default -- null takes
// the uniform Origins/Destinations path unchanged; (b) when set, every spawn's origin AND destination is a
// fringe/POI endpoint (appearance legitimacy by construction, the P8-3 x P8-2 synergy); (c) the population
// respects the cap; (d) the run is deterministic. Hermetic: committed net.xml + manifest.json + pois.json.
public class SubareaWeightedDemandTests
{
    private readonly ITestOutputHelper _out;

    public SubareaWeightedDemandTests(ITestOutputHelper output) => _out = output;

    private const double MaxSpeed = 1.4;
    private const double Radius = 0.3;
    private const double ArriveRadius = 0.3;
    private const double ArrivalRadius = 0.5;
    private const double Dt = 0.1;
    private const double DwellSeconds = 0.5;

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Traffic.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }

    private static string BoxDir() => Path.Combine(RepoRoot(), "scenarios", "_ped", "subarea-box");

    private static SumoNavMesh BuildBoxNav()
    {
        var network = PedNetworkParser.Load(Path.Combine(BoxDir(), "net.xml"));
        var polygons = WalkablePolygonBaker.Bake(network);
        return new SumoNavMesh(polygons, new SumoWalkableSpace(polygons));
    }

    private static SubareaDemand BuildBoxDemand()
    {
        var pois = PedPoiReader.LoadJson(Path.Combine(BoxDir(), "pois.json"));
        var network = PedNetworkParser.Load(Path.Combine(BoxDir(), "net.xml"));
        var manifest = SubareaManifest.Load(Path.Combine(BoxDir(), "manifest.json"));
        var fringe = SubareaDemand.FringeEndpointsFromNetwork(network, manifest.WalkableFringeEdges);
        return SubareaDemand.Build(pois, fringe, fringeWeight: 1.0);
    }

    // ---- Inert default: WeightedEndpoints=null takes the uniform Origins/Destinations path -------------

    [Fact]
    public void NullWeightedEndpoints_DrawsFromUniformOriginsDestinations_Unchanged()
    {
        var nav = BuildBoxNav();
        var manager = new PedLodManager(nav, new PedPublisher(), ArriveRadius, DwellSeconds);

        // Two fringe points as the classic uniform O/D set; WeightedEndpoints is left null.
        var demand = BuildBoxDemand();
        var a = demand.Endpoints.First(e => e.IsFringe).Pos;
        var b = demand.Endpoints.Last(e => e.IsFringe).Pos;

        var config = new PedDemandConfig
        {
            Origins = new[] { a, b },
            Destinations = new[] { a, b },
            SpawnRatePerSecond = 3.0,
            PopulationCap = 8,
            Seed = 555UL,
            MaxSpeed = MaxSpeed,
            Radius = Radius,
            ArrivalRadius = ArrivalRadius,
            // WeightedEndpoints deliberately omitted -> null (inert default)
        };
        var ped = new PedDemand(config, nav, manager);

        var field = new InterestField();
        var noEntities = Array.Empty<WorldDisc>();
        var now = 0.0;
        for (var i = 0; i < 400; i++)
        {
            ped.Step(now, Dt, field, noEntities);
            now += Dt;
        }

        Assert.True(ped.SpawnCount > 0, "uniform path produced no spawns");

        // Every spawn's O/D is one of the two uniform points -- the null path never consulted the endpoint set.
        foreach (var e in ped.SpawnEvents)
        {
            Assert.True(Coincides(e.Origin, a) || Coincides(e.Origin, b), "origin not a uniform point");
            Assert.True(Coincides(e.Destination, a) || Coincides(e.Destination, b), "destination not a uniform point");
        }

        _out.WriteLine($"[P8-3b] inert-default: {ped.SpawnCount} spawns all from the 2 uniform points");
    }

    // ---- WeightedEndpoints set: legitimacy by construction + cap + non-vacuous routing ----------------

    [Fact]
    public void WeightedEndpoints_EverySpawnOriginAndDestinationIsAFringeOrPoiEndpoint_AndCapHeld()
    {
        var nav = BuildBoxNav();
        var manager = new PedLodManager(nav, new PedPublisher(), ArriveRadius, DwellSeconds);
        var demand = BuildBoxDemand();
        const int cap = 12;

        var config = new PedDemandConfig
        {
            Origins = Array.Empty<Vec2>(),        // supplied by WeightedEndpoints
            Destinations = Array.Empty<Vec2>(),
            SpawnRatePerSecond = 4.0,
            PopulationCap = cap,
            Seed = 20240719UL,
            MaxSpeed = MaxSpeed,
            Radius = Radius,
            ArrivalRadius = ArrivalRadius,
            WeightedEndpoints = demand,
        };
        var ped = new PedDemand(config, nav, manager);

        // The legitimate endpoint positions -- every spawn O/D must be one of these.
        var endpointPositions = demand.Endpoints.Select(e => e.Pos).ToArray();

        var field = new InterestField();
        var noEntities = Array.Empty<WorldDisc>();
        var now = 0.0;
        var maxLive = 0;
        for (var i = 0; i < 1500; i++)
        {
            ped.Step(now, Dt, field, noEntities);
            now += Dt;
            maxLive = Math.Max(maxLive, ped.LiveCount);
            Assert.True(ped.LiveCount <= cap, $"live population {ped.LiveCount} exceeded cap {cap}");
        }

        Assert.True(ped.SpawnCount > 0, "weighted demand produced no spawns (routing vacuous?)");

        // Legitimacy by construction: every spawn's origin AND destination is a fringe/POI endpoint.
        foreach (var e in ped.SpawnEvents)
        {
            Assert.True(endpointPositions.Any(p => Coincides(p, e.Origin)),
                $"spawn {e.Id} origin ({e.Origin.X:F2},{e.Origin.Y:F2}) is not a fringe/POI endpoint");
            Assert.True(endpointPositions.Any(p => Coincides(p, e.Destination)),
                $"spawn {e.Id} destination ({e.Destination.X:F2},{e.Destination.Y:F2}) is not a fringe/POI endpoint");
            Assert.False(Coincides(e.Origin, e.Destination), $"spawn {e.Id} has zero-length O==D");
        }

        Assert.Equal(ped.SpawnCount - ped.ArrivalCount, ped.LiveCount); // no leak / no phantom

        _out.WriteLine($"[P8-3b] weighted: spawns={ped.SpawnCount} arrivals={ped.ArrivalCount} " +
                       $"maxLive={maxLive}/{cap} unreachableSkips={ped.UnreachableSkipCount}; all O/D legitimate");
    }

    // ---- Determinism: identical seed -> identical spawn/arrival stream --------------------------------

    [Fact]
    public void WeightedEndpoints_IsDeterministic_AcrossIndependentRuns()
    {
        var (s1, a1) = RunWeighted();
        var (s2, a2) = RunWeighted();

        Assert.Equal(s1.Count, s2.Count);
        Assert.True(s1.Count > 0);
        for (var i = 0; i < s1.Count; i++)
        {
            Assert.Equal(s1[i].Id, s2[i].Id);
            Assert.Equal(s1[i].Time, s2[i].Time, precision: 12);
            Assert.Equal(s1[i].Origin.X, s2[i].Origin.X, precision: 12);
            Assert.Equal(s1[i].Origin.Y, s2[i].Origin.Y, precision: 12);
            Assert.Equal(s1[i].Destination.X, s2[i].Destination.X, precision: 12);
            Assert.Equal(s1[i].Destination.Y, s2[i].Destination.Y, precision: 12);
        }

        Assert.Equal(a1.Count, a2.Count);
        for (var i = 0; i < a1.Count; i++)
        {
            Assert.Equal(a1[i].Id, a2[i].Id);
            Assert.Equal(a1[i].Time, a2[i].Time, precision: 12);
        }

        _out.WriteLine($"[P8-3b] determinism: {s1.Count} spawns / {a1.Count} arrivals bit-identical across two runs");
    }

    private static (IReadOnlyList<PedSpawnEvent> Spawns, IReadOnlyList<PedArrivalEvent> Arrivals) RunWeighted()
    {
        var nav = BuildBoxNav();
        var manager = new PedLodManager(nav, new PedPublisher(), ArriveRadius, DwellSeconds);
        var config = new PedDemandConfig
        {
            Origins = Array.Empty<Vec2>(),
            Destinations = Array.Empty<Vec2>(),
            SpawnRatePerSecond = 3.0,
            PopulationCap = 8,
            Seed = 77UL,
            MaxSpeed = MaxSpeed,
            Radius = Radius,
            ArrivalRadius = ArrivalRadius,
            WeightedEndpoints = BuildBoxDemand(),
        };
        var ped = new PedDemand(config, nav, manager);
        var field = new InterestField();
        var noEntities = Array.Empty<WorldDisc>();
        var now = 0.0;
        for (var i = 0; i < 900; i++)
        {
            ped.Step(now, Dt, field, noEntities);
            now += Dt;
        }

        return (new List<PedSpawnEvent>(ped.SpawnEvents), new List<PedArrivalEvent>(ped.ArrivalEvents));
    }

    private static bool Coincides(Vec2 a, Vec2 b) => (a - b).Abs < 1e-9;
}
