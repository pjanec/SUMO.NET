using System;
using System.Collections.Generic;
using Sim.Core.Mixed;
using Sim.Core.Orca;
using Xunit;
using Xunit.Abstractions;

namespace Sim.ParityTests;

// P0-2 (docs/PEDESTRIAN-TASKS.md P0-2; docs/PEDESTRIAN-DESIGN.md §3(d)): MixedTrafficCrowd's new
// stable-handle Add/Remove, mirroring P0-1's OrcaCrowd exactly (see OrcaCrowdAddRemoveTests, the
// template this file deliberately follows) -- its OWN free-list/generation bookkeeping and its OWN
// MixedTrafficHandle id-space (never interchangeable with an OrcaHandle).
//
// MixedTrafficCrowd has NO parallel step (no UseParallelStep/ParallelPlan/Parallel.For anywhere in the
// class -- confirmed by inspection), so there is no serial-vs-parallel bit-identity gate to extend here
// (OrcaCrowdAddRemoveTests' third test has no counterpart); only the two guarantees P0-1 established for
// the disc crowd, carried over to the shaped/oriented one:
//   1. Remove is O(1) and touches ONLY the removed slot -- every OTHER vehicle's handle/position/heading
//      is completely undisturbed (no shifting).
//   2. A later Add recycles a vacated slot (LIFO) rather than appending, and the recycled handle's
//      Generation is strictly greater than any handle a caller held to that slot before -- a stale
//      handle is provably rejected, never silently misresolved to the new occupant.
//   3. A scripted add/remove/step run is bit-identical run-to-run (no System.Random / thread-order
//      dependence).
public class MixedTrafficCrowdAddRemoveTests
{
    private const double Dt = 0.2;

    private readonly ITestOutputHelper _out;

    public MixedTrafficCrowdAddRemoveTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Remove_RecyclesSlot_BumpsGeneration_AndStaleHandleIsRejected()
    {
        var crowd = new MixedTrafficCrowd(4);

        // Five cars in parallel lanes (x = i*6.0), all cruising due north (+y) at the same speed and
        // heading -- already mutually collision-free (same direction, same speed, ample lateral
        // clearance for a 1.8 m-wide car at 6 m spacing), so the shaped-VO solve imposes NO lateral
        // correction (the relative velocity between any pair is exactly zero and their fixed lateral
        // offset never enters the collision set, so the pair's ORCA line always admits the unperturbed
        // preferred velocity) -- each car's x must stay EXACTLY put step after step. Any later
        // divergence in x is an unambiguous sign that removing/recycling a NEIGHBOUR'S slot leaked into
        // this car's own state.
        var handles = new MixedTrafficHandle[5];
        for (var i = 0; i < 5; i++)
        {
            handles[i] = crowd.Add(
                VehicleClass.Car, new Vec2(i * 6.0, 0.0), new Vec2(i * 6.0, 400.0), headingRad: Math.PI / 2);
        }

        // Fresh handles occupy slots 0..4 in Add order, generation 1 (MixedTrafficCrowd starts every
        // never-used slot's generation at 1 -- 0 is reserved for MixedTrafficHandle.Invalid).
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(i, handles[i].Index);
            Assert.Equal(1u, handles[i].Generation);
        }

        Assert.Equal(5, crowd.Count);

        // Remove an arbitrary, non-contiguous subset: slots 1 and 3.
        crowd.Remove(handles[1]);
        crowd.Remove(handles[3]);

        Assert.False(crowd.IsAlive(handles[1]));
        Assert.False(crowd.IsAlive(handles[3]));
        Assert.True(crowd.IsAlive(handles[0]));
        Assert.True(crowd.IsAlive(handles[2]));
        Assert.True(crowd.IsAlive(handles[4]));

        // A stale handle is rejected by every read/write accessor (throws) rather than silently
        // resolving to garbage.
        Assert.Throws<InvalidOperationException>(() => crowd.Position(handles[1]));
        Assert.Throws<InvalidOperationException>(() => crowd.Velocity(handles[3]));
        Assert.Throws<InvalidOperationException>(() => crowd.Heading(handles[1]));
        Assert.Throws<InvalidOperationException>(() => crowd.Class(handles[3]));
        Assert.Throws<InvalidOperationException>(() => crowd.IsActive(handles[1]));
        Assert.Throws<InvalidOperationException>(() => crowd.SetGoal(handles[3], Vec2.Zero));
        Assert.Throws<InvalidOperationException>(() => crowd.Deactivate(handles[1]));

        // Removing an already-removed (stale) handle again is an inert no-op (documented contract),
        // never a throw/crash.
        crowd.Remove(handles[1]);
        crowd.Remove(MixedTrafficHandle.Invalid);

        // Two more Adds must RECYCLE the two vacated slots (LIFO: most-recently-freed popped first --
        // slot 3 was freed after slot 1, so it comes back first), not grow past the high-water mark of 5.
        var recycledA = crowd.Add(
            VehicleClass.Car, new Vec2(500.0, 0.0), new Vec2(500.0, 400.0), headingRad: Math.PI / 2);
        var recycledB = crowd.Add(
            VehicleClass.Car, new Vec2(600.0, 0.0), new Vec2(600.0, 400.0), headingRad: Math.PI / 2);

