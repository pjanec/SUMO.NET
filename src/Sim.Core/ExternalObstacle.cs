namespace Sim.Core;

// B1: a live, non-SUMO obstacle injected onto a lane (DESIGN.md "Two futures" -- a real vehicle,
// pedestrian, or detection fed in from outside any offline SUMO run). FrontPos mirrors a
// vehicle's `pos` convention: it is the obstacle's DOWNSTREAM (front) edge on the lane, so its
// back (the edge a follower must not cross) is FrontPos - Length, exactly like a stopped leader's
// back = leader.Pos - leader.Length in LeaderFollowSpeedConstraint. Active only during
// [StartTime, EndTime) -- both default to "always active" so the common single-argument-set
// AddObstacle call (no start/end) is unconditionally active.
//
// B5-i: Speed and MaxDecel generalize this from a purely STATIC obstacle (B1's only case) to a
// MOVING one driven by an external, non-SUMO layer (navmesh/RVO agent, pedestrian, live
// detection). Speed is the agent's along-lane velocity in m/s, exactly as reported by that
// external layer for THIS step -- Engine dead-reckons FrontPos forward by Speed*dt once per step
// (AdvanceObstacles, Input phase) between owner corrections, the same "extrapolate a reported
// velocity, don't simulate a driver" contract UpdateObstacle documents. MaxDecel is the agent's
// braking capability, used only when Speed != 0 (see ObstacleConstraint's predMaxDecel
// conditional) -- for a static obstacle it is irrelevant (BrakeGap(0, ...) == 0 regardless), so
// leaving it at its default here never changes B1 behavior. Both default to the static case
// (Speed=0, MaxDecel=0) so every existing AddObstacle call site is unaffected.
public sealed record ExternalObstacle(
    string Id,
    string LaneId,
    double FrontPos,
    double Length,
    double StartTime,
    double EndTime,
    double Speed = 0.0,
    double MaxDecel = 0.0);
