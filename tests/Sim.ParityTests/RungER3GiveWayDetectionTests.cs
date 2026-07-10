using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// Rung ER3 behavioral (property) tests: give-way DETECTION + intent. A slow regular car (car0) is
// caught up by a blue-light emergency vehicle (ev, hasBluelight) on a single lane. The detection
// (Engine.DetectGiveWaySide) reads only the frozen start-of-step snapshot and writes the reacting
// vehicle's own GiveWaySide (0 none / -1 right / +1 left), exported via VehicleExportSnapshot.
// These tests assert the intent forms when-and-only-when an EV is in range, and that the whole
// subsystem is inert when no bluelight vehicle is present. There is NO SUMO golden (SUMO's rescue
// lane is a device that pushes state onto neighbours; we invert it to a per-ego pull -- and it
// cannot form a rescue lane on a single lane at all).
public class RungER3GiveWayDetectionTests
{
    // Captures each vehicle's exported GiveWaySide per frame.
    private sealed class GiveWayRecorder : ISimExportObserver
    {
        public readonly List<(double Time, string Id, int Side)> Samples = new();
        public void OnVehicleExported(in VehicleExportSnapshot s) => Samples.Add((s.Time, s.VehicleId, s.GiveWaySide));
        public void OnFrameEnd(double time) { }
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

    private static GiveWayRecorder Run(string scenario, int steps)
    {
        var dir = Path.Combine(RepoRoot(), "scenarios", scenario);
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(dir, "net.net.xml"),
            Path.Combine(dir, "rou.rou.xml"),
            Path.Combine(dir, "config.sumocfg"));
        var recorder = new GiveWayRecorder();
        engine.AddExportObserver(recorder);
        engine.Run(steps);
        return recorder;
    }

    [Fact]
    public void RegularCar_FormsGiveWayIntent_WhenEmergencyVehicleApproachesFromBehind()
    {
        var rec = Run("53-giveway-single", 60);

        var car0 = rec.Samples.Where(s => s.Id == "car0").OrderBy(s => s.Time).ToList();
        Assert.NotEmpty(car0);

        // The EV starts 60 m behind (out of the 25 m siren range), so at the very first frame car0
        // has NOT yet reacted.
        Assert.Equal(0, car0[0].Side);

        // Once the EV closes to within 25 m behind, car0 forms a give-way intent. On this single
        // lane (neither leftmost-of-multi-lane) the intent is to clear toward the RIGHT edge (-1).
        Assert.Contains(car0, s => s.Side == -1);
        Assert.DoesNotContain(car0, s => s.Side == +1);
    }

    [Fact]
    public void EmergencyVehicle_NeverGivesWayToItself()
    {
        var rec = Run("53-giveway-single", 60);
        var evSamples = rec.Samples.Where(s => s.Id == "ev").ToList();
        // The EV itself must never form a give-way intent (an EV never clears for another EV).
        Assert.NotEmpty(evSamples);
        Assert.All(evSamples, s => Assert.Equal(0, s.Side));
    }

    [Fact]
    public void NoEmergencyVehicle_DetectionIsInert()
    {
        // Scenario 14 has no bluelight vehicle -> _anyBluelight is false -> GiveWaySide is always 0.
        var rec = Run("14-external-obstacle", 60);
        Assert.NotEmpty(rec.Samples);
        Assert.All(rec.Samples, s => Assert.Equal(0, s.Side));
    }
}
