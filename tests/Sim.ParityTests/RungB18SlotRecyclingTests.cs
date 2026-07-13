using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// SUMOSHARP-API.md §9 (vehicle-slot recycling): Despawn frees a vehicle's EntityIndex slot; the next
// runtime SpawnVehicle reuses it (rebuilding the slot in place, resetting its idx-keyed side state, with a
// bumped generation) instead of growing _vehicles forever. Additive / runtime-demand only; inert for the
// golden path (which never despawns), so the parity gate + determinism hash are unchanged.
public class RungB18SlotRecyclingTests
{
    private static string Net14 => Path.Combine(RepoRoot(), "scenarios", "14-external-obstacle", "net.net.xml");
    private static string DiamondNet =>
        Path.Combine(RepoRoot(), "scenarios", "_fixtures", "routing-diamond", "net.net.xml");

    private static Engine Loaded(string net)
    {
        var e = new Engine();
        e.LoadNetwork(net);
        return e;
    }

    // Despawn frees the slot; the next spawn reuses that exact EntityIndex with a DIFFERENT generation, and
    // the despawned handle stays stale.
    [Fact]
    public void Despawn_ThenSpawn_ReusesSlot_WithBumpedGeneration()
    {
        var e = Loaded(Net14);
        var h1 = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });
        e.Step();
        Assert.Equal(VehicleLifecycle.Active, e.GetLifecycle(h1));

        Assert.True(e.Despawn(h1));
        var h2 = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });

        Assert.Equal(h1.Index, h2.Index);              // same slot reused
        Assert.NotEqual(h1.Generation, h2.Generation); // distinct generation
        Assert.Equal(VehicleLifecycle.Unknown, e.GetLifecycle(h1)); // old handle stale
        Assert.NotEqual(VehicleLifecycle.Unknown, e.GetLifecycle(h2));
    }

    // With recycling OFF, slots grow monotonically (the pre-recycling behaviour).
    [Fact]
    public void RecyclingOff_SlotsGrowMonotonically()
    {
        var e = Loaded(Net14);
        e.RecycleVehicleSlots = false;

        var h1 = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });
        e.Step();
        Assert.True(e.Despawn(h1));
        var h2 = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });

        Assert.Equal(h1.Index + 1u, h2.Index); // grew, did not reuse
    }

    // A recycled slot emits a fresh Departed event (its lifecycle-diff baseline was reset), i.e. the new
    // occupant is a proper new vehicle, not a ghost of the old one.
    [Fact]
    public void RecycledSlot_EmitsFreshDepartedEvent()
    {
        var e = Loaded(Net14);
        var h1 = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });
        e.Step(); // h1 departs
        e.Despawn(h1);
        e.Step(); // drains h1's Arrived

        var h2 = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });
        e.Step(); // h2 should depart on the recycled slot

        var sawDeparted = false;
        foreach (var ev in e.Events)
        {
            if (ev.Handle.Index == h2.Index && ev.Handle.Generation == h2.Generation
                && ev.Kind == SimEventKind.Departed)
            {
                sawDeparted = true;
            }
        }

        Assert.True(sawDeparted, "recycled slot should emit a fresh Departed for the new vehicle");
        Assert.Equal(VehicleLifecycle.Active, e.GetLifecycle(h2));
    }

    // A recycled slot follows its NEW route, carrying no stale routing state from the previous occupant.
    [Fact]
    public void RecycledSlot_FollowsNewRoute_NoStaleState()
    {
        var e = Loaded(DiamondNet);
        var h1 = e.SpawnVehicle(e.DefaultVType, "SA", "CD");
        e.Step();
        e.Despawn(h1);

        var h2 = e.SpawnVehicle(e.DefaultVType, "SA", "DE"); // different destination, reuses the slot
        Assert.Equal(h1.Index, h2.Index);

        // Advance and confirm h2 reaches DE's edge on its OWN route (stale CD state would misroute it).
        var reachedDE = false;
        for (var k = 0; k < 200 && !reachedDE; k++)
        {
            e.Step();
            if (e.TryGetVehicle(h2, out var s) && s.LaneId.StartsWith("DE", StringComparison.Ordinal))
            {
                reachedDE = true;
            }
        }

        Assert.True(reachedDE, "recycled vehicle should traverse its own SA->DE route");
    }

    // Determinism: an identical spawn/despawn/spawn sequence produces identical handles and trajectory.
    [Fact]
    public void RecyclingIsDeterministic()
    {
        static (uint idx, ushort gen, double pos, string lane) Run()
        {
            var e = Loaded(Net14);
            var a = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });
            e.Step();
            e.Despawn(a);
            var b = e.SpawnVehicle(e.DefaultVType, new[] { "e0" });
            for (var k = 0; k < 10; k++) e.Step();
            e.TryGetVehicle(b, out var s);
            return (b.Index, b.Generation, s.Pos, s.LaneId);
        }

        Assert.Equal(Run(), Run());
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
