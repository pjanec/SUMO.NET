using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// Rung C8-i (ballistic integration, free flow): scenarios/21-ballistic-freeflow runs with
// step-method.ballistic=true. Ballistic differs from Euler ONLY in the position update
// (trapezoidal pos += 0.5*(oldSpeed+newSpeed)*dt vs Euler pos += newSpeed*dt); the free-flow
// speed sequence is identical to Euler. Exact-parity vs SUMO's ballistic golden. The ballistic
// safe-speed branches (car-following/stop) are deferred to a ballistic-with-leader scenario.
public class RungC8ParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "21-ballistic-freeflow");

    [Fact]
    public void Run20Steps_MatchesBallisticGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ScenarioDir, "net.net.xml"),
            Path.Combine(ScenarioDir, "rou.rou.xml"),
            Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(20);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch,
            $"Rung-C8 ballistic parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}; " +
            string.Join("; ", result.Attributes.Select(a => $"{a.Attribute} maxAbs={a.MaxAbsError} ok={a.WithinTolerance}")));
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
