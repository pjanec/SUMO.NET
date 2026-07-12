using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// SUMOSHARP-API.md §10: the per-Step lifecycle event buffer (Engine.Events). Departed on insertion,
// Arrived on route completion / despawn. Populated only on the Step() path (Run() is unaffected).
public class RungB11LifecycleEventsTests
{
    private static string Net14 => Path.Combine(RepoRoot(), "scenarios", "14-external-obstacle", "net.net.xml");

    [Fact]
    public void Departed_EmittedOnceWhenVehicleInserts()
    {
        var e = new Engine();
        e.LoadNetwork(Net14);
        var h = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });

        e.Step(); // inserts on the empty lane
        Assert.True(HasEvent(e, h, SimEventKind.Departed));

        e.Step(); // no repeat
        Assert.False(HasEvent(e, h, SimEventKind.Departed));
    }

    [Fact]
    public void Arrived_EmittedWhenVehicleFinishesRoute()
    {
        var e = new Engine();
        e.LoadNetwork(Net14);
        var t = e.DefineVType(new VTypeParams { Sigma = 0.0 });
        var h = e.SpawnVehicle(t, new[] { "e0" }, departPos: 0.0, departSpeed: 0.0, departLane: 0);

        var arrived = false;
        for (var k = 0; k < 200 && !arrived; k++)
        {
            e.Step();
            arrived = HasEvent(e, h, SimEventKind.Arrived);
        }

        Assert.True(arrived, "expected an Arrived event when the vehicle ran off the end of e0");
        Assert.Equal(VehicleLifecycle.Arrived, e.GetLifecycle(h));
    }

    [Fact]
    public void Despawn_SurfacesAsArrivedEvent()
    {
        var e = new Engine();
        e.LoadNetwork(Net14);
        var h = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });
        e.Step();
        Assert.Equal(VehicleLifecycle.Active, e.GetLifecycle(h));

        e.Despawn(h);
        e.Step();
        // Despawn bumps the generation, so match on the vehicle index (the handle the host holds is stale).
        Assert.True(HasEventIndex(e, h.Index, SimEventKind.Arrived));
    }

    private static bool HasEvent(Engine e, VehicleHandle h, SimEventKind kind)
    {
        foreach (var ev in e.Events)
        {
            if (ev.Handle == h && ev.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEventIndex(Engine e, uint index, SimEventKind kind)
    {
        foreach (var ev in e.Events)
        {
            if (ev.Handle.Index == index && ev.Kind == kind)
            {
                return true;
            }
        }

        return false;
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
