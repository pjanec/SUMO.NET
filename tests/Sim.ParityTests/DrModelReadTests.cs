using System.IO;
using Sim.Core;
using Sim.Core.Orca;
using Xunit;
using Xunit.Abstractions;

namespace Sim.ParityTests;

// DR2 (dead-reckoning coordination, issue #3): the per-vehicle DR-regime read the DR publisher polls to
// classify a lane vehicle LaneArc vs FreeKinematic-while-swerving. Exposed two ways off the Step() read
// surface: the batched `Engine.DrModels` column (aligned with `VehicleHandles`) and the per-handle
// `Engine.GetDrModel(VehicleHandle)`. Additive/gated: a plain lane vehicle is always LaneArc/Stationary
// (LateralManoeuvre is only ever set under LanelessRvo && _sublane), so the parity path and determinism
// hash are unaffected -- the full suite + hash gate confirm it.
public class DrModelReadTests
{
    private static readonly string LanelessDir =
        Path.Combine(RepoRoot(), "scenarios", "_fixtures", "bridge-crossing");     // lateral-resolution=0.8
    private static readonly string PlainDir =
        Path.Combine(RepoRoot(), "scenarios", "60-sublane-drift");                 // a committed scenario

    private readonly ITestOutputHelper _out;

    public DrModelReadTests(ITestOutputHelper output) => _out = output;

    // A laneless-RVO vehicle that must swerve for a pedestrian standing in its lane reads FreeKinematic
    // WHILE swerving (actively coupled), and LaneArc / Stationary otherwise -- and the column agrees with
    // the per-handle accessor at every step.
    [Fact]
    public void LanelessVehicleSwervingForCrowd_ReadsFreeKinematic_ColumnMatchesAccessor()
    {
        var engine = new Engine { LanelessRvo = true };
        engine.LoadScenario(
            Path.Combine(LanelessDir, "net.net.xml"),
            Path.Combine(LanelessDir, "rou.rou.xml"),
            Path.Combine(LanelessDir, "config.sumocfg"));

        // A person standing in the lane ahead (goal == start -> holds position). Direction B only: the
        // vehicle sees the crowd via CrowdSource and swerves; the crowd need not move for this read test.
        var crowd = new OrcaCrowd();
        crowd.Add(new Vec2(30, -3.6), radius: 0.35, maxSpeed: 1.5, goal: new Vec2(30, -3.6));
        engine.CrowdSource = crowd;

        var sawFreeKinematic = false;
        var sawLaneArc = false;

        for (var step = 0; step < 25; step++)
        {
            engine.Step();
            var handles = engine.VehicleHandles;
            var col = engine.DrModels;
            Assert.Equal(handles.Length, col.Length);

            for (var i = 0; i < handles.Length; i++)
            {
                var viaAccessor = engine.GetDrModel(handles[i]);
                // The batched column MUST equal the per-handle accessor for the same vehicle.
                Assert.Equal((byte)viaAccessor, col[i]);

                if (viaAccessor == DrModel.FreeKinematic)
                {
                    sawFreeKinematic = true;
                }
                else if (viaAccessor == DrModel.LaneArc)
                {
                    sawLaneArc = true;
                }
            }
        }

        _out.WriteLine($"laneless swerve DR: sawFreeKinematic={sawFreeKinematic} sawLaneArc={sawLaneArc}");
        Assert.True(sawFreeKinematic, "vehicle never read FreeKinematic despite swerving for the crowd agent");
        Assert.True(sawLaneArc, "vehicle never read LaneArc (expected it before/after the manoeuvre)");
    }

    // A plain lane vehicle (no LanelessRvo, no crowd) is NEVER FreeKinematic -- always LaneArc while
    // moving, Stationary when stopped. This is the parity-path guarantee: DR classification is inert.
    [Fact]
    public void PlainLaneVehicle_IsNeverFreeKinematic()
    {
        var engine = new Engine();   // no LanelessRvo, no CrowdSource
        engine.LoadScenario(
            Path.Combine(PlainDir, "net.net.xml"),
            Path.Combine(PlainDir, "rou.rou.xml"),
            Path.Combine(PlainDir, "config.sumocfg"));

        var sawLaneArc = false;
        for (var step = 0; step < 20; step++)
        {
            engine.Step();
            var handles = engine.VehicleHandles;
            var col = engine.DrModels;
            for (var i = 0; i < handles.Length; i++)
            {
                var m = engine.GetDrModel(handles[i]);
                Assert.Equal((byte)m, col[i]);
                Assert.NotEqual(DrModel.FreeKinematic, m);   // never reactive for a plain vehicle
                if (m == DrModel.LaneArc)
                {
                    sawLaneArc = true;
                }
            }
        }

        Assert.True(sawLaneArc, "a moving plain vehicle should read LaneArc");
    }

    // A stale handle (never issued) resolves to Stationary -- nothing to extrapolate.
    [Fact]
    public void StaleHandle_ResolvesToStationary()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(PlainDir, "net.net.xml"),
            Path.Combine(PlainDir, "rou.rou.xml"),
            Path.Combine(PlainDir, "config.sumocfg"));
        engine.Step();

        Assert.Equal(DrModel.Stationary, engine.GetDrModel(new VehicleHandle(9999, 1)));
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
