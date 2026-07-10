using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// Rung OV4 behavioral (property) test: cooperative oncoming shift -- the mirror of the ER3/ER5
// give-way drift for opposite-direction overtaking. A held-up fast lcOpposite overtaker spills into
// the oncoming (bidi) lane to pass a slow leader (OV3); the ONCOMING vehicle, seeing that spilled
// overtaker closing head-on within its reaction range, pulls to its OWN outer lane edge to widen the
// corridor, then recentres once the overtaker has recentred. Asserted: the oncoming forms the
// cooperative-shift DECISION only while the overtaker is actually spilled across the centre line, it
// drifts OUTWARD (away from the centre line) while doing so, both vehicles recentre, and NO pair of
// vehicles ever physically overlaps. Collisions are checked in the exported world (X, Y). No SUMO
// golden (this is not a SUMO-parity behaviour -- SUMO's sublane opposite-overtake does not push the
// oncoming aside -- it is the requested live-reactivity enhancement, inert when no vType is
// lcOpposite).
public class RungOV4CooperativeShiftTests
{
    private const double LaneCentreYAB = -1.60; // edge AB (the overtaker's lane) centre
    private const double LaneCentreYBA = 1.60;  // edge BA (the oncoming's lane) centre
    private const double SpillThreshold = 0.8;  // overtaker is "spilled" once well off its lane centre
    private const double CarLen = 5.0;
    private const double CombinedHalfWidth = 1.8;

    private sealed class Recorder : ISimExportObserver
    {
        public readonly List<(double Time, string Id, double X, double Y, bool Overtake, bool Coop)> Samples = new();
        public void OnVehicleExported(in VehicleExportSnapshot s) =>
            Samples.Add((s.Time, s.VehicleId, s.X, s.Y, s.OvertakeActive, s.CooperativeShift));
        public void OnFrameEnd(double time) { }
    }

    private static Recorder Run(int steps)
    {
        var dir = Path.Combine(RepoRoot(), "scenarios", "57-overtake-opposite");
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(dir, "net.net.xml"),
            Path.Combine(dir, "ov4-cooperative.rou.xml"),
            Path.Combine(dir, "config.sumocfg"));
        var rec = new Recorder();
        engine.AddExportObserver(rec);
        engine.Run(steps);
        return rec;
    }

    [Fact]
    public void OncomingShiftsToOuterEdge_WhileOvertakerSpilled_ThenBothRecentre_NoCollision()
    {
        var rec = Run(30);

        var overtaker = rec.Samples.Where(s => s.Id == "overtaker").OrderBy(s => s.Time).ToList();
        var oncoming = rec.Samples.Where(s => s.Id == "oncoming").OrderBy(s => s.Time).ToList();
        Assert.NotEmpty(overtaker);
        Assert.NotEmpty(oncoming);

        // The overtaker genuinely spilled into the oncoming lane (OV3) at some point.
        Assert.Contains(overtaker, p => p.Y - LaneCentreYAB > SpillThreshold);

        // The oncoming formed the cooperative-shift decision, and did so ONLY while the overtaker was
        // actually spilled across the centre line -- the shift is caused by the encroachment, not
        // free-floating. The decision reads the overtaker's ALREADY-COMMITTED lateral position from
        // the frozen start-of-step snapshot, so the decision exported at time t reflects the
        // overtaker's spill at the PREVIOUS step (t-1) -- the one-step lag between a plan-phase flag
        // and the positions it was computed from.
        var overtakerByTime = overtaker.ToDictionary(p => p.Time, p => p.Y);
        var coopFrames = oncoming.Where(p => p.Coop).ToList();
        Assert.NotEmpty(coopFrames);
        foreach (var c in coopFrames)
        {
            var spilledNow = overtakerByTime.TryGetValue(c.Time, out var yNow) && yNow - LaneCentreYAB > SpillThreshold;
            var spilledPrev = overtakerByTime.TryGetValue(c.Time - 1.0, out var yPrev) && yPrev - LaneCentreYAB > SpillThreshold;
            Assert.True(spilledNow || spilledPrev,
                $"oncoming shifted at t={c.Time} but the overtaker was not spilled at t or t-1");
        }

        // While shifting, the oncoming drifted OUTWARD -- farther from the centre line (y=0) than its
        // lane centre, i.e. to a MORE positive world Y than +1.60.
        Assert.Contains(oncoming, p => p.Coop && p.Y > LaneCentreYBA + 0.5);

        // The overtake still completed: the overtaker ended ahead of the leader and both the overtaker
        // and the oncoming returned to their own lane centres.
        var leader = rec.Samples.Where(s => s.Id == "leader").OrderBy(s => s.Time).ToList();
        Assert.True(overtaker[^1].X > leader[^1].X, "overtaker never got ahead of the leader");
        Assert.True(Math.Abs(overtaker[^1].Y - LaneCentreYAB) < 1e-6, $"overtaker did not recentre (final y {overtaker[^1].Y:F2})");
        Assert.False(oncoming[^1].Coop, "oncoming still shifting at the end of its run");
        Assert.True(Math.Abs(oncoming[^1].Y - LaneCentreYBA) < 1e-6, $"oncoming did not recentre (final y {oncoming[^1].Y:F2})");

        // No physical overlap between ANY pair at any timestep.
        foreach (var frame in rec.Samples.GroupBy(s => s.Time))
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
                        $"collision at t={a.Time}: {a.Id}({a.X:F1},{a.Y:F1}) vs {b.Id}({b.X:F1},{b.Y:F1})");
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
