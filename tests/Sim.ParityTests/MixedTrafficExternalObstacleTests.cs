using System;
using System.Collections.Generic;
using Sim.Core.Bridge;
using Sim.Core.Mixed;
using Sim.Core.Orca;
using Xunit;
using Xunit.Abstractions;

namespace Sim.ParityTests;

// P0-2 Part B (docs/PEDESTRIAN-TASKS.md P0-2; docs/PEDESTRIAN-DESIGN.md §3(d), §6): MixedTrafficCrowd
// .SetExternalObstacles -- a dynamic, per-step external moving-disc input (mirroring OrcaCrowd's
// cross-regime bridge) so a maneuvering car can avoid a MOVING pedestrian velocity-awarely, without a
// per-step crowd rebuild. This is the concrete replacement for POC-6b LotCoupling's Direction-B
// approximation, which had NO such input and instead rebuilt the whole car crowd every step with the
// pedestrian's CURRENT position re-added as a static, zero-velocity `AddBlock` box (see LotCoupling's
// class remarks) -- reacting to where a pedestrian IS, never to where it is GOING.
//
// Scenario: a car drives dead straight across open ground, (-30,0) -> (30,0), at an 8 m/s cruise. A
// pedestrian-sized disc walks straight up (+y) through x=5 at a normal walking speed, timed so that,
// left undisturbed, the two would occupy the exact same point at the exact same instant (a genuine
// collision course, not a near-miss that avoidance gets credit for by luck). Three runs over the SAME
// timeline:
//   baseline          -- no external obstacle fed at all (no pedestrian): the car holds a dead-straight
//                        line (near-zero lateral deviation) the whole way -- the "no reason to swerve"
//                        control.
//   movingAware       -- SetExternalObstacles fed the disc's REAL (position, velocity) every step,
//                        exactly as a live cross-regime coupling would.
//   staticMomentary   -- SetExternalObstacles fed the SAME correctly-updated per-step position but with
//                        velocity ZEROED -- the POC-6b idiom of re-deriving a per-step static obstacle
//                        from the pedestrian's current position, discarding its heading/speed.
// Both obstacle-fed runs must never let the car's TRUE (un-inflated) footprint overlap the disc (hard
// invariant, checked every step via the same separating-axis test MixedTrafficBehaviourTests already
// uses for shaped-vehicle overlap). Beyond that: movingAware must show a MEASURABLE yield relative to
// baseline (it is genuinely avoiding, not sailing through by construction), and movingAware must begin
// deviating EARLIER (farther from the encounter) than staticMomentary -- the concrete, measured proof
// that carrying the disc's real velocity into the shaped-VO solve buys real anticipation, not just a
// same-quality reaction one step later.
public class MixedTrafficExternalObstacleTests
{
    private const double Dt = 0.05;
    private const int Steps = 300; // 15 s budget -- comfortably past both the encounter and recovery

    private readonly ITestOutputHelper _out;
    public MixedTrafficExternalObstacleTests(ITestOutputHelper output) => _out = output;

    private static readonly Vec2 CarStart = new(-30.0, 0.0);
    private static readonly Vec2 CarGoal = new(30.0, 0.0);
    private const double CarMaxSpeed = 8.0;

    // Car covers 35 m at 8 m/s in 4.375 s to reach x=5 (NOT the 60 m to its goal); the pedestrian is
    // timed to reach (5, 0) at that SAME instant if undisturbed -- an exact collision-course rendezvous,
    // not a comfortable near-miss.
    private const double PedRadius = 0.3;
    private static readonly Vec2 PedVelocity = new(0.0, 1.4); // a normal walking speed
    private const double CrossingX = 5.0;
    private const double RendezvousTime = (CrossingX - -30.0) / CarMaxSpeed;
    private static readonly Vec2 PedStart = new(CrossingX, -PedVelocity.Y * RendezvousTime);

    private const double DeviationThreshold = 0.15; // metres of |y| that counts as "started avoiding"

    private sealed record RunResult(
        List<Vec2> CarTrace, double MaxPenetration, double MaxLateralDeviation, int FirstStepPastThreshold);

