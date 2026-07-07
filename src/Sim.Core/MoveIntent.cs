namespace Sim.Core;

// Seam 4 (DESIGN.md): the plan phase writes ONLY to the owning vehicle's own MoveIntent --
// never to shared state, even single-threaded (this discipline is what turns later
// multithreading into a scheduling change, not a rewrite). LatOffset is the lane-change
// bridge: LC2013 (phase 1) will write a target lane index here and integration snaps to it;
// SL2015 (phase 2) will write a continuous lateral offset and integration drifts toward it.
// It stays 0 in this task -- no lane changes exist yet.
public struct MoveIntent
{
    public double NewSpeed;
    public double LatOffset;

    // Rung 5: the plan phase's proposed update to the front of this vehicle's own stop queue
    // (see StopTransition/StopRuntime) -- null when there is no stop, the stop isn't reached
    // yet, or reached-and-still-holding-with-nothing-to-change-in-bookkeeping (there is always
    // something to write while reached, so in practice this is null only pre-reach). Applied by
    // Engine.ExecuteMoves, never read/written elsewhere during Plan.
    public StopTransition? StopUpdate;

    // Rung 8b (LC2013 keep-right, DESIGN.md Seam 4): the plan phase's proposed structural lane
    // change, threaded through MoveIntent exactly like StopUpdate above rather than mutating
    // VehicleRuntime.LaneId directly -- ExecuteMoves is the only place a lane change is actually
    // applied (via the command buffer), never Plan. Null when the keep-right decision does not
    // fire this step (no right neighbor, or accumulator hasn't crossed threshold yet).
    public string? TargetLaneId;

    // The plan phase's updated value for VehicleRuntime.KeepRightProbability (SUMO's
    // myKeepRightProbability), written back by ExecuteMoves alongside TargetLaneId. Only
    // meaningful when the vehicle's current lane has a right neighbor (see Engine's guard);
    // otherwise it is always 0, matching the fact that a vehicle on lane index 0 never
    // accumulates a keep-right incentive.
    public double KeepRightProbability;
}
