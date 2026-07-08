using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// Rung C7-i (TASKS.md "speedFactor distribution (heterogeneous desired speeds)"): the per-
// vehicle desired-speed multiplier, ported from:
//   sumo/src/microsim/MSVehicleType.cpp:89-91          computeChosenSpeedDeviation
//   sumo/src/utils/distribution/Distribution_Parameterized.cpp:107-120  sample() ("normc")
//   sumo/src/utils/common/RandHelper.cpp:137-147        randNorm() (polar/Marsaglia method)
//   sumo/src/utils/vehicle/SUMOVTypeParameter.cpp:70,374-378  the normc(1.0,dev,0.2,2.0) default
//     + the default.speeddev override
// integrated at Engine.LoadScenario's once-at-creation draw (VehicleRuntime.SpeedFactor) and
// threaded into KraussModel.LaneVehicleMaxSpeed's four Engine.cs call sites.
//
// FULLY OFFLINE -- no SUMO, no golden for the speeddev>0 fixtures below (speeddev>0 is outside
// CLAUDE.md's phase-1 exact-parity determinism ladder, same as C1's sigma>0 fixture); these are
// BEHAVIORAL/PROPERTY tests against the engine's own TrajectorySet, mirroring
// RungC1DawdleTests' idiom. The OWNER DECISION (TASKS.md C7-i, mirrors C1) is that the
// statistical bar is ENSEMBLE/AGGREGATE, not RNG-exact -- these tests check the speeddev=0
// byte-identical control, DETERMINISM (same seed -> same result), ENSEMBLE HETEROGENEITY
// (distribution shape/bounds across seeds), and C1 INDEPENDENCE (the speedFactor draw's salted
// RNG never touches VehicleRuntime.RngState / the dawdle stream) -- never an exact numeric match
// to any SUMO RandHelper stream.
public class RungC7SpeedFactorTests
{
    private static readonly string SpeedFactorScenarioDir =
        Path.Combine(RepoRoot(), "scenarios", "_fixtures", "speedfactor-single-lane");
    private static readonly string IndependenceScenarioDir =
        Path.Combine(RepoRoot(), "scenarios", "_fixtures", "speedfactor-independence");
    private static readonly string ControlScenarioDir =
        Path.Combine(RepoRoot(), "scenarios", "01-single-free-flow");

    private const double LaneMaxSpeed = 13.89;

    private static Engine LoadSpeedFactorEngine(ulong seed)
    {
        var engine = new Engine { Seed = seed };
        engine.LoadScenario(
            Path.Combine(SpeedFactorScenarioDir, "net.net.xml"),
            Path.Combine(SpeedFactorScenarioDir, "rou.rou.xml"),
            Path.Combine(SpeedFactorScenarioDir, "config.sumocfg"));
        return engine;
    }

    // Absolute constraint #1 (the C7-i briefing): speeddev=0 (every existing scenario's own
    // default.speeddev="0") must draw NO random number at all -- NormcDistribution.SampleNormc's
    // `dev<=0` branch returns the vType's mean speedFactor (1.0) immediately -- so this control
    // scenario reaches EXACTLY the lane's free-flow speed, byte-identical to every pre-C7 rung
    // (mirrors RungC1DawdleTests.SigmaZero_StillReachesExactFreeFlowSpeed_NoDawdleDrawOccurs).
    [Fact]
    public void SpeedDevZero_ReachesExactFreeFlowSpeed_NoSpeedFactorDrawOccurs()
    {
        var engine = new Engine();
        engine.LoadScenario(
            Path.Combine(ControlScenarioDir, "net.net.xml"),
            Path.Combine(ControlScenarioDir, "rou.rou.xml"),
            Path.Combine(ControlScenarioDir, "config.sumocfg"));

        var trajectory = engine.Run(80);
        var last = trajectory.PointsFor("veh0").Values.Last();

        Assert.Equal(LaneMaxSpeed, last.Speed, precision: 6);
    }

    // Determinism (the load-bearing CLAUDE.md guarantee): two runs with the SAME engine seed
    // produce byte-identical trajectories even with default.speeddev>0 -- same seed -> same
    // salted per-entity speedFactor draw -> same resolved LaneVehicleMaxSpeed at every step.
    [Fact]
    public void SameSeed_SpeedDevPositive_ProducesByteIdenticalTrajectories()
    {
        var engineA = LoadSpeedFactorEngine(seed: 123);
        var engineB = LoadSpeedFactorEngine(seed: 123);

        var trajA = engineA.Run(60).PointsFor("veh0");
        var trajB = engineB.Run(60).PointsFor("veh0");

        Assert.True(trajA.Count > 1, "expected veh0 to be present at multiple timesteps");
        Assert.Equal(trajA.Count, trajB.Count);

        foreach (var (time, pointA) in trajA)
        {
            Assert.True(trajB.TryGetValue(time, out var pointB), $"missing matching point at t={time}");
            Assert.Equal(pointA.Pos, pointB.Pos);
            Assert.Equal(pointA.Speed, pointB.Speed);
        }
    }

