using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// Rung C1-i (TASKS.md "Statistical parity / driver imperfection (sigma>0)"): Krauss dawdle2
// (sumo/src/microsim/cfmodels/MSCFModel_Krauss.cpp:129-151) + the per-entity seeded RNG
// (Sim.Core.VehicleRng), integrated at KraussModel.FinalizeSpeed's patchSpeedBeforeLC call site.
// FULLY OFFLINE -- no SUMO, no golden: sigma>0 is explicitly outside CLAUDE.md's phase-1 exact-
// determinism ladder, so these are BEHAVIORAL/PROPERTY tests against the engine's own
// TrajectorySet, mirroring RungB5MovingObstacleTests' structure/idiom. The OWNER DECISION
// (TASKS.md C1) is that the statistical bar is ENSEMBLE/AGGREGATE, not RNG-exact -- these tests
// therefore check DETERMINISM (same seed -> same result), an IMPERFECTION EFFECT (sigma>0 slows
// the vehicle down and introduces variance), SEED SENSITIVITY (different seeds -> different but
// bounded trajectories), and PARALLEL-SAFETY (UseParallelPlan doesn't change the result) --
// never an exact numeric match to any SUMO RandHelper stream.
public class RungC1DawdleTests
{
    private static readonly string DawdleScenarioDir =
        Path.Combine(RepoRoot(), "scenarios", "_fixtures", "dawdle-single-lane");
    private static readonly string ControlScenarioDir =
        Path.Combine(RepoRoot(), "scenarios", "01-single-free-flow");

    private const double LaneMaxSpeed = 13.89;

    private static Engine LoadDawdleEngine(ulong seed, bool useParallelPlan = false)
    {
        var engine = new Engine { Seed = seed, UseParallelPlan = useParallelPlan };
        engine.LoadScenario(
            Path.Combine(DawdleScenarioDir, "net.net.xml"),
            Path.Combine(DawdleScenarioDir, "rou.rou.xml"),
            Path.Combine(DawdleScenarioDir, "config.sumocfg"));
        return engine;
    }

    // sigma==0 byte-identical guarantee (CLAUDE.md rule 3 / the C1-i briefing's absolute
    // constraint #1): re-run an EXISTING sigma=0 parity scenario and confirm it is still exactly
    // what it always was -- KraussModel.FinalizeSpeed's `vType.Sigma > 0.0` guard means no draw
    // ever happens here, so this is simply scenario 01's own free-flow trajectory, unperturbed by
    // this rung's changes.
    [Fact]
    public void SigmaZero_StillReachesExactFreeFlowSpeed_NoDawdleDrawOccurs()
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
    // produce byte-identical trajectories even with sigma>0 -- same seed -> same per-entity draw
    // sequence -> same dawdle result at every step.
    [Fact]
    public void SameSeed_ProducesByteIdenticalTrajectories_EvenWithSigmaPositive()
    {
        var engineA = LoadDawdleEngine(seed: 123);
        var engineB = LoadDawdleEngine(seed: 123);

        var trajA = engineA.Run(60).PointsFor("dawdler");
        var trajB = engineB.Run(60).PointsFor("dawdler");

        Assert.True(trajA.Count > 1, "expected the dawdler to be present at multiple timesteps");
        Assert.Equal(trajA.Count, trajB.Count);

        foreach (var (time, pointA) in trajA)
        {
            Assert.True(trajB.TryGetValue(time, out var pointB), $"missing matching point at t={time}");
            Assert.Equal(pointA.Pos, pointB.Pos);
            Assert.Equal(pointA.Speed, pointB.Speed);
        }
    }

