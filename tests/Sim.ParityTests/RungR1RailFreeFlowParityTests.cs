using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung R1 (rail support) trajectory parity test: runs the engine's Krauss port on a single
// free-running TRAIN (vClass="rail", carFollowModel="Krauss") on a straight track and compares
// against golden.fcd.xml within tolerance.json's bounds. This is the rail analog of
// Rung1ParityTests / RungA1ParityTests (truck): it proves VTypeDefaults.Resolve now resolves the
// rail vClass defaults (accel=0.25, decel=1.3, minGap=5, length=135, sigma=0 -- see
// scenarios/47-rail-free-flow/golden.vtype.json) AND that the existing Krauss/integration core
// reproduces the resulting rail free-flow trajectory exactly. NON-VACUOUS: the pre-port engine
// threw NotSupportedException on vClass="rail", so it could not even load this scenario.
// Mirrors RungA1ParityTests.cs exactly, pointed at scenarios/47-rail-free-flow.
public class RungR1RailFreeFlowParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "47-rail-free-flow");

    [Fact]
    public void Run80Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(80);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-R1 rail parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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

    // Mirrors EngineRung1PlumbingTests.RepoRoot(): resolve the repo root by walking up from
    // the test assembly's location until Traffic.sln is found.
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
