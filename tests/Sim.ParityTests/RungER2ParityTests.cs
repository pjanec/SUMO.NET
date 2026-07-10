using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung ER2's parity tests (emergency ignore-FOE at a priority junction). Both scenarios reuse
// scenario 11-priority-junction's net + demand but make the yielding MINOR vehicle an EMERGENCY
// vType with jmIgnoreFoeProb=1, jmIgnoreFoeSpeed=100, jmIgnoreJunctionFoeProb=1 (all probs 1.0
// => deterministic, so exact @1e-3 parity). The two scenarios isolate the two ignore gates:
//
//   51-emergency-foe            -- both vehicles depart t=0, so at vMinor's crossing decision the
//                                  priority foe vMajor is already ON its internal lane :J_2_0.
//                                  vMinor's yield is the ON-JUNCTION link-leader arm
//                                  (MSVehicle::checkLinkLeaderCurrentAndParallel, MSVehicle.cpp:
//                                  3430); with jmIgnoreJunctionFoeProb it is ignored, so vMinor
//                                  crosses at free-flow instead of holding.
//   52-emergency-foe-approaching -- the priority foe vMajor departs t=4, so at vMinor's crossing
//                                  decision vMajor is still APPROACHING on lane WJ. vMinor's yield
//                                  is the approaching-foe stop-line yield (MSLink::opened /
//                                  blockedAtTime, MSLink.cpp:898-902); with jmIgnoreFoeProb +
//                                  jmIgnoreFoeSpeed it is ignored, so vMinor crosses at free-flow.
//
// The cautious minor-link approach (a foe-independent, visibility-based deceleration toward the
// stop line) is NOT gated by the ignore and is therefore identical to scenario 11 through t=17;
// the divergence is only in whether vMinor keeps yielding afterward. Non-vacuous: with either
// gate disabled the emergency vehicle yields exactly like scenario 11's passenger.
public class RungER2ParityTests
{
    [Theory]
    [InlineData("51-emergency-foe")]
    [InlineData("52-emergency-foe-approaching")]
    public void Run_MatchesGoldenFcdWithinTolerance(string scenario)
    {
        var scenarioDir = Path.Combine(RepoRoot(), "scenarios", scenario);

        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(scenarioDir, "net.net.xml"),
            Path.Combine(scenarioDir, "rou.rou.xml"),
            Path.Combine(scenarioDir, "config.sumocfg"));

        var actual = engine.Run(60);
        var golden = FcdParser.Parse(Path.Combine(scenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(scenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(scenario, result));
    }

    private static string BuildFailureMessage(string scenario, ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-ER2 parity FAILED ({scenario}). FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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
