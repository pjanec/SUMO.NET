using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung C2-vi (complete route->lane resolution for a general -L2 city): the exit lane a vehicle
// leaves an edge on MUST have a <connection> to the route's NEXT edge -- even when its
// bestLaneOffset (a per-edge downstream hint) points at a sibling lane that does not connect.
// scenarios/41-forced-turn-lane: E0(2 lanes)->E1(2 lanes)->E2(1 lane); E0_0 is the ONLY lane
// connecting to E1, but ComputeBestLanes gives E0_0 a nonzero offset (+1) inherited from E1_0's own
// +1 (E1_0 dead-ends off-route; only E1_1->E2 continues). The pre-C2-vi ResolveSequenceCore applied
// that offset and chose E0's non-connecting lane 1 as the exit -> threw at insertion (the exact bug
// a general `netgenerate -L2` city hits, e.g. route C1B1 B1B2 B2A2). The fix picks the CONNECTING
// lane nearest the offset target, so E0's exit stays lane 0; the vehicle arrives E1_0 and
// strategic-LCs to E1_1 for the E2 turn.
//
// SUMO golden: E0_0 (t<=13), :J_0_0 (t=14), E1_1 (t=15 -- the immediate post-junction strategic LC
// off the dead-end E1_0), :K_1_0 (t=29), E2_0 (t=30). Distinct from scenario 36 (E0_0 redirected TO
// a connecting sibling) and scenario 37 (1-lane depart edge, mid-edge change). Runs 44 steps
// (t=0..43, v0's last golden frame).
public class RungC2viForcedTurnLaneParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "41-forced-turn-lane");

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
            $"Rung-C2-vi forced-turn-lane parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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
