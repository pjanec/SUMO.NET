using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// Rung OV3b behavioral (property) test: adversarial abort-mid-spill. The leader accelerates toward
// the overtaker's own speed (maxSpeed 11), so the overtaker's gap acceptance commits early (leader
// still slow -> large speed advantage) but the pass runs long as the leader speeds up while an
// oncoming closes head-on. The overtaker must ABORT the overtake WHILE ALREADY SPILLED into the
// oncoming lane and recenter without a collision. This is the adversarial safety case the OV3 review
// asked for: it verifies that OV2's gap acceptance (requiredClear grows as the leader's speed
// advantage shrinks) drops the intent MID-SPILL and the overtaker recenters collision-free, rather
// than only completing clean overtakes. Collisions are checked in the exported world (X, Y) across
// ALL vehicle pairs. No SUMO golden.
//
// DOCUMENTED FOLLOW-UPS (deliberately out of scope, see OV-REMAINING.md):
//  - An explicit cross-lane hard-brake backstop for the case OV2 might be optimistic was prototyped
//    and reverted: with OV2's (conservative) gap acceptance it never binds -- it dropped the intent
//    here while the oncoming was still ~238 m away -- so the backstop had no non-vacuous test and was
//    not added (defense-in-depth without a demonstrated need is speculative code).
//  - Run past ~t=13 the overtaker, once the oncoming has cleared, re-commits and overtakes the
//    now-fast leader a second time and its RETURN cuts in slightly too tight in front of that fast
//    leader -- a pre-existing OV3 return-gap issue (the return triggers on "passed the leader"
//    without enforcing a safe re-entry gap). This test covers only the abort-mid-spill window.
public class RungOV3bAbortSafetyTests
{
    private const double LaneCentreYAB = -1.60;
    private const double CarLen = 5.0;
    private const double CombinedHalfWidth = 1.8;
    private const int Steps = 13; // through the abort + recenter, before the follow-up return-gap window

    [Fact]
    public void OvertakerAbortsMidSpill_AndRecenters_WithoutCollision()
    {
        var dir = Path.Combine(RepoRoot(), "scenarios", "57-overtake-opposite");
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(dir, "net.net.xml"),
            Path.Combine(dir, "ov3b-adversarial.rou.xml"),
            Path.Combine(dir, "config.sumocfg"));
        var traj = engine.Run(Steps);

        var ov = traj.PointsFor("overtaker").OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();

        // It genuinely started the overtake (spilled well off its lane centre) ...
        var spilled = ov.Where(p => p.Y - LaneCentreYAB > 0.8).ToList();
        Assert.NotEmpty(spilled);
        // ... then ABORTED and returned to its lane centre before the run ends (mid-spill abort,
        // NOT a completed pass -- it is still behind the leader).
        var leaderEnd = traj.PointsFor("leader").OrderBy(kv => kv.Key).Last().Value;
        Assert.True(Math.Abs(ov[^1].Y - LaneCentreYAB) < 1e-6, $"overtaker did not recenter (final y {ov[^1].Y:F2})");
        Assert.True(ov[^1].Pos < leaderEnd.Pos, "overtaker should have aborted (still behind the leader), not completed the pass");

        // No physical overlap between ANY pair at any timestep through the abort window.
        foreach (var frame in traj.AllPoints.GroupBy(p => p.Time))
        {
            var pts = frame.ToList();
            for (var i = 0; i < pts.Count; i++)
            {
                for (var j = i + 1; j < pts.Count; j++)
                {
                    var a = pts[i];
                    var b = pts[j];
                    var overlap = Math.Abs(a.X - b.X) < CarLen && Math.Abs(a.Y - b.Y) < CombinedHalfWidth;
                    Assert.False(overlap,
                        $"collision at t={a.Time}: {a.VehicleId}({a.X:F1},{a.Y:F1}) vs {b.VehicleId}({b.X:F1},{b.Y:F1})");
                }
            }
        }
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
