using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung C5 (keepClear / don't-block-the-box): a vehicle must not enter a junction it cannot clear.
// scenarios/34-keepclear is a 4-way priority cross where mBlock sits STOPPED on the exit edge JE,
// jamming it. mThrough (W->E, major) must stop at the junction ENTRY (WJ@91.8 = WJ.len 92.80 -
// DIST_TO_STOPLINE_EXPECT_PRIORITY 1.0) rather than creep onto the internal lane :J_1 and block the
// box. keepClear fires because the WJ->JE link has crossing foe LINKS (N->S) -- MSVehicle::keepClear's
// link->hasFoes() -- even with no crossing vehicle present.
//
// Port = Engine.KeepClearConstraint, the "removal" half of MSVehicle::checkRewindLinkLanes
// (MSVehicle.cpp:5025): it walks ego's downstream exit chain (subtracting internal-lane brutto
// vehicle-length sums, adding each exit lane's getSpaceTillLastStanding) and, when a stopped vehicle
// leaves leftSpace = availableSpace - lengthWithGap < 0, brakes ego to the junction-entry stop line.
// VERIFIED byte-exact against the vendored v1_20_0 DEBUG_CHECKREWINDLINKLANES trace (exit JE stls=1.0,
// avail=1.0, leftSpace=1.0-7.5=-6.5, removalBegin=0). The pre-C5 engine has no downstream-space
// accounting and drives mThrough straight through the junction (and through the stopped mBlock) --
// stash-test territory.
//
// Runs the golden's full 40 steps (t=0..39): mThrough brakes 13.89 -> 0 by t=13 and stays at WJ@91.8
// (JE is blocked for the whole run).
public class RungC5KeepClearParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "34-keepclear");

    [Fact]
    public void Run40Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(40);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-C5 keepClear parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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
