using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung C4-i (TASKS.md "Remaining right-of-way" -- the RIGHT-BEFORE-LEFT sub-rung): 9b did PRIORITY
// junctions; this covers an uncontrolled SYMMETRIC junction where a vehicle yields to traffic
// approaching from its right. scenarios/26-right-before-left is a 4-arm uncontrolled cross J
// (node type `right_before_left`): the W->E through (vWest) and the S->N through (vSouth) cross.
// Both depart at t=0, pos 0, 13.89, sigma=0 -- a deliberate simultaneous-arrival conflict.
//
// netconvert resolves the RBL junction into a request matrix that is priority-like from each
// vehicle's own perspective: link 0 (SJ->JN, vSouth) is MAJOR (`response="00"`, state "M") and
// link 1 (WJ->JE, vWest) is EQUAL (state "="), yielding to link 0 (`response="01"`). vSouth is on
// vWest's RIGHT (vWest heads east; its right is the south leg), so vWest yields -- exactly the
// real-world right-before-left rule, encoded in the matrix. Because Engine.JunctionYieldConstraint
// is driven ENTIRELY by that <request> matrix (RespondsTo / Response), the SAME machinery 9b +
// C3 built already handles RBL with no new code: vSouth cruises through at 13.89 (major, never
// yields); vWest cautiously approaches then junction-leader-follows vSouth across the crossing
// (13.89 -> 9.6097 -> 5.1097), enters its own internal lane `:J_1_0` at t=16 once vSouth has
// cleared, and re-accelerates. This rung is thus an ADDITIVE parity anchor: it proves the
// matrix-driven yield generalizes from the `m`/`M` (priority) states to the `=`/`M` (RBL) states
// with byte-exact behavior, not a new algorithm.
//
// Runs 31 steps: vSouth clears the network by t=28 and vWest (delayed by the yield) by t=30 in the
// golden; 31 compares every populated golden row for both vehicles.
public class RungC4iRightBeforeLeftParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "26-right-before-left");

    [Fact]
    public void Run31Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(31);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-C4-i parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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
