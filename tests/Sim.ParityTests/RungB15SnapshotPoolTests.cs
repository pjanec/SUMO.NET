using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// SUMOSHARP-API.md §7 (snapshot pool): the opt-in SimulationRunner.EnableSnapshotPool() reuses backing
// arrays across Ticks so a live render loop stops allocating the columnar arrays every frame. It must be
// behavior-neutral (same published values as the default per-Tick-alloc path) and must actually recycle
// arrays. Additive / async-only -- no bearing on the parity path.
public class RungB15SnapshotPoolTests
{
    private static string Net14 => Path.Combine(RepoRoot(), "scenarios", "14-external-obstacle", "net.net.xml");

    // Pooled snapshots carry the SAME values as a bare engine stepped in lockstep -- pooling changes only
    // where the arrays live, not what they contain.
    [Fact]
    public void Pooled_MatchesBareEngine_ValuesIdentical()
    {
        var bare = new Engine();
        bare.LoadNetwork(Net14);
        var hb = bare.SpawnVehicle(bare.DefaultVType, new[] { "e0" });

        var eng = new Engine();
        eng.LoadNetwork(Net14);
        var runner = new SimulationRunner(eng);
        runner.EnableSnapshotPool(capacity: 3);
        Assert.True(runner.IsSnapshotPoolEnabled);
        var hr = runner.Invoke(e => e.SpawnVehicle(e.DefaultVType, new[] { "e0" }));

        for (var k = 0; k < 30; k++)
        {
            bare.Step();
            runner.Tick();

            var bareHas = bare.TryGetVehicle(hb, out var sb);
            var snap = runner.Snapshot;
            var snapHas = snap.TryGetVehicle(hr, out var ss);

            Assert.Equal(bareHas, snapHas);
            if (bareHas)
            {
                Assert.Equal(sb.Pos, ss.Pos);       // bit-exact
                Assert.Equal(sb.Speed, ss.Speed);
                Assert.Equal(sb.LaneId, ss.LaneId);
            }

            Assert.Equal(bare.StepCount, snap.StepCount);
        }
    }

    // With a stable vehicle count, the ring recycles the backing arrays: the array instance published now
    // is the SAME instance published `capacity` Ticks earlier (proving no per-frame re-allocation).
    [Fact]
    public void Pooled_RecyclesBackingArrays_AcrossCapacityTicks()
    {
        const int cap = 3;
        var eng = new Engine();
        eng.LoadNetwork(Net14);
        var runner = new SimulationRunner(eng);
        runner.EnableSnapshotPool(cap);
        runner.Invoke(e => e.SpawnVehicle(e.DefaultVType, new[] { "e0" }));

        // Get the single vehicle active, then verify the count is stable across the window we test.
        for (var k = 0; k < 4; k++) runner.Tick();
        var baseline = runner.Snapshot.Count;
        Assert.True(baseline > 0);

        var firstArray = runner.Snapshot.PosX;
        var stable = true;
        for (var k = 0; k < cap; k++)
        {
            runner.Tick();
            if (runner.Snapshot.Count != baseline) stable = false;
        }

        // Only assert recycling if the count held constant (otherwise a legitimate re-alloc occurred).
        if (stable)
        {
            Assert.Same(firstArray, runner.Snapshot.PosX);
        }
    }

    // The default (pool-off) runner allocates a fresh array every Tick -- the counter-case to the above,
    // confirming the pool is what changes the reference behavior.
    [Fact]
    public void Unpooled_AllocatesFreshArraysEachTick()
    {
        var eng = new Engine();
        eng.LoadNetwork(Net14);
        var runner = new SimulationRunner(eng);
        Assert.False(runner.IsSnapshotPoolEnabled);
        runner.Invoke(e => e.SpawnVehicle(e.DefaultVType, new[] { "e0" }));

        for (var k = 0; k < 3; k++) runner.Tick();
        var a = runner.Snapshot.PosX;
        runner.Tick();
        var b = runner.Snapshot.PosX;

        Assert.NotSame(a, b);
    }

    // The interpolation hook still works with the pool on: current + previous frames stay valid together.
    [Fact]
    public void Pooled_InterpolationHookStillWorks()
    {
        var eng = new Engine();
        eng.LoadNetwork(Net14);
        var runner = new SimulationRunner(eng);
        runner.EnableSnapshotPool(capacity: 3);
        var h = runner.Invoke(e => e.SpawnVehicle(e.DefaultVType, new[] { "e0" }));

        for (var k = 0; k < 5; k++) runner.Tick();
        Assert.True(runner.PreviousSnapshot.TryGetVehicle(h, out var before));
        Assert.True(runner.Snapshot.TryGetVehicle(h, out var now));

        var mid = (runner.PreviousSnapshot.Time + runner.Snapshot.Time) / 2.0;
        Assert.True(runner.TryInterpolateVehicle(h, mid, out var r));
        Assert.InRange(r.PosX, Math.Min(before.X, now.X), Math.Max(before.X, now.X));
        Assert.InRange(r.PosY, Math.Min(before.Y, now.Y), Math.Max(before.Y, now.Y));
    }

    [Fact]
    public void EnableSnapshotPool_RejectsBadCapacity_AndAfterStart()
    {
        var eng = new Engine();
        eng.LoadNetwork(Net14);
        var runner = new SimulationRunner(eng);

        Assert.Throws<ArgumentOutOfRangeException>(() => runner.EnableSnapshotPool(1));

        using (runner)
        {
            runner.Start(targetHz: 500.0);
            try
            {
                Assert.Throws<InvalidOperationException>(() => runner.EnableSnapshotPool(3));
            }
            finally
            {
                runner.Stop();
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
