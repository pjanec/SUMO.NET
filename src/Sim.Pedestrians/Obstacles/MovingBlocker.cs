using Sim.Core.Bridge;
using Sim.Core.Orca;

namespace Sim.Pedestrians.Obstacles;

// A moving external entity pedestrians must dodge (POC-5, docs/PEDESTRIAN-POC-PLAN.md POC-5 Req 3
// "walking external blocker"; docs/PEDESTRIAN-DESIGN.md §6 "OrcaCrowd.SetExternalObstacles makes
// peds avoid moving external discs"). This is NOT a new avoidance mechanism -- OrcaCrowd already
// consumes a WorldDisc[] every step via SetExternalObstacles. This class only holds the small piece
// of bookkeeping a caller driving such an entity needs: its own deterministic position update (a
// pure function of elapsed time, no System.Random, no per-step drift), so a test/driver can Advance
// it in lockstep with OrcaCrowd.Step and re-publish its disc each tick.
//
// Deliberately holonomic straight-line motion (constant velocity) -- this is a stand-in for "some
// other regime's entity" (an external car, another simulation's avatar, ...) whose real motion model
// lives elsewhere; the pedestrian side only needs its CURRENT position/velocity/radius as a WorldDisc
// each step, which is exactly Sim.Core.Bridge.WorldDisc.
public sealed class MovingBlocker
{
    public Vec2 Position { get; private set; }
    public Vec2 Velocity { get; }
    public double Radius { get; }

    public MovingBlocker(Vec2 start, Vec2 velocity, double radius)
    {
        Position = start;
        Velocity = velocity;
        Radius = radius;
    }

    // Advances position by exactly velocity * dt -- deterministic, no accumulation surprises beyond
    // ordinary double-precision arithmetic (same integration style OrcaCrowd.Step itself uses).
    public void Advance(double dt) => Position += Velocity * dt;

    public WorldDisc ToWorldDisc() => new(Position.X, Position.Y, Velocity.X, Velocity.Y, Radius);
}
