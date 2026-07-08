using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// C11-ii: ACC (Adaptive Cruise Control) car-following model + carFollowModel dispatch --
// scenarios/23-acc-carfollow, both vTypes carFollowModel="ACC" on scenario 01's 1000m single
// lane (net = scenario 01's net). "lead" (maxSpeed=6) never has its own leader (single-lane, at
// the front of the route), so its own ACC state is never touched -- only "follow"'s. "follow"
// (default desired ~13.9) free-accelerates (bounded by IdmModel.FinalizeSpeed's -- see AccModel's
// FinalizeSpeed dispatch comment in Engine.cs -- accel cap, not ACC's own aggressive
// accelSpeedControl term) until the gap to "lead" drops under 120m (SPEED CONTROL), through the
// 100-120m HYSTERESIS band (reading the per-vehicle AccControlMode state), then under 100m (GAP
// CONTROL), settling behind the slow leader. sigma=0, Euler, actionStepLength=1.
public class RungC11AccParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "23-acc-carfollow");

    [Fact]
    public void Run70Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(70);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"RungC11 (ACC) parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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

    // Mirrors RungC11ParityTests.RepoRoot(): resolve the repo root by walking up from the test
    // assembly's location until Traffic.sln is found.
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
