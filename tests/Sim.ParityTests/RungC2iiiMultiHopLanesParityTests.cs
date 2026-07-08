using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung C2-iii (multi-hop lane-to-lane continuity / route-wide best-lanes): the deferred second half
// of C2. scenarios/36-multihop-lanes is a 3-edge, 2-lane, 2-junction route E0->E1->E2 where E1->E2
// connects ONLY from E1_1, so reaching E2 requires being on E1_1, which requires inserting on E0_1 --
// a connection TWO hops out (E1->E2) dictates the E0 insertion lane. v0 departs E0_0 (departLane 0,
// the lane that dead-ends for the route). The pre-C2-iii single-look-ahead resolver threw
// "No <connection> found from edge 'E1' lane 0 to edge 'E2'".
//
// The port (NetworkModel.ComputeBestLanes' backward pass + the multi-connection pool threading, a
// port of MSVehicle::updateBestLanes MSVehicle.cpp:6003-6063) makes bestLaneOffset/Length route-wide,
// so E0_0 gets bestLaneOffset +1: v0 inserts on E0_0 and strategic-changes (the existing C2-ii
// TryStrategicLaneChange path) to E0_1 at t=8 (pos 111.12), then E1_1 (t=15), then E2_0 (t=29) --
// exact to 1e-3 against the SUMO v1_20_0 golden. Inert for the single-junction C2-ii case
// (scenario 18 unchanged) since a 2-edge route's backward pass reduces to the single-hop result.
//
// Runs the golden's full 44 steps (t=0..43): v0 clears the route by t=43.
public class RungC2iiiMultiHopLanesParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "36-multihop-lanes");

    [Fact]
    public void Run44Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(44);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-C2-iii multi-hop parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
        };

        foreach (var attribute in result.Attributes)
        {
            lines.Add(
                $"  attribute={attribute.Attribute} maxAbsError={attribute.MaxAbsError} rmse={attribute.Rmse} withinTolerance={attribute.WithinTolerance}");
        }

        if (result.PresenceMismatches.Count > 0)
        {
            lines.Add("  presence mismatches:");
            foreach (var mismatch in result.PresenceMismatches)
            {
                lines.Add($"    {mismatch.Kind} vehicle={mismatch.VehicleId} time={mismatch.Time?.ToString() ?? "n/a"}");
            }
        }

        return string.Join(Environment.NewLine, lines);
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
