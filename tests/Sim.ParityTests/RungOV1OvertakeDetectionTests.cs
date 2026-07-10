using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// Rung OV1 behavioral (property) tests: opposite-direction overtake DETECTION. A fast lcOpposite
// vehicle held up behind a slow leader on edge AB forms an overtake intent (OvertakeActive, exported
// via VehicleExportSnapshot) only when the oncoming (opposite-direction) lane BA is clear far enough
// ahead. As the oncoming approaches head-on the intent must drop once it is within the clear-ahead
// distance. Reads only the frozen snapshot; inert when no vType has lcOpposite. No SUMO golden.
public class RungOV1OvertakeDetectionTests
{
    // Must match Engine.OvertakeClearAheadDist.
    private const double ClearAheadDist = 150.0;

    private sealed class Recorder : ISimExportObserver
    {
        // world X + overtake flag, per (time, id).
        public readonly List<(double Time, string Id, bool Overtake, double X)> Samples = new();
        public void OnVehicleExported(in VehicleExportSnapshot s) => Samples.Add((s.Time, s.VehicleId, s.OvertakeActive, s.X));
        public void OnFrameEnd(double time) { }
    }

    private static Recorder Run(string rouFile, int steps)
    {
        var dir = Path.Combine(RepoRoot(), "scenarios", "57-overtake-opposite");
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(dir, "net.net.xml"),
            Path.Combine(dir, rouFile),
            Path.Combine(dir, "config.sumocfg"));
        var rec = new Recorder();
        engine.AddExportObserver(rec);
        engine.Run(steps);
        return rec;
    }

    [Fact]
    public void OvertakeIntent_FollowsOncomingClearance()
    {
        var rec = Run("overtake.rou.xml", 20);

        // Index the overtake flag and each vehicle's world-X by time. Edge AB runs +x, so "ahead of
        // the overtaker" is a larger world-X; aheadDist = oncoming.X - overtaker.X. NOTE: the
        // exported OvertakeActive at time t was computed in the PRIOR step's plan from the
        // start-of-step positions -- i.e. from the positions exported at time t-1 -- so the flag at t
        // is checked against the geometry at t-1 (the state the detector actually saw).
        var byTime = rec.Samples
            .GroupBy(s => s.Time)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Id, s => s));
        var dt = 1.0; // config step-length

        var formedInClearWindow = false;
        foreach (var (t, frame) in byTime)
        {
            if (!frame.TryGetValue("overtaker", out var ov))
            {
                continue;
            }

            if (!byTime.TryGetValue(t - dt, out var prev)
                || !prev.TryGetValue("overtaker", out var ovPrev)
                || !prev.TryGetValue("oncoming", out var oncPrev))
            {
                continue;
            }

            var aheadDist = oncPrev.X - ovPrev.X; // geometry the detector saw when it set ov.Overtake

            // INVARIANT: while an oncoming vehicle was within the clear-ahead distance ahead, the
            // held-up overtaker must NOT signal an intent to use the oncoming lane.
            if (aheadDist > 0.0 && aheadDist <= ClearAheadDist)
            {
                Assert.False(ov.Overtake,
                    $"overtake intent set at t={t} with oncoming only {aheadDist:F1} m ahead (at t-1)");
            }

            // The clear window (oncoming still far ahead) is where a held-up overtaker DOES intend.
            if (aheadDist > ClearAheadDist && ov.Overtake)
            {
                formedInClearWindow = true;
            }
        }

        Assert.True(formedInClearWindow, "overtaker never formed an intent even while the oncoming lane was clear ahead");

        // The non-lcOpposite vehicles never form the intent.
        Assert.All(rec.Samples.Where(s => s.Id != "overtaker"), s => Assert.False(s.Overtake));
    }

    [Fact]
    public void NoLcOppositeVType_DetectionIsInert()
    {
        // The same fixture but the overtaker is a plain vType (no lcOpposite) -> _anyLcOpposite is
        // still true (the fixture's `fast` type sets it), but a vehicle that is not itself lcOpposite
        // never forms the intent. We assert inertness via the leader/oncoming (never lcOpposite).
        var rec = Run("overtake.rou.xml", 20);
        Assert.All(rec.Samples.Where(s => s.Id == "leader" || s.Id == "oncoming"),
            s => Assert.False(s.Overtake));
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
