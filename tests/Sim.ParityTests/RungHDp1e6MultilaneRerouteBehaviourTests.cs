using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// P1E-6 acceptance (docs/HIGH-DENSITY-P1E-DESIGN.md §11, §0.5.2): the MULTI-LANE reroute anchor.
// scenarios/46-reroute-multilane is the 2-lane-prefix + 2-lane-detour variant of 45. With
// pre-insertion rerouting, SumoSharp routes every equipped vehicle onto the detour from departure
// (matching vanilla SUMO 1.20.0), so the ROUTE SPLIT is reproduced exactly.
//
// This is a BEHAVIOURAL gate (route choice), NOT a bit-exact trajectory gate, per the owner-approved
// acceptance model (§0.5.2): a *no-rerouting* 2-lane run with the same demand routed directly on the
// detour diverges from SUMO by the SAME ~7 m / 2.6 m/s (verified), i.e. the residual multi-lane
// pos/speed divergence is the PRE-EXISTING multi-lane lane-distribution / car-following gap
// (docs/NEED-multilane-density-willpass.md, NEED-multilane-junction-passage.md; the P2-G roadmap
// item) and is entirely independent of rerouting. Gating P1-E on it would wrongly couple rerouting
// to an unrelated open gap. scenarios/45-reroute-congestion is the SINGLE-LANE bit-exact anchor for
// the reroute mechanism; this test asserts rerouting produces SUMO's route split on a multi-lane net.
public class RungHDp1e6MultilaneRerouteBehaviourTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "46-reroute-multilane");

    [Fact]
    public void MultilaneReroute_RouteSplit_MatchesGolden()
    {
        var engine = new Engine();
        engine.LoadScenario(Path.Combine(ScenarioDir, "config.sumocfg"));
        var actual = engine.Run(300);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));

        var goldenDetour = VehiclesUsingEdge(golden, "e_det2");
        var goldenShort = VehiclesUsingEdge(golden, "e_short");
        var actualDetour = VehiclesUsingEdge(actual, "e_det2");
        var actualShort = VehiclesUsingEdge(actual, "e_short");

        // SUMO diverts ALL flow vehicles to the detour (none on the congested short edge).
        Assert.NotEmpty(goldenDetour);
        Assert.Empty(goldenShort);

        // SumoSharp must reproduce that exact route split: same set of vehicles on the detour, and
        // (the load-bearing pre-insertion assertion) NONE stranded on the slow short edge.
        Assert.Equal(goldenShort, actualShort);   // both empty
        Assert.Equal(goldenDetour, actualDetour);
    }

    [Fact]
    public void MultilaneReroute_AllVehiclesArrive_LikeGolden()
    {
        var engine = new Engine();
        engine.LoadScenario(Path.Combine(ScenarioDir, "config.sumocfg"));
        var actual = engine.Run(300);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));

        // Throughput parity: the same set of vehicles appears (all depart and are simulated), and
        // both engines drain the network within the run (nothing left permanently stuck).
        Assert.Equal(
            golden.VehicleIds.OrderBy(x => x, StringComparer.Ordinal),
            actual.VehicleIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    // The set of vehicle ids that occupy any lane of `edgeId` at some point in the run.
    private static SortedSet<string> VehiclesUsingEdge(TrajectorySet set, string edgeId)
    {
        var prefix = edgeId + "_";
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var p in set.AllPoints)
        {
            if (p.Lane.StartsWith(prefix, StringComparison.Ordinal))
            {
                result.Add(p.VehicleId);
            }
        }

        return result;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Traffic.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (Traffic.sln not found above test assembly).");
    }
}
