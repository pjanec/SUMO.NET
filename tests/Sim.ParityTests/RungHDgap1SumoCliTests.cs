using Sim.Harness;
using Sim.Sumo;
using Xunit;

namespace Sim.ParityTests;

// GAP-1 (docs/SUMOSHARP-SERVE-PATH-DROP-IN.md §1, docs/SERVE-PATH-PLAN.md): the sumo-compatible CLI
// shim. Drives the shim IN-PROCESS (SumoShim.Run) exactly as SumoData would shell out, over the
// multi-file-cfg scenario 41-multifile-cfg (<input> = net + demand.rou + vtypes.rou + extra.add),
// and proves two things:
//   1. the CLI wiring drives the engine IDENTICALLY to the committed golden -- the shim-produced
//      --fcd-output is parity-checked against golden.fcd.xml through the same FcdParser/tolerance
//      machinery the plain-engine parity tests use. If the CLI mis-wired the load or step count, this
//      fails.
//   2. the vanilla flag CONTRACT holds: -c, --end (time, not step count), --summary-output,
//      --statistic-output, --tripinfo-output (accepted), --no-step-log (accepted+ignored), and an
//      UNKNOWN flag is tolerated (exit 0, warning) rather than aborting.
public class RungHDgap1SumoCliTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "41-multifile-cfg");

    [Fact]
    public void ServeShape_ProducesSummaryStatisticAndTripinfoIsAccepted_UnknownFlagTolerated()
    {
        var outDir = NewTempDir();
        try
        {
            var summary = Path.Combine(outDir, "S.xml");
            var statistic = Path.Combine(outDir, "T.xml");
            var tripinfo = Path.Combine(outDir, "TI.xml");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            // The exact serve/verify invocation shape SumoData uses, plus a deliberately unknown flag.
            var exit = SumoShim.Run(
                new[]
                {
                    "-c", Path.Combine(ScenarioDir, "config.sumocfg"),
                    "--summary-output", summary,
                    "--statistic-output", statistic,
                    "--tripinfo-output", tripinfo,
                    "--end", "120",
                    "--no-step-log", "true",
                    "--some-future-flag", "ignored-value",
                },
                stdout, stderr);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(summary), "summary-output not produced");
            Assert.True(File.Exists(statistic), "statistic-output not produced");
            // GAP-1: tripinfo is ACCEPTED but not yet produced (GAP-2 delivers the writer). The run
            // must not abort, and the notice must be surfaced.
            Assert.False(File.Exists(tripinfo), "tripinfo should not be produced until GAP-2");
            Assert.Contains("--tripinfo-output", stderr.ToString());
            // Unknown flag tolerated with a warning, not an abort.
            Assert.Contains("--some-future-flag", stderr.ToString());

            // The statistic file is the SUMO schema and phase-1 (teleport off) reports 0 teleports.
            var stats = StatisticOutputParser.Parse(statistic);
            Assert.Equal(0, stats.TeleportsTotal);
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void FcdShape_ProducedFcdMatchesGoldenWithinTolerance()
    {
        var outDir = NewTempDir();
        try
        {
            var fcd = Path.Combine(outDir, "F.xml");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exit = SumoShim.Run(
                new[]
                {
                    "-c", Path.Combine(ScenarioDir, "config.sumocfg"),
                    "--fcd-output", fcd,
                    "--end", "120",
                    "--no-step-log", "true",
                },
                stdout, stderr);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(fcd), "fcd-output not produced");

            // The CLI-produced FCD must match the committed golden within the scenario's tolerance --
            // proving the shim drives the engine identically to the plain-engine parity path.
            var actual = FcdParser.Parse(fcd);
            var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
            var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

            var result = TrajectoryComparator.Compare(actual, golden, tolerance);

            Assert.True(result.IsMatch,
                "GAP-1 CLI-produced FCD parity FAILED for 41-multifile-cfg. " +
                $"FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}");
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void EndFlag_ControlsRunLength_FewerStepsProduceShorterFcd()
    {
        var outDir = NewTempDir();
        try
        {
            var fcd = Path.Combine(outDir, "F.xml");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            // --end is a TIME, not a step count: with step-length 1 this is 30 timesteps (0..29),
            // strictly fewer than the golden's 120 -- the proof that --end drives run length.
            var exit = SumoShim.Run(
                new[]
                {
                    "-c", Path.Combine(ScenarioDir, "config.sumocfg"),
                    "--fcd-output", fcd,
                    "--end", "30",
                },
                stdout, stderr);

            Assert.Equal(0, exit);
            var producedSteps = FcdParser.Parse(fcd).AllPoints.Select(p => p.Time).Distinct().Count();
            var goldenSteps = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"))
                .AllPoints.Select(p => p.Time).Distinct().Count();
            Assert.True(producedSteps < goldenSteps,
                $"--end=30 should produce fewer distinct timesteps ({producedSteps}) than the 120s golden ({goldenSteps}).");
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void MissingConfig_IsReportedNotThrown()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = SumoShim.Run(new[] { "--fcd-output", "x.xml", "--end", "10" }, stdout, stderr);
        Assert.Equal(1, exit);
        Assert.Contains("no configuration", stderr.ToString());
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sumosharp-gap1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
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
