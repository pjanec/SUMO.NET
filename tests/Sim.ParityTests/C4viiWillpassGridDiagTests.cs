using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// C4-vii DIAGNOSTIC (not a parity anchor). scenarios/_diag/c4vii-willpass-grid is a committed 6x6
// -L2 priority grid (netgenerate --grid.number=6 --grid.length=250 -L2, priority junctions; 75
// duarouter trips) that reproduces BOTH open C4-vii sub-bugs deterministically:
//   1. It PARKS vehicles on 2-lane junction-internal lanes (via junction yielding), which is exactly
//      the path that crashed C4-vii-b's keep-right (ComputeBestLanes on an internal edge). So the
//      "runs 600 steps WITHOUT THROWING" assertion below is the crash regression test that a minimal
//      single-vehicle scenario could not provide (a free-flowing vehicle crosses the short internal
//      lane within one step, so the post-move keep-right phase never samples it there).
//   2. SUMO runs this net at free flow (0 of 75 stuck); the engine gridlocks (~40 stuck) -- the open
//      C4-vii-c willPass gridlock. This test does NOT assert the stuck count (that is the unfinished
//      work); it only guards against a REGRESSION past the current baseline and against a crash.
// See C4-VII-REMAINING.md for the full bootstrap (willPass pre-pass + internal-junction RoW).
public class C4viiWillpassGridDiagTests
{
    private static readonly string Dir = System.IO.Path.Combine(RepoRoot(), "scenarios", "_diag", "c4vii-willpass-grid");

    [Fact]
    public void Grid_RunsWithoutThrowing_CrashRegression()
    {
        var engine = new Engine();
        engine.LoadScenario(
            System.IO.Path.Combine(Dir, "net.net.xml"),
            System.IO.Path.Combine(Dir, "rou.rou.xml"),
            System.IO.Path.Combine(Dir, "config.sumocfg"));

        // Must not throw. Before the internal-lane keep-right guard (main eac0a5b) this threw
        // "edge ':C2_16' is not part of the given route" on the first junction crossing.
        var traj = engine.Run(600);

        var last = new Dictionary<string, (double T, double Speed)>();
        var maxT = 0.0;
        foreach (var p in traj.AllPoints)
        {
            maxT = System.Math.Max(maxT, p.Time);
            last[p.VehicleId] = (p.Time, p.Speed);
        }

        var stuck = last.Count(kv => kv.Value.T >= maxT - 1 && kv.Value.Speed < 0.1);

        // SUMO: 0 stuck / 75. Engine baseline at time of writing: ~40 stuck (open C4-vii-c). We do NOT
        // assert 0 here -- only that the crash regression holds (we got this far) and the gridlock has
        // not gotten materially WORSE than the recorded baseline. Tighten toward 0 as C4-vii-c lands.
        Assert.True(stuck <= 45, $"C4-vii-c regression: {stuck} stuck (baseline ~40, SUMO 0). See C4-VII-REMAINING.md.");
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