    // Imperfection has an effect: with sigma>0, the dawdler's steady-state mean speed is
    // STRICTLY BELOW the sigma=0 free-flow speed (dawdling slows it down -- dawdle2 only ever
    // subtracts from vMax, never adds), and its step-to-step speed shows positive variance (it is
    // not a constant like the sigma=0 control).
    [Fact]
    public void SigmaPositive_MeanSpeedBelowFreeFlowAndSpeedHasVariance()
    {
        var engine = LoadDawdleEngine(seed: 7);
        var trajectory = engine.Run(60).PointsFor("dawdler");

        // Steady-state window: skip the initial ~10s acceleration-from-rest ramp so the mean/
        // variance below reflect dawdling around cruise speed, not the accel-limited departure.
        var steadySpeeds = trajectory.Values.Where(p => p.Time >= 20.0).Select(p => p.Speed).ToList();
        Assert.True(steadySpeeds.Count > 5, "expected several steady-state samples");

        var mean = steadySpeeds.Average();
        Assert.True(
            mean < LaneMaxSpeed - 0.01,
            $"sigma>0 mean steady-state speed should be strictly below free-flow max: mean={mean}, max={LaneMaxSpeed}");

        var variance = steadySpeeds.Select(s => (s - mean) * (s - mean)).Average();
        Assert.True(variance > 1e-6, $"sigma>0 speed should vary step-to-step, not be constant: variance={variance}");

        // Contrast: the sigma=0 control (scenario 01's veh0) has ZERO variance once it settles --
        // a constant free-flow cruise speed.
        var controlEngine = new Engine();
        controlEngine.LoadScenario(
            Path.Combine(ControlScenarioDir, "net.net.xml"),
            Path.Combine(ControlScenarioDir, "rou.rou.xml"),
            Path.Combine(ControlScenarioDir, "config.sumocfg"));
        var controlTrajectory = controlEngine.Run(60).PointsFor("veh0");
        var controlSteadySpeeds = controlTrajectory.Values.Where(p => p.Time >= 20.0).Select(p => p.Speed).ToList();
        var controlVariance = controlSteadySpeeds.Select(s => (s - LaneMaxSpeed) * (s - LaneMaxSpeed)).Average();
        Assert.Equal(0.0, controlVariance, precision: 9);
    }

    // Seed sensitivity: two DIFFERENT engine seeds produce DIFFERENT trajectories (the RNG
    // actually drives divergence), but both stay within sane bounds (speed in [0, vMax], no NaN,
    // monotone (non-decreasing) position -- dawdle2's own MAX2(0, ...) floor and finalizeSpeed's
    // vMax cap guarantee this).
    [Fact]
    public void DifferentSeeds_ProduceDifferentButBoundedTrajectories()
    {
        var engineA = LoadDawdleEngine(seed: 1);
        var engineB = LoadDawdleEngine(seed: 2);

        var trajA = engineA.Run(60).PointsFor("dawdler");
        var trajB = engineB.Run(60).PointsFor("dawdler");

        var anyDifferent = false;
        var lastPos = double.NegativeInfinity;

        foreach (var (time, pointA) in trajA)
        {
            Assert.True(trajB.TryGetValue(time, out var pointB), $"missing matching point at t={time}");

            Assert.False(double.IsNaN(pointA.Speed), $"NaN speed at t={time} (seed=1)");
            Assert.False(double.IsNaN(pointB.Speed), $"NaN speed at t={time} (seed=2)");
            Assert.InRange(pointA.Speed, 0.0, LaneMaxSpeed);
            Assert.InRange(pointB.Speed, 0.0, LaneMaxSpeed);

            Assert.True(pointA.Pos >= lastPos - 1e-9, $"position should be non-decreasing: t={time}, pos={pointA.Pos}, lastPos={lastPos}");
            lastPos = pointA.Pos;

            if (Math.Abs(pointA.Speed - pointB.Speed) > 1e-9 || Math.Abs(pointA.Pos - pointB.Pos) > 1e-9)
            {
                anyDifferent = true;
            }
        }

        Assert.True(anyDifferent, "two different seeds should diverge somewhere in the trajectory");
    }

    // Parallel determinism (D8's UseParallelPlan safety argument, now extended to sigma>0): a
    // fixed seed under UseParallelPlan=true reproduces the single-threaded sigma>0 result exactly
    // -- each entity draws from its own private RngState, so thread-order never matters, even
    // with only one vehicle in this fixture the point is that the SAME call path
    // (Parallel.For-driven ComputeMoveIntent) is exercised and still matches.
    [Fact]
    public void ParallelPlan_WithSigmaPositive_ReproducesSingleThreadedResult()
    {
        var sequential = LoadDawdleEngine(seed: 55, useParallelPlan: false);
        var parallel = LoadDawdleEngine(seed: 55, useParallelPlan: true);

        var trajSeq = sequential.Run(60).PointsFor("dawdler");
        var trajPar = parallel.Run(60).PointsFor("dawdler");

        Assert.Equal(trajSeq.Count, trajPar.Count);
        foreach (var (time, pointSeq) in trajSeq)
        {
            Assert.True(trajPar.TryGetValue(time, out var pointPar), $"missing matching point at t={time}");
            Assert.Equal(pointSeq.Pos, pointPar.Pos);
            Assert.Equal(pointSeq.Speed, pointPar.Speed);
        }
    }

    // Mirrors EngineRung1PlumbingTests.RepoRoot() / RungB5MovingObstacleTests.RepoRoot().
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