        Assert.Equal(3, recycledA.Index);
        Assert.Equal(1, recycledB.Index);
        Assert.Equal(5, crowd.Count); // recycled, not appended -- high-water mark unchanged

        // Each recycled handle's generation is strictly greater than the ORIGINAL occupant's -- the
        // original caller-held handle (handles[1]/handles[3]) can never be confused with the new one.
        Assert.True(recycledA.Generation > handles[3].Generation,
            $"recycled slot 3's generation ({recycledA.Generation}) must exceed the original occupant's ({handles[3].Generation})");
        Assert.True(recycledB.Generation > handles[1].Generation,
            $"recycled slot 1's generation ({recycledB.Generation}) must exceed the original occupant's ({handles[1].Generation})");

        // The now-stale ORIGINAL handles to those same slot indices still must not resolve (even though
        // the slot index is alive again under a DIFFERENT generation).
        Assert.False(crowd.IsAlive(handles[1]));
        Assert.False(crowd.IsAlive(handles[3]));
        Assert.True(crowd.IsAlive(recycledA));
        Assert.True(crowd.IsAlive(recycledB));

        // Step the crowd and confirm every SURVIVING original car (0, 2, 4) is exactly where its own
        // straight-line cruise predicts -- unperturbed by its neighbours having been removed/recycled.
        for (var s = 0; s < 20; s++)
        {
            crowd.Step(Dt);
        }

        foreach (var i in new[] { 0, 2, 4 })
        {
            var pos = crowd.Position(handles[i]);
            Assert.Equal(i * 6.0, pos.X, precision: 9);
            Assert.True(pos.Y > 0.0, $"car {i} should have made forward progress toward its goal (y={pos.Y:F3})");
        }

        // The two recycled cars are also progressing normally (the recycled slot is a fully-functional
        // live vehicle, not a half-initialized leftover of its predecessor).
        Assert.True(crowd.Position(recycledA).Y > 0.0);
        Assert.True(crowd.Position(recycledB).Y > 0.0);
    }

    // Determinism gate (independent of "does removal disturb survivors" above): a fixed script of
    // Add/Remove calls interleaved with Step produces BIT-IDENTICAL trajectories across independent
    // runs -- no System.Random / thread-order dependence anywhere in the new removal path.
    [Fact]
    public void ScriptedAddRemoveRun_IsBitIdentical_RunToRun()
    {
        List<(int AgentId, Vec2 Pos, double Heading)[]> RunOnce()
        {
            var crowd = new MixedTrafficCrowd(8) { SymmetryBreak = 0.05 };
            var handles = new List<MixedTrafficHandle>();
            for (var i = 0; i < 6; i++)
            {
                handles.Add(crowd.Add(
                    VehicleClass.Car, new Vec2(i * 5.0, 0.0), new Vec2(i * 5.0, 120.0), headingRad: Math.PI / 2));
            }

            var trace = new List<(int, Vec2, double)[]>();
            for (var step = 0; step < 60; step++)
            {
                // Scripted membership churn at fixed steps -- deterministic, no RNG.
                if (step == 10)
                {
                    crowd.Remove(handles[1]);
                    crowd.Remove(handles[4]);
                }

                if (step == 20)
                {
                    handles.Add(crowd.Add(
                        VehicleClass.Car, new Vec2(200.0, 0.0), new Vec2(200.0, 120.0), headingRad: Math.PI / 2));
                }

                if (step == 30)
                {
                    crowd.Remove(handles[0]);
                }

                if (step == 35)
                {
                    handles.Add(crowd.Add(
                        VehicleClass.Car, new Vec2(210.0, 5.0), new Vec2(210.0, 125.0), headingRad: Math.PI / 2));
                }

                crowd.Step(Dt);

                var frame = new List<(int, Vec2, double)>();
                for (var i = 0; i < handles.Count; i++)
                {
                    if (crowd.IsAlive(handles[i]))
                    {
                        frame.Add((i, crowd.Position(handles[i]), crowd.Heading(handles[i])));
                    }
                }

                trace.Add(frame.ToArray());
            }

            return trace;
        }

        var run1 = RunOnce();
        var run2 = RunOnce();

        Assert.Equal(run1.Count, run2.Count);
        for (var step = 0; step < run1.Count; step++)
        {
            Assert.Equal(run1[step].Length, run2[step].Length);
            for (var k = 0; k < run1[step].Length; k++)
            {
                Assert.Equal(run1[step][k].AgentId, run2[step][k].AgentId);
                Assert.Equal(run1[step][k].Pos.X, run2[step][k].Pos.X, precision: 15);
                Assert.Equal(run1[step][k].Pos.Y, run2[step][k].Pos.Y, precision: 15);
                Assert.Equal(run1[step][k].Heading, run2[step][k].Heading, precision: 15);
            }
        }

        _out.WriteLine($"scripted add/remove run: {run1.Count} steps, bit-identical across independent runs.");
    }
}
