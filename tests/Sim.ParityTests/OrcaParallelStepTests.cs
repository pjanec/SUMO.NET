using System;
using Sim.Core.Bridge;
using Sim.Core.Orca;
using Xunit;
using Xunit.Abstractions;

namespace Sim.ParityTests;

// POC-7a (docs/PEDESTRIAN-POC-PLAN.md POC-7; docs/PEDESTRIAN-DESIGN.md §3c/§9): OrcaCrowd.Step's PLAN
// phase is embarrassingly parallel BY CONSTRUCTION -- every agent's _newVelocity[i] is a pure function
// of the frozen start-of-step state and writes only its own slot. The refactor that unlocks
// parallelism (OrcaCrowd.UseParallelStep) moves Plan()'s scratch buffers (neighbour list, its
// distance-sq array, the ORCA line list, the obstacle-segment list, the spatial-hash candidate list)
// from shared instance fields into a per-worker ScratchSet, so concurrent Plan(i) calls never race on
// them.
//
// The hard requirement, same as OrcaSpatialHashTests' grid-vs-brute-force gate: BIT-IDENTITY. Threading
// must never change a single bit of the trajectory -- only the wall-clock. This test builds a large
// (2000-agent) crowd exercising every feature that touches scratch (spatial hash, static obstacles,
// external cross-regime discs, MaxNeighbours-bounded insertion, SymmetryBreak jitter) and runs it BOTH
// ways -- UseParallelStep=false (unchanged serial path) and UseParallelStep=true (the new parallel
// path, which also engages ParallelStepThreshold at this size) -- from an identical initial state, then
// asserts every agent's position AND velocity are bit-identical (exact double equality) at every step.
public class OrcaParallelStepTests
{
    private const double Dt = 0.2;
    private const double Radius = 0.4;
    private const double MaxSpeed = 1.6;
    private const int AgentCount = 2000;
    private const int Steps = 300;

    private readonly ITestOutputHelper _out;

    public OrcaParallelStepTests(ITestOutputHelper output) => _out = output;

    // Deterministic build: a large square grid of agents, each routed to the point mirrored through
    // the origin (so paths cross densely through the middle -- lots of neighbour-list churn), plus two
    // static-obstacle walls, a handful of external (cross-regime) discs, MaxNeighbours capping the
    // agent-agent neighbour list, and SymmetryBreak jitter (all of which touch a scratch buffer).
    private static OrcaCrowd Build(bool useParallel)
    {
        var crowd = new OrcaCrowd(AgentCount)
        {
            UseSpatialHash = true,
            MaxNeighbours = 10,
            SymmetryBreak = 0.05,
            UseParallelStep = useParallel,
        };

        // sqrt(2000) isn't integral -- use a rectangular grid that multiplies out to exactly AgentCount.
        const int gx = 50;
        const int gy = 40; // 50 * 40 == 2000
        var spacing = 1.4;
        var originX = -(gx - 1) * spacing / 2.0;
        var originY = -(gy - 1) * spacing / 2.0;

        for (var iy = 0; iy < gy; iy++)
        {
            for (var ix = 0; ix < gx; ix++)
            {
                var p = new Vec2(originX + ix * spacing, originY + iy * spacing);
                crowd.Add(p, Radius, MaxSpeed, goal: -p);
            }
        }

        Assert.Equal(AgentCount, crowd.Count);

        // Two static walls inside the crossing area (thin two-sided walls -- 2-vertex obstacles).
        crowd.AddObstacle(new[] { new Vec2(-8.0, 5.0), new Vec2(8.0, 5.0) });
        crowd.AddObstacle(new[] { new Vec2(-8.0, -5.0), new Vec2(8.0, -5.0) });

        // A handful of external (cross-regime) discs, e.g. lane vehicles the crowd yields to.
        var discs = new[]
        {
            new WorldDisc(-20.0, 0.0, 1.0, 0.0, 1.2),
            new WorldDisc(20.0, 2.0, -0.5, 0.3, 0.9),
            new WorldDisc(0.0, 25.0, 0.0, -0.8, 1.0),
            new WorldDisc(0.0, -25.0, 0.2, 0.6, 1.1),
        };
        crowd.SetExternalObstacles(discs);

        return crowd;
    }

    [Fact]
    public void LargeCrowd_ParallelStepMatchesSerial_BitIdentical()
    {
        var serial = Build(useParallel: false);
        var parallel = Build(useParallel: true);

        Assert.False(serial.UseParallelStep);
        Assert.True(parallel.UseParallelStep);
        Assert.True(AgentCount >= 256, "test must exceed OrcaCrowd's parallel-step size gate");

        for (var step = 0; step < Steps; step++)
        {
            serial.Step(Dt);
            parallel.Step(Dt);

            for (var i = 0; i < AgentCount; i++)
            {
                var ps = serial.Position(i);
                var pp = parallel.Position(i);
                var vs = serial.Velocity(i);
                var vp = parallel.Velocity(i);

                Assert.True(ps.X == pp.X && ps.Y == pp.Y,
                    $"position diverged at step {step}, agent {i}: " +
                    $"serial=({ps.X:R},{ps.Y:R}) parallel=({pp.X:R},{pp.Y:R})");
                Assert.True(vs.X == vp.X && vs.Y == vp.Y,
                    $"velocity diverged at step {step}, agent {i}: " +
                    $"serial=({vs.X:R},{vs.Y:R}) parallel=({vp.X:R},{vp.Y:R})");
            }
        }

        _out.WriteLine($"{AgentCount} agents x {Steps} steps: parallel bit-identical to serial.");
    }

    // Same bit-identity guarantee under an explicit MaxParallelism cap (exercises a different
    // Parallel.For partitioning than the runtime default, which is the whole point of the gate --
    // scheduling must never leak into the trajectory).
    [Fact]
    public void LargeCrowd_ParallelStepMatchesSerial_UnderPinnedThreadCount()
    {
        var serial = Build(useParallel: false);
        var parallel = Build(useParallel: true);
        parallel.MaxParallelism = 2;

        for (var step = 0; step < 50; step++)
        {
            serial.Step(Dt);
            parallel.Step(Dt);

            for (var i = 0; i < AgentCount; i++)
            {
                var ps = serial.Position(i);
                var pp = parallel.Position(i);
                Assert.True(ps.X == pp.X && ps.Y == pp.Y,
                    $"position diverged at step {step}, agent {i} (MaxParallelism=2): " +
                    $"serial=({ps.X:R},{ps.Y:R}) parallel=({pp.X:R},{pp.Y:R})");
            }
        }

        _out.WriteLine("MaxParallelism=2: parallel bit-identical to serial over 50 steps.");
    }
}
