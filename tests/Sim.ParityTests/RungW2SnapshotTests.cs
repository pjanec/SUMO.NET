using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// Rung W2 test: SaveSnapshot/LoadSnapshot roundtrip equivalence on a MIXED road + rail fixture
// (scenarios/51-rail-crossing: a rail train crosses a rail_crossing junction while a road car waits
// for it, so the fixture exercises BOTH a train and the engine-level rail-crossing phase state
// machine). WarmUp(W) then save -> fresh load -> LoadSnapshot -> Run(N) must reproduce EXACTLY the
// tail of WarmUp(W); Run(N) done in one process. If the crossing state machine (or any per-vehicle
// field, including the train's) were not captured, the restored run would diverge -- so the exact
// match is the non-vacuous proof that "all vehicles including trains" + the engine state machines
// are captured. Offline, no SUMO golden.
public class RungW2SnapshotTests
{
    // Warm boundary chosen DURING the crossing's opening sequence: by t=40 the train has cleared
    // the crossing (so the crossing state is no longer derivable from any train position) and the
    // junction is counting down its yellow/opening timer toward green while car0 waits. That makes
    // the engine-level crossing state machine (_railCrossingStep/NextSwitch) genuinely load-bearing
    // across the snapshot -- if it were not restored, the opening timer would restart from the load
    // moment and car0 would release at a different time.
    private const int Warm = 40;
    private const int Cont = 20;

    private static Engine Load()
    {
        var dir = Path.Combine(RepoRoot(), "scenarios", "51-rail-crossing");
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(dir, "net.net.xml"),
            Path.Combine(dir, "rou.rou.xml"),
            Path.Combine(dir, "config.sumocfg"));
        return engine;
    }

    [Fact]
    public void SaveLoad_Roundtrip_ReproducesTheInMemoryWarmStart()
    {
        // Reference: warm up in memory, then continue (all in one engine).
        var refEngine = Load();
        refEngine.WarmUp(Warm);
        var refTail = refEngine.Run(Cont);

        // Snapshot: warm up, save; then a FRESH engine loads the snapshot and continues.
        var snapPath = Path.Combine(Path.GetTempPath(), $"w2-snapshot-{Guid.NewGuid():N}.xml");
        try
        {
            var saver = Load();
            saver.WarmUp(Warm);
            saver.SaveSnapshot(snapPath);

            var restored = Load();
            restored.LoadSnapshot(snapPath);
            var restoredTail = restored.Run(Cont);

            // Same vehicles (train + car), same emitted times, identical state at every one.
            Assert.Equal(
                refTail.VehicleIds.OrderBy(x => x, StringComparer.Ordinal),
                restoredTail.VehicleIds.OrderBy(x => x, StringComparer.Ordinal));
            Assert.Contains("train0", restoredTail.VehicleIds);
            Assert.Contains("car0", restoredTail.VehicleIds);

            var compared = 0;
            foreach (var rp in refTail.AllPoints)
            {
                Assert.True(restoredTail.TryGet(rp.VehicleId, rp.Time, out var sp),
                    $"restored run missing {rp.VehicleId} at t={rp.Time}");
                Assert.Equal(rp.Lane, sp.Lane);
                Assert.Equal(rp.Pos, sp.Pos, precision: 9);
                Assert.Equal(rp.Speed, sp.Speed, precision: 9);
                compared++;
            }

            Assert.True(compared > 0);

            // Non-vacuity that the CROSSING STATE mattered: at the warm boundary the train is on the
            // crossing and car0 is held short of it (near-stopped), and in the continued run car0
            // must later proceed onto CN. If the crossing state had reset to green on load, car0's
            // trajectory would differ from the reference (which the exact match above already rules
            // out); this asserts the scenario genuinely exercised the closed-then-open sequence.
            var car0 = restoredTail.PointsFor("car0").OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            Assert.Contains(car0, p => p.Speed < 0.5);              // was held at the closed crossing
            Assert.Contains(car0, p => p.Lane == "CN_0");           // proceeded after it opened
        }
        finally
        {
            if (File.Exists(snapPath)) File.Delete(snapPath);
        }
    }

    [Fact]
    public void SaveSnapshot_Throws_OnActuatedTls_ScopedOutState()
    {
        // Actuated-TLS phase/detector state is not yet captured; SaveSnapshot must fail LOUDLY on a
        // net with an actuated program rather than silently produce a wrong warm start.
        var dir = Path.Combine(RepoRoot(), "scenarios", "35-actuated-tls");
        var e = new Engine();
        e.LoadScenario(
            Path.Combine(dir, "net.net.xml"),
            Path.Combine(dir, "rou.rou.xml"),
            Path.Combine(dir, "config.sumocfg"));
        e.WarmUp(10);

        var snapPath = Path.Combine(Path.GetTempPath(), $"w2-actuated-{Guid.NewGuid():N}.xml");
        try
        {
            var ex = Assert.Throws<NotSupportedException>(() => e.SaveSnapshot(snapPath));
            Assert.Contains("actuated", ex.Message);
        }
        finally
        {
            if (File.Exists(snapPath)) File.Delete(snapPath);
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
