using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// Rung D2 behavioral (property) test: opposite-direction overtake RETURN-GAP enforcement. This is the
// full-run continuation of the OV3b adversarial fixture (RungOV3bAbortSafetyTests only covered the
// abort window ~13 steps). Past that window the oncoming clears and the overtaker re-commits and
// overtakes the now-faster leader a second time; WITHOUT return-gap enforcement it recenters the
// instant it noses ahead of the leader (~3.6 m gap -> a body overlap), the pre-existing OV3 bug.
// With D2 the overtaker STAYS spilled until it is a safe following gap AHEAD of the just-passed
// leader, then recenters. Asserted: no vehicle pair ever physically overlaps across the FULL run,
// the overtaker really passed the leader and recentered, and at the moment it recenters (while ahead
// of the leader) the longitudinal gap is safe. Collisions checked in the exported world (X, Y).
public class RungD2ReturnGapTests
{
    private const double LaneCentreYAB = -1.60;
    private const double CarLen = 5.0;
    private const double CombinedHalfWidth = 1.8;
    private const double SafeReturnGap = 7.5; // > CarLen (5) + a following margin; the buggy return cut in at ~3.6 m
    private const int Steps = 45;

    [Fact]
    public void OvertakerRecentersOnlyWithASafeGapAheadOfTheLeader_NoCollisionFullRun()
    {
        var dir = Path.Combine(RepoRoot(), "scenarios", "57-overtake-opposite");
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(dir, "net.net.xml"),
            Path.Combine(dir, "ov3b-adversarial.rou.xml"),
            Path.Combine(dir, "config.sumocfg"));
        var traj = engine.Run(Steps);

        var ov = traj.PointsFor("overtaker").OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        var leader = traj.PointsFor("leader");

        // Non-vacuous: the overtaker spilled into the oncoming lane and ended ahead of the leader,
        // recentred on its own lane.
        Assert.Contains(ov, p => p.Y - LaneCentreYAB > 0.8);
        Assert.True(ov[^1].Pos > leader.OrderBy(kv => kv.Key).Last().Value.Pos, "overtaker never got ahead of the leader");
        Assert.True(Math.Abs(ov[^1].Y - LaneCentreYAB) < 1e-6, $"overtaker did not recentre (final y {ov[^1].Y:F2})");

        // Return-gap: on every step where the overtaker is centred (recentred) AND ahead of the
        // leader on the same edge, the longitudinal gap to the leader must be safe -- i.e. it never
        // cut back into the lane right in front of the leader. (The pre-D2 bug recentred at ~3.6 m.)
        foreach (var (time, o) in traj.PointsFor("overtaker").OrderBy(kv => kv.Key))
        {
            if (!leader.TryGetValue(time, out var ld)) continue;
            var centred = Math.Abs(o.Y - LaneCentreYAB) < 0.2;
            var ahead = o.Pos > ld.Pos;
            if (centred && ahead)
            {
                Assert.True(o.Pos - ld.Pos >= SafeReturnGap,
                    $"overtaker recentred only {o.Pos - ld.Pos:F1} m ahead of the leader at t={time} (need >= {SafeReturnGap})");
            }
        }

        // No physical overlap between ANY pair at any timestep across the whole run.
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
