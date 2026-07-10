using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung R4 (rail support -- the headline) trajectory parity test: a single-track MEET guarded by
// RAIL SIGNALS. Two rail trains converge on one shared bidi block (AB/BA) from opposite
// double-track ends, each end junction (A, B) a rail_signal. tL (depart 0) reserves and occupies
// the shared block; tR (depart 30), reaching signal B while the block is occupied by tL, is HELD
// AT THE SIGNAL (red) -- it brakes to a stop ~1 m before junction B (pos 997.379 on RB) and waits.
// Once tL's whole 135 m body clears the block, signal B goes green and tR proceeds (~t=133).
//
// This is the block-based "hold until the section ahead is clear" behaviour (MSRailSignal driveway
// reservation). NON-VACUOUS: the pre-port engine has NO rail signal -- it would throw
// KeyNotFoundException on the rail-signal connection (tl="B" with no <tlLogic>), and even guarded it
// would run tR straight through the block into a head-on collision with tL. Exact @1e-3 on
// lane,pos,speed. Mirrors RungA1ParityTests, pointed at scenarios/50-rail-signal-meet.
public class RungR4RailSignalMeetParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "50-rail-signal-meet");

    [Fact]
    public void Run220Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(220);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-R4 rail-signal parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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
