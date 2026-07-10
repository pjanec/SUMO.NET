using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung R6 (rail support -- the last, hardest rung) trajectory parity test: the MSCFModel_Rail
// TRACTION model. A single free-running train with carFollowModel="Rail" (parametric traction
// maxPower/maxTraction + resistance resCoef_* curves, trainType "custom") accelerates on a straight
// track. Unlike Krauss (a constant accel bound), the acceleration is speed-dependent -- strong at
// low speed, tapering as speed rises and resistance grows -- so this is a distinctly non-Krauss
// accel profile that only the ported traction model (RailModel) can reproduce. Exact @1e-3 on
// lane,pos,speed. Mirrors RungA1ParityTests, pointed at scenarios/52-rail-traction.
//
// NON-VACUOUS: the pre-port engine had no Rail CF model -- carFollowModel="Rail" would resolve to
// the Krauss path and produce a CONSTANT-accel profile, diverging from the golden's tapering
// traction curve within a few steps. Scope: the free-running traction profile (parametric curves,
// flat track, Euler). The moving-block leader followSpeed, station dwell, and reversal are deferred.
//
// Note: no golden.vtype.json is committed for this scenario (the dump helper hardcodes
// carFollowModel="Krauss" and reports the Rail model's sigma as -1, so it is not a valid Rail
// cross-check reference -- same pattern as the IDM/ACC/CACC scenarios 22-25). The trajectory parity
// validates the resolved Rail params (weight/massFactor/maxPower/maxTraction/resCoef) end-to-end.
public class RungR6RailTractionParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "52-rail-traction");

    [Fact]
    public void Run120Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(120);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-R6 rail-traction parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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