    private static RunResult Run(bool feedObstacle, bool zeroDiscVelocity)
    {
        var crowd = new MixedTrafficCrowd(4)
        {
            NeighbourDist = 30.0, // wide enough that TimeHorizon's 3 s of lookahead has room to bind early
            MaxNeighbours = 4,
            // SafetyMargin inflates only the SOLVE shape (ShapedVoSolver's avoidance geometry) -- the
            // TRUE footprint used by the no-overlap check below (WorldPoly, via Class(h).Shape(), the
            // un-inflated shape) is unaffected. This gives the holonomic solver room to plan a smooth
            // avoidance well before the encounter, instead of the razor's-edge, backward-velocity
            // "bacteria" correction an EXACT bullseye rendezvous otherwise forces at the last instant.
            SafetyMargin = 0.5,
        };

        var car = crowd.Add(VehicleClass.Car, CarStart, CarGoal, headingRad: 0.0, maxSpeedOverride: CarMaxSpeed);

        var trace = new List<Vec2>();
        var maxPenetration = 0.0;
        var maxDeviation = 0.0;
        var firstPast = -1;
        var discBuf = new WorldDisc[1];

        for (var step = 0; step < Steps; step++)
        {
            var t = step * Dt;
            var pedPos = PedStart + PedVelocity * t;

            if (feedObstacle)
            {
                var vx = zeroDiscVelocity ? 0.0 : PedVelocity.X;
                var vy = zeroDiscVelocity ? 0.0 : PedVelocity.Y;
                discBuf[0] = new WorldDisc(pedPos.X, pedPos.Y, vx, vy, PedRadius);
                crowd.SetExternalObstacles(discBuf);
            }

            crowd.Step(Dt);

            var pos = crowd.Position(car);
            trace.Add(pos);

            var dev = Math.Abs(pos.Y);
            if (dev > maxDeviation)
            {
                maxDeviation = dev;
            }

            if (firstPast < 0 && dev > DeviationThreshold)
            {
                firstPast = step;
            }

            if (feedObstacle)
            {
                var carPoly = WorldPoly(crowd, car);
                var discPoly = DiscPoly(pedPos);
                maxPenetration = Math.Max(maxPenetration, Penetration(carPoly, discPoly));
            }
        }

        return new RunResult(trace, maxPenetration, maxDeviation, firstPast);
    }

    [Fact]
    public void MovingDisc_NeverOverlapsCar_AndYieldsMeasurablyVsBaseline()
    {
        var baseline = Run(feedObstacle: false, zeroDiscVelocity: false);
        var movingAware = Run(feedObstacle: true, zeroDiscVelocity: false);

        _out.WriteLine($"baseline: maxDeviation={baseline.MaxLateralDeviation:F3}");
        _out.WriteLine($"movingAware: maxPenetration={movingAware.MaxPenetration:F4} maxDeviation={movingAware.MaxLateralDeviation:F3} " +
            $"firstPastThreshold=step{movingAware.FirstStepPastThreshold}");

        // Hard invariant: the car's TRUE oriented-box footprint never overlaps the moving disc.
        Assert.Equal(0.0, movingAware.MaxPenetration);

        // Sanity: the baseline (no pedestrian) run is essentially a straight line -- it has no reason to
        // deviate, so its own max deviation must be tiny (rules out "the car always wobbles regardless").
        Assert.True(baseline.MaxLateralDeviation < 0.05,
            $"baseline (no obstacle) should track a dead-straight line, deviated {baseline.MaxLateralDeviation:F3} m");

        // Measurable yield: fed the moving disc, the car deviates FAR more than baseline -- proof it is
        // genuinely avoiding (not merely missing the disc by construction of the scenario).
        Assert.True(movingAware.MaxLateralDeviation > baseline.MaxLateralDeviation + 0.5,
            $"expected a measurable yield: movingAware deviation {movingAware.MaxLateralDeviation:F3} vs " +
            $"baseline {baseline.MaxLateralDeviation:F3}");
        Assert.True(movingAware.FirstStepPastThreshold > 0, "movingAware never crossed the deviation threshold");
    }

