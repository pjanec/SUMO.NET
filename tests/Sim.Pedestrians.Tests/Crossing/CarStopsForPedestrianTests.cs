using Sim.Core;
using Sim.Core.Orca;
using Xunit;

namespace Sim.Pedestrians.Tests.Crossing;

// POC-2 (docs/PEDESTRIAN-POC-PLAN.md POC-2, success condition 3; docs/PEDESTRIAN-DESIGN.md §6 "Cars
// stop for crossers -- mostly BUILT"): a car approaching POC-0's junction on a GREEN through-movement
// halts before reaching a pedestrian standing on its lane, via the EXISTING Engine.CrowdSource /
// ICrowdFootprintSource seam (Engine.CrowdLongitudinalConstraint) -- exactly the pattern
// src/Sim.Evac/EvacDirector.cs uses (Engine.SpawnVehicle + Engine.CrowdSource = an OrcaCrowd, engine
// and crowd stepped in lockstep). No new production code is needed for the avoidance itself (per
// §6); this test only supplies the pedestrian-as-obstacle input and asserts the outcome.
//
// Attributing the stop to the PED, not the signal: the car is routed "nc"->"cs" (straight through
// the junction, matching the task's example) and departs at t=0, while tlLogic "c"'s phase0 [0,37)
// gives that movement a hard 'G' (see CrossingTlReaderTests / the net's <tlLogic id="c">, linkIndex
// 1/2 = 'G' at position 0-36.9s of the cycle) -- so the car has an uncontested green the whole
// approach. The pedestrian is placed directly on the car's own lane at y=111.6 (inside the junction's
// south exit, well short of the "cs" edge, so the car cannot pass it before its own green covers the
// whole transit -- transit time is ~10-12s at the lane's 13.89 m/s limit, comfortably inside the 37s
// green window). The only thing that can make the car brake here is the pedestrian.
public class CarStopsForPedestrianTests
{
    private static string NetPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "net.net.xml");

    private const double PedY = 111.6; // just south of the junction, on the car's "nc"->"cs" path
    private const double PedRadius = 0.3;
    private const double Dt = 1.0; // Engine's default StepLength (Engine.cs DefaultConfig)

    [Fact]
    public void Car_HaltsBeforePedestrian_MinGapNeverNegative_SpeedNearZeroBeforeCrossing()
    {
        var engine = new Engine();
        engine.LoadNetwork(NetPath);
        var vtype = engine.DefineVType(new VTypeParams { VClass = "passenger", Sigma = 0.0 });
        var carHandle = engine.SpawnVehicle(vtype, "nc", "cs", departPos: 5.0);

        VehicleState carState = SettleOntoVehicleLane(engine, carHandle, out var inserted);
        Assert.True(inserted, "car should have inserted within a few steps of spawning");

        // Place the pedestrian directly on the car's own lane (same x the car is already travelling
        // on), so it is squarely in the car's path regardless of which "nc"/"cs" lane index the
        // router picked.
        var crowd = new OrcaCrowd();
        var pedPos = new Vec2(carState.X, PedY);
        crowd.Add(pedPos, PedRadius, maxSpeed: 0.0, goal: pedPos); // stationary: goal == own position
        engine.CrowdSource = crowd;

        var minGap = double.MaxValue;
        var minSpeedNearPed = double.MaxValue;
        const double nearPedThreshold = 8.0; // metres: "close enough that a real stop should show"

        for (var i = 0; i < 40; i++)
        {
            engine.Step();
            crowd.Step(Dt);

            if (!engine.TryGetVehicle(carHandle, out carState))
            {
                break; // arrived/despawned -- nothing left to measure
            }

            // South-heading route: VehicleState.X/Y is the car's FRONT (Engine.cs's Pos is the
            // front-bumper arc-length, ported from SUMO's myState.myPos convention -- see
            // AddObstacle(laneHandle, frontPos, ...) and the "front" comments throughout Engine.cs).
            // The pedestrian is stationary at PedY, south of (below) the approaching front, so the
            // longitudinal gap is simply carFrontY - pedY: positive while the front has not yet
            // reached the pedestrian's line.
            var gap = carState.Y - PedY;
            minGap = Math.Min(minGap, gap);

            if (gap < nearPedThreshold)
            {
                minSpeedNearPed = Math.Min(minSpeedNearPed, carState.Speed);
            }
        }

        // Success condition 3a: the ped is never inside the car's footprint -- the minimum
        // longitudinal gap over the whole run never goes negative.
        Assert.True(minGap >= 0.0, $"car's front reached/passed the pedestrian: min gap = {minGap:F3} m");

        // Success condition 3b: the car's speed reaches ~0 before reaching the crossing (i.e. it
        // actually halted, not just slowed a little and coasted up close).
        Assert.True(minSpeedNearPed < 0.3,
            $"expected the car to nearly stop near the pedestrian; min speed within {nearPedThreshold} m was {minSpeedNearPed:F3} m/s");

        // Sanity: the pedestrian genuinely sat inside the car's original (unobstructed) path -- i.e.
        // this is a real stop, not a no-op where the car never got close.
        Assert.True(minGap < nearPedThreshold, $"car never got close to the pedestrian: min gap = {minGap:F3} m");
    }

    [Fact]
    public void Determinism_CarAndPedestrianTrajectory_IsIdenticalAcrossIndependentRuns()
    {
        (double[] CarX, double[] CarY, double[] CarSpeed, Vec2[] PedPos) RunOnce()
        {
            var engine = new Engine();
            engine.LoadNetwork(NetPath);
            var vtype = engine.DefineVType(new VTypeParams { VClass = "passenger", Sigma = 0.0 });
            var carHandle = engine.SpawnVehicle(vtype, "nc", "cs", departPos: 5.0);

            var carState = SettleOntoVehicleLane(engine, carHandle, out _);

            var crowd = new OrcaCrowd();
            var pedPos = new Vec2(carState.X, PedY);
            var pedIdx = crowd.Add(pedPos, PedRadius, maxSpeed: 0.0, goal: pedPos);
            engine.CrowdSource = crowd;

            var carX = new List<double>();
            var carY = new List<double>();
            var carSpeed = new List<double>();
            var pedTrace = new List<Vec2>();

            for (var i = 0; i < 40; i++)
            {
                engine.Step();
                crowd.Step(Dt);

                if (!engine.TryGetVehicle(carHandle, out carState))
                {
                    break;
                }

                carX.Add(carState.X);
                carY.Add(carState.Y);
                carSpeed.Add(carState.Speed);
                pedTrace.Add(crowd.Position(pedIdx));
            }

            return (carX.ToArray(), carY.ToArray(), carSpeed.ToArray(), pedTrace.ToArray());
        }

        var run1 = RunOnce();
        var run2 = RunOnce();

        Assert.Equal(run1.CarX.Length, run2.CarX.Length);
        for (var i = 0; i < run1.CarX.Length; i++)
        {
            Assert.Equal(run1.CarX[i], run2.CarX[i], precision: 12);
            Assert.Equal(run1.CarY[i], run2.CarY[i], precision: 12);
            Assert.Equal(run1.CarSpeed[i], run2.CarSpeed[i], precision: 12);
            Assert.Equal(run1.PedPos[i].X, run2.PedPos[i].X, precision: 12);
            Assert.Equal(run1.PedPos[i].Y, run2.PedPos[i].Y, precision: 12);
        }
    }

    // Steps the engine until the car is inserted AND has moved off its departure lane onto a real
    // vehicle lane. Insertion (SpawnVehicle -> actually appearing in the read surface) happens inside
    // Step(), not at SpawnVehicle time (mirrors EvacDirector's own VehState.Seen handling) -- but on
    // POC-0's fixture a freshly-inserted car's FIRST reported lane is index 0 of its departure edge
    // (e.g. "nc_0"), which is the PEDESTRIAN-ONLY sidewalk lane (allow="pedestrian"), not a lane the
    // car can actually stay on -- it lane-changes onto a real vehicle lane (e.g. "nc_1") within the
    // next step or two. Reading VehicleState.X at that transient first reading gives the SIDEWALK's x,
    // not the car's actual travel lane -- placing a "blocking" pedestrian there would never actually
    // be in the car's path (verified with a standalone probe: the car sails straight through). So this
    // settles past any lane whose id ends "_0" (POC-0's sidewalk-lane convention on every approach
    // edge) before the caller reads VehicleState.X/Y to place a pedestrian.
    private static VehicleState SettleOntoVehicleLane(Engine engine, VehicleHandle handle, out bool inserted)
    {
        var state = default(VehicleState);
        inserted = false;
        for (var i = 0; i < 6; i++)
        {
            engine.Step();
            if (engine.TryGetVehicle(handle, out state))
            {
                inserted = true;
                if (!state.LaneId.EndsWith("_0", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        return state;
    }
}
