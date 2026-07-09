using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung C4-vii (multi-lane junction passage) -- PENDING. scenarios/44-multilane-junction-turn is the
// committed, deterministic, network-free minimal repro the C4-vii done-condition asks to "build the
// anchor FIRST". A single symmetric 2-lane priority crossroads with four vehicles, one per approach,
// ALL turning left, departing together from rest. SUMO flows all four (staggered junction entry
// t=17..20, everyone clears by t=38, no teleport); the current engine (baseline f378d3a) diverges:
//   (A) the left-turn internal path collapses (:C_3_0 only, skipping the internal-junction lane
//       :C_16_0 -- cont="1" links are not fully modeled), and
//   (B) a state-dependent spurious final-edge lane change (CE_1 -> CE_0 at ~t=29) strands vN at the
//       lane end (pos 189.6, speed 0) so it NEVER arrives -- a permanent stuck.
// (A lone left-turner does NOT reproduce (B), so it is an interaction bug, not static arrival-lane
// resolution.) See scenarios/44-.../provenance.txt for the full diagnosis, and TASKS.md C4-vii for
// the decomposition (bugs A/B here; the willPass gridlock bug C needs a separate cyclic anchor).
//
// This test is Skip-gated until C4-vii lands. Unskip it (and drop this banner to a normal rung
// header) once the engine flows the anchor exact @1e-3 -- it must then pass with NO tolerance change.
public class RungC4viiMultilaneJunctionParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "44-multilane-junction-turn");

    [Fact(Skip = "C4-vii pending: multi-lane junction passage cluster (cont-internal-lane path + spurious final-edge lane change + willPass gridlock) -- see TASKS.md and scenarios/44-multilane-junction-turn/provenance.txt")]
    public void Run45Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(45);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"Rung-C4-vii multi-lane-junction parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
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