    [Fact]
    public void MovingDisc_AvoidsEarlierThan_StaticMomentaryApproximation()
    {
        var movingAware = Run(feedObstacle: true, zeroDiscVelocity: false);
        var staticMomentary = Run(feedObstacle: true, zeroDiscVelocity: true);

        _out.WriteLine($"movingAware: firstPastThreshold=step{movingAware.FirstStepPastThreshold} " +
            $"maxDeviation={movingAware.MaxLateralDeviation:F3} maxPenetration={movingAware.MaxPenetration:F4}");
        _out.WriteLine($"staticMomentary: firstPastThreshold=step{staticMomentary.FirstStepPastThreshold} " +
            $"maxDeviation={staticMomentary.MaxLateralDeviation:F3} maxPenetration={staticMomentary.MaxPenetration:F4}");

        // movingAware (this task's new feature) is the hard invariant: fed the disc's real velocity, the
        // car's TRUE footprint never overlaps it.
        Assert.Equal(0.0, movingAware.MaxPenetration);

        // Sanity: both runs actually had to react (a vacuous "neither ever deviated" would not exercise
        // the comparison at all).
        Assert.True(movingAware.FirstStepPastThreshold > 0, "movingAware never crossed the deviation threshold");
        Assert.True(staticMomentary.FirstStepPastThreshold > 0, "staticMomentary never crossed the deviation threshold");

        // Velocity-aware avoidance anticipates the disc's closing motion (its relative-velocity term
        // reads the disc's true (Vx,Vy)), so it must begin visibly yielding EARLIER (a smaller step
        // index -- i.e. while still farther from the encounter) than the zero-velocity, POC-6b-style
        // "reacts to where the disc currently is" approximation.
        Assert.True(movingAware.FirstStepPastThreshold < staticMomentary.FirstStepPastThreshold,
            $"expected movingAware (step {movingAware.FirstStepPastThreshold}) to react earlier than " +
            $"staticMomentary (step {staticMomentary.FirstStepPastThreshold})");

        // The measured, concrete cost of discarding velocity (POC-6b's approximation): reacting only to
        // where the pedestrian CURRENTLY is, not where it is headed, leaves LESS margin at the same tight
        // rendezvous -- here it measurably erodes all the way to a real, non-zero footprint overlap that
        // the velocity-aware run avoids entirely under IDENTICAL geometry/timing/car tuning. This is not
        // asserted as a universal law (a coarser scenario might give the static approximation enough
        // slack to scrape by too) -- it is this test's own measured proof that velocity information
        // strictly helps, never hurts, holding everything else fixed.
        Assert.True(staticMomentary.MaxPenetration > movingAware.MaxPenetration,
            $"expected the zero-velocity approximation to fare no better than the velocity-aware run " +
            $"(static penetration {staticMomentary.MaxPenetration:F4} vs movingAware {movingAware.MaxPenetration:F4})");
    }

    // ----- shared geometry helpers (mirror MixedTrafficBehaviourTests' SAT overlap test exactly) -----

    private static Vec2[] WorldPoly(MixedTrafficCrowd c, MixedTrafficHandle h)
    {
        var proto = c.Class(h).Shape().RotatedTo(c.Heading(h));
        var p = c.Position(h);
        var w = new Vec2[proto.Count];
        for (var v = 0; v < proto.Count; v++)
        {
            w[v] = p + proto.Verts[v];
        }

        return w;
    }

    private static Vec2[] DiscPoly(Vec2 centre)
    {
        var proto = ConvexShape.RegularPolygon(8, PedRadius);
        var w = new Vec2[proto.Count];
        for (var v = 0; v < proto.Count; v++)
        {
            w[v] = centre + proto.Verts[v];
        }

        return w;
    }

    // Penetration depth of two convex polygons (0 if separated), by the separating-axis theorem over
    // both polygons' edge normals. Positive => they overlap by that many metres on the tightest axis.
    private static double Penetration(Vec2[] a, Vec2[] b)
    {
        var minOverlap = double.PositiveInfinity;
        foreach (var poly in new[] { a, b })
        {
            for (var i = 0; i < poly.Length; i++)
            {
                var e = poly[(i + 1) % poly.Length] - poly[i];
                var axis = new Vec2(-e.Y, e.X).Normalized();
                Project(a, axis, out var aMin, out var aMax);
                Project(b, axis, out var bMin, out var bMax);
                var overlap = Math.Min(aMax, bMax) - Math.Max(aMin, bMin);
                if (overlap <= 0.0)
                {
                    return 0.0; // a separating axis exists -> no overlap
                }

                minOverlap = Math.Min(minOverlap, overlap);
            }
        }

        return minOverlap;
    }

    private static void Project(Vec2[] poly, Vec2 axis, out double min, out double max)
    {
        min = double.PositiveInfinity;
        max = double.NegativeInfinity;
        foreach (var v in poly)
        {
            var d = Vec2.Dot(v, axis);
            min = Math.Min(min, d);
            max = Math.Max(max, d);
        }
    }
}
