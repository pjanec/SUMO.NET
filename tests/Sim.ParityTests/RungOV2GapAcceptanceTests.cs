using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// Rung OV2 behavioral (property) test: the overtake decision uses a closing-speed / time-to-complete
// gap acceptance, not a fixed clear-ahead distance. Two fixtures share an IDENTICAL held-up
// overtaker and an IDENTICAL oncoming START position (BA pos 800 -> worldX 200, 140 m ahead of the
// overtaker at t=0); only the oncoming's speed differs. A fixed-distance rule (OV1) would give the
// same answer for both. OV2 accepts the overtake against the SLOW oncoming and refuses it against
// the FAST one, because the fast oncoming closes the head-on gap before the pass could complete.
// The decision is read from the flag exported at t=1, which the detector computed from the identical
// t=0 geometry.
public class RungOV2GapAcceptanceTests
{
    private sealed class Recorder : ISimExportObserver
    {
        public readonly List<(double Time, string Id, bool Overtake, double X)> Samples = new();
        public void OnVehicleExported(in VehicleExportSnapshot s) => Samples.Add((s.Time, s.VehicleId, s.OvertakeActive, s.X));
        public void OnFrameEnd(double time) { }
    }

    private static Recorder Run(string rouFile)
    {
        var dir = Path.Combine(RepoRoot(), "scenarios", "57-overtake-opposite");
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(dir, "net.net.xml"),
            Path.Combine(dir, rouFile),
            Path.Combine(dir, "config.sumocfg"));
        var rec = new Recorder();
        engine.AddExportObserver(rec);
        engine.Run(6);
        return rec;
    }

    private static (bool Overtake, double OncomingX, double OvertakerX) At(Recorder rec, double time)
    {
        var ov = rec.Samples.Single(s => s.Time == time && s.Id == "overtaker");
        var onc = rec.Samples.Single(s => s.Time == time && s.Id == "oncoming");
        return (ov.Overtake, onc.X, ov.X);
    }

    [Fact]
    public void SameDistance_DifferentOncomingSpeed_FlipsTheDecision()
    {
        var slow = Run("ov2-slow.rou.xml");
        var fast = Run("ov2-fast.rou.xml");

        // The two runs start from IDENTICAL geometry (only the oncoming speed differs), so a
        // position-only rule could not tell them apart.
        var s0 = At(slow, 0);
        var f0 = At(fast, 0);
        Assert.Equal(s0.OncomingX, f0.OncomingX, precision: 6);
        Assert.Equal(s0.OvertakerX, f0.OvertakerX, precision: 6);

        // The flag at t=1 was computed from that identical t=0 geometry. Speed alone flips it:
        // accept against the slow oncoming, refuse against the fast one.
        Assert.True(At(slow, 1).Overtake, "overtake should be ACCEPTED against the slow oncoming");
        Assert.False(At(fast, 1).Overtake, "overtake should be REFUSED against the fast oncoming (it closes the gap too soon)");
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
