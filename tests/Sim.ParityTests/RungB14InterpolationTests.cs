using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// SUMOSHARP-API.md §7 (interpolation hook): the SimulationRunner publishes the last TWO frames
// (Snapshot + PreviousSnapshot) with their sim-time stamps, so a host rendering faster than the sim
// ticks can blend between them for smooth motion. Additive / async-only -- no bearing on the parity
// path (the determinism gate is unaffected). Driven deterministically via manual Tick().
public class RungB14InterpolationTests
{
    private static string Net14 => Path.Combine(RepoRoot(), "scenarios", "14-external-obstacle", "net.net.xml");

    private static SimulationRunner StartedWithOneVehicle(out VehicleHandle handle)
    {
        var eng = new Engine();
        eng.LoadNetwork(Net14);
        var runner = new SimulationRunner(eng);
        handle = runner.Invoke(e => e.SpawnVehicle(e.DefaultVType, new[] { "e0" }));
        return runner;
    }

    // PreviousSnapshot is Empty until two frames exist, then lags Snapshot by exactly one step.
    [Fact]
    public void PreviousSnapshot_LagsCurrentByOneFrame()
    {
        var runner = StartedWithOneVehicle(out _);

        Assert.Same(SimulationSnapshot.Empty, runner.PreviousSnapshot); // no frame yet

        runner.Tick();
        Assert.Same(SimulationSnapshot.Empty, runner.PreviousSnapshot); // only one frame so far
        var firstStep = runner.Snapshot.StepCount;

        runner.Tick();
        Assert.Equal(firstStep, runner.PreviousSnapshot.StepCount);      // previous == the earlier frame
        Assert.Equal(firstStep + 1, runner.Snapshot.StepCount);
        Assert.True(runner.Snapshot.Time > runner.PreviousSnapshot.Time);
    }

    // Alpha maps renderTime linearly onto [0,1] across the two frames' stamps, clamped at both ends, and
    // is a safe 1.0 before two distinct frames exist.
    [Fact]
    public void InterpolationAlpha_MapsAndClamps()
    {
        var runner = StartedWithOneVehicle(out _);

        runner.Tick();
        Assert.Equal(1.0, runner.InterpolationAlpha(runner.Snapshot.Time)); // only one frame -> latest

        runner.Tick();
        var t0 = runner.PreviousSnapshot.Time;
        var t1 = runner.Snapshot.Time;

        Assert.Equal(0.0, runner.InterpolationAlpha(t0), 6);
        Assert.Equal(1.0, runner.InterpolationAlpha(t1), 6);
        Assert.Equal(0.5, runner.InterpolationAlpha((t0 + t1) / 2.0), 6);
        Assert.Equal(0.0, runner.InterpolationAlpha(t0 - 100.0));  // clamp low
        Assert.Equal(1.0, runner.InterpolationAlpha(t1 + 100.0));  // clamp high
    }

    // A vehicle present in both frames interpolates monotonically between them: alpha=0 -> the previous
    // frame's position, alpha=1 -> the current frame's, and a mid render-time lies strictly between.
    [Fact]
    public void TryInterpolateVehicle_BlendsBetweenFrames()
    {
        var runner = StartedWithOneVehicle(out var h);

        // Advance until the vehicle is active in two consecutive frames.
        for (var k = 0; k < 5; k++) runner.Tick();
        Assert.True(runner.PreviousSnapshot.TryGetVehicle(h, out var before));
        Assert.True(runner.Snapshot.TryGetVehicle(h, out var now));
        Assert.True(now.X != before.X || now.Y != before.Y, "vehicle should have moved between frames");

        var t0 = runner.PreviousSnapshot.Time;
        var t1 = runner.Snapshot.Time;

        Assert.True(runner.TryInterpolateVehicle(h, t0, out var at0));
        Assert.Equal((double)before.X, at0.PosX, 4);
        Assert.Equal((double)before.Y, at0.PosY, 4);

        Assert.True(runner.TryInterpolateVehicle(h, t1, out var at1));
        Assert.Equal((double)now.X, at1.PosX, 4);
        Assert.Equal((double)now.Y, at1.PosY, 4);

        Assert.True(runner.TryInterpolateVehicle(h, (t0 + t1) / 2.0, out var mid));
        // Strictly between the endpoints on whichever axis moved.
        Assert.InRange(mid.PosX, Math.Min(before.X, now.X), Math.Max(before.X, now.X));
        Assert.InRange(mid.PosY, Math.Min(before.Y, now.Y), Math.Max(before.Y, now.Y));
        Assert.InRange(mid.Angle, 0f, 360f);
    }

    // An unknown / departed handle (not in the latest frame) yields false.
    [Fact]
    public void TryInterpolateVehicle_UnknownHandle_False()
    {
        var runner = StartedWithOneVehicle(out _);
        runner.Tick();
        runner.Tick();

        var bogus = new VehicleHandle(9999u, 1);
        Assert.False(runner.TryInterpolateVehicle(bogus, runner.Snapshot.Time, out _));
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
