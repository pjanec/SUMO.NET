using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung A3's parity test: an emergency vehicle (vClass=emergency, sigma=0,
// jmDriveAfterRedTime="1000") drives route "WJ JE" through the same traffic-light-controlled
// junction J as rung 10 (09-traffic-light) while the signal is red for sim time [0, 30) then
// green. Unlike rung 10's passenger (which halts at the stop line, WJ_0 pos 298.999), this
// vehicle's jmDriveAfterRedTime (1000) exceeds the red's elapsed time throughout the whole red
// phase (redDuration never reaches 30), so MSVehicle::ignoreRed's jm-privilege arm fires on
// every step and the vehicle drives straight through at free-flow, crossing onto JE_0 by
// t~=24 (MSVehicle.cpp:7266). Runs the scenario's full length (end=50, step-length=1 => 50
// steps) and compares against golden.fcd.xml within tolerance.
public class RungA3ParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "16-emergency-red");

    [Fact]
    public void Run50Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(50);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-A3 parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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