    // Ensemble heterogeneity: over an ENSEMBLE of N independent engine seeds (single vehicle
    // each, sigma=0 so steady-state speed is driven ONLY by the drawn speedFactor -- no dawdle
    // noise), the steady-state cruise speeds show POSITIVE VARIANCE (the distribution is actually
    // applied, not silently collapsed to the mean), a MEAN close to the lane's free-flow speed
    // (normc(1.0, 0.1, ...) is centered on 1.0 and truncation at 8 sigma from the clamp is
    // negligible), and every sample lies within the normc clamp's bounds
    // [0.2*laneMaxSpeed, 2.0*laneMaxSpeed] -- proving the sampler's reject-resample clamp is
    // honored (SampleNormc/Distribution_Parameterized.cpp's while(val<min||val>max) loop).
    [Fact]
    public void Ensemble_SpeedFactorHeterogeneity_PositiveVarianceMeanNearFreeFlowBoundedByClamp()
    {
        const int seedCount = 50;
        var steadySpeeds = new List<double>();

        for (ulong seed = 1; seed <= seedCount; seed++)
        {
            var engine = LoadSpeedFactorEngine(seed);
            var trajectory = engine.Run(60).PointsFor("veh0");
            var last = trajectory.Values.Last();
            steadySpeeds.Add(last.Speed);
        }

        Assert.Equal(seedCount, steadySpeeds.Count);

        // Bounded by the normc(mean=1.0, dev=0.1, min=0.2, max=2.0) clamp.
        foreach (var speed in steadySpeeds)
        {
            Assert.InRange(speed, 0.2 * LaneMaxSpeed, 2.0 * LaneMaxSpeed);
        }

        var mean = steadySpeeds.Average();
        Assert.True(
            Math.Abs(mean - LaneMaxSpeed) < 1.0,
            $"ensemble mean steady-state speed should stay near the free-flow speed: mean={mean}, freeFlow={LaneMaxSpeed}");

        var variance = steadySpeeds.Select(s => (s - mean) * (s - mean)).Average();
        Assert.True(variance > 1e-3, $"speedFactor should introduce real cross-vehicle heterogeneity: variance={variance}");
    }

    // C1 independence (the C7-i briefing's absolute constraint #2): the once-at-creation
    // speedFactor draw MUST use a SEPARATE, salted VehicleRng (VehicleRng.SeedFor(Seed,
    // entityIndex, salt)) that never touches VehicleRuntime.RngState -- C1's per-step dawdle
    // stream. VehicleRuntime is `internal sealed`, so this integration test proves it
    // BEHAVIORALLY rather than by peeking at the field directly:
    //
    // rou_low.rou.xml / rou_high.rou.xml differ ONLY in their vType's speedFactor MEAN (1.0 vs
    // 1.8); both use default.speeddev=0.05 (a REAL, nonzero dev -- so BOTH runs' speedFactor
    // sampler actually draws from its own salted RNG, the exact code path capable of stealing
    // draws from RngState if buggy) and the SAME sigma=0.5/accel/Seed.
    //
    // KraussModel.FinalizeSpeed's own formula (see its header comments) reduces, while the
    // vehicle is still in its depart-from-rest ACCEL RAMP (i.e. while the accel-limited
    // MaxNextSpeed(oldV) is still the smaller term against the target `laneSpeed*speedFactor`),
    // to `vMax = MaxNextSpeed(oldV)` -- a value that depends ONLY on the vehicle's own accel/oldV
    // history, NEVER on its target speed / speedFactor. Both fixtures' targets (~13.89 and ~25
    // m/s respectively) stay far above the 3-second accel ramp (accel=2.6 m/s^2 => ~7.8 m/s at
    // t=3), so for t in [1,3] this holds in BOTH runs simultaneously. Given that, if RngState's
    // dawdle draw sequence is truly independent of the speedFactor draw, the two runs' `oldV`/
    // dawdle-perturbed speed histories are DRIVEN BY THE SAME per-step random draw at every one
    // of those steps and must therefore be BYTE-IDENTICAL for t in [1,3] -- regardless of the
    // wildly different speedFactor/target between the two runs. Any bug that wired the
    // speedFactor sampler into the wrong VehicleRng (stealing extra draws from RngState at
    // creation, or vice versa) would desync this immediately at t=1.
    [Fact]
    public void SpeedFactorDraw_DoesNotConsumeDawdleRngState()
    {
        const ulong seed = 77;

        var engineLow = new Engine { Seed = seed };
        engineLow.LoadScenario(
            Path.Combine(IndependenceScenarioDir, "net.net.xml"),
            Path.Combine(IndependenceScenarioDir, "rou_low.rou.xml"),
            Path.Combine(IndependenceScenarioDir, "config.sumocfg"));

        var engineHigh = new Engine { Seed = seed };
        engineHigh.LoadScenario(
            Path.Combine(IndependenceScenarioDir, "net.net.xml"),
            Path.Combine(IndependenceScenarioDir, "rou_high.rou.xml"),
            Path.Combine(IndependenceScenarioDir, "config.sumocfg"));

        var trajLow = engineLow.Run(4).PointsFor("dawdler");
        var trajHigh = engineHigh.Run(4).PointsFor("dawdler");

        var comparedAnyStep = false;
        foreach (var time in new[] { 1.0, 2.0, 3.0 })
        {
            Assert.True(trajLow.TryGetValue(time, out var pointLow), $"missing low-mean point at t={time}");
            Assert.True(trajHigh.TryGetValue(time, out var pointHigh), $"missing high-mean point at t={time}");

            Assert.Equal(pointLow.Pos, pointHigh.Pos);
            Assert.Equal(pointLow.Speed, pointHigh.Speed);
            comparedAnyStep = true;
        }

        Assert.True(comparedAnyStep, "expected at least one accel-ramp step to compare");
    }

    // Mirrors RungC1DawdleTests.RepoRoot() / EngineRung1PlumbingTests.RepoRoot().
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
