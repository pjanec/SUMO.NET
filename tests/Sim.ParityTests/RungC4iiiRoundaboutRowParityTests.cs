using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung C4-iii (junction arrival-time right-of-way): the two-vehicle priority roundabout. vWest
// circulates through node RS (ring priority 10) while vSouth enters at RS (approach priority 1)
// toward the same east exit -- a sameTarget merge where vSouth must yield. Unlike scenarios 29/31
// (where the foe is already ON the merge lane when ego is within visibility, so the merge-LEADER
// path handles it), here vSouth must stop-line yield to vWest while vWest is still APPROACHING the
// merge (circulating on the ring's approach lane, not yet on its internal lane :RS_1).
//
// That is SUMO's junction arrival-time right-of-way (MSLink::opened/blockedByFoe, MSLink.cpp:747-
// 1013): ego is blocked iff a responded foe within its approach-reservation range has an
// arrival-time window at the conflict overlapping ego's, within a 1 s lookAhead. Port =
// Engine.SameTargetMergeConstraint's PHASE-0 arm (KraussModel.MinimalArrivalTime +
// Engine.BlockedByMergeFoe + the reservation-distance gate), VERIFIED per-step against the vendored
// v1_20_0 MSLink_DEBUG_OPENED trace: `blocked (hard conflict)` for vSouth at t=14..18 (vWest
// approaching), then at t=19 vWest is ON :RS_1 and the existing merge-leader (PHASE 1) takes over --
// vSouth brakes 13.89 -> 2.48, stops (0.00) at the give-way, then follows vWest out at 2.60. The
// reservation-distance gate is what keeps a DISTANT responded foe from blocking (the scenario-19
// mainline, ~362 m away, never reserves the merge link -> vSouth's onramp analog does not yield).
// Golden regenerated in-session from SUMO 1.20.0 (Euler, sigma=0, teleport off, seed 42).
//
// Runs 38 steps: both vehicles have cleared to the east exit by t=37 in the golden.
public class RungC4iiiRoundaboutRowParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "32-roundabout");

    [Fact]
    public void Run38Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(38);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-C4-iii (arrival-time RoW) parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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
