using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// P0-D (docs/HIGH-DENSITY-P0-DESIGN.md "P0-D"): engine writers for `--summary-output` /
// `--statistic-output`, acceptance scenario scenarios/44-summary-output (a 5-vehicle platoon from
// 05-platoon-shockwave whose leader has a <stop>; golden regenerated from real SUMO 1.20.0).
// Registers a SummaryWriterObserver on the SAME D9 export seam Rung7ParityTests' plain engine.Run
// already exercises for FCD, so this compares an IN-MEMORY step-series (SummaryWriterObserver.
// Records) against the parsed golden.summary.xml -- no file round-trip needed, mirroring how
// engine.Run(100) hands the FCD parity test an in-memory TrajectorySet directly.
public class RungHDp0dSummaryOutputParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "44-summary-output");

    [Fact]
    public void Run100Steps_SummaryStepSeries_MatchesGoldenWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        using var summaryWriter = new SummaryWriterObserver();
        engine.AddExportObserver(summaryWriter);
        engine.Run(100);

        var golden = SummaryOutputParser.Parse(Path.Combine(ScenarioDir, "golden.summary.xml"));

        var result = SummaryComparator.Compare(summaryWriter.Records, golden);

        Assert.True(result.IsMatch,
            "scenario 44 summary-output parity FAILED. " +
            $"FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}; " +
            $"missingSteps=[{string.Join(",", result.MissingSteps)}]; extraSteps=[{string.Join(",", result.ExtraSteps)}]; " +
            string.Join(" ", result.Attributes.Select(a =>
                $"{a.Attribute}(maxAbs={a.MaxAbsError:G6},rmse={a.Rmse:G6},ok={a.WithinTolerance})")));
    }

    // docs/HIGH-DENSITY-P0-DESIGN.md "P0-D": teleports total = 0 pre-P1-F -- phase 1 runs
    // time-to-teleport=-1 (CLAUDE.md "Determinism (phase 1)": teleport off), so Engine.TeleportCount
    // must still read 0 after a full run, matching golden.statistic.xml's own <teleports total="0"/>.
    [Fact]
    public void Run100Steps_TeleportCount_MatchesGoldenStatisticTeleportsTotal()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        engine.Run(100);

        var golden = StatisticOutputParser.Parse(Path.Combine(ScenarioDir, "golden.statistic.xml"));

        Assert.Equal(golden.TeleportsTotal, engine.TeleportCount);
        Assert.Equal(0, engine.TeleportCount);
    }

    // Convenience acceptance check (optional per the task brief -- 05-platoon-shockwave's own
    // Rung7ParityTests already covers this trajectory) confirming scenario 44's FCD golden is
    // ALSO byte-for-byte the same platoon+stop scenario, so a summary-parity failure here can be
    // localized to the summary aggregation itself rather than a trajectory divergence.
    [Fact]
    public void Run100Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(100);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch,
            $"scenario 44 FCD parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}");
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
