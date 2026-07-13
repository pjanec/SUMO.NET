namespace Sim.Core;

// A per-vehicle runtime override of the individual SUMO driver knobs (PANIC-EVAC.md R2). Every
// field is OPTIONAL: null leaves the vehicle's current resolved value untouched, so the knobs are
// INDEPENDENTLY settable -- the external evac layer can nudge one dimension or bulk-set them all.
// "Flee mode" is NOT a distinct state in the core; it is simply this override applied with a set of
// aggressive values (the preset lives in the external layer, Sim.Evac -- the core only exposes the
// surface). Fed to Engine.SetVehicleParams, which merges the non-null fields onto the running
// vehicle's ResolvedVType. Purely additive: a scenario that never calls SetVehicleParams is
// byte-identical to today (determinism hash 909605E965BFFE59 unmoved).
public readonly record struct VehicleParamOverride
{
    // Desired-speed multiplier (VehicleRuntime.SpeedFactor). Raising it makes the vehicle want to
    // drive above the lane limit -- the primary "push harder" flee lever.
    public double? SpeedFactor { get; init; }

    // Free-flow speed cap (m/s).
    public double? MaxSpeed { get; init; }

    // Desired time headway (s). Lowering it tightens following -> tailgating under flee.
    public double? Tau { get; init; }

    // Minimum standstill gap (m). Lowering it lets vehicles pack closer.
    public double? MinGap { get; init; }

    // Acceleration / deceleration ability (m/s^2). Higher accel = jumps into gaps faster.
    public double? Accel { get; init; }
    public double? Decel { get; init; }

    // Emergency deceleration (m/s^2). If left null while Decel is raised past the current
    // EmergencyDecel, the setter lifts EmergencyDecel to match (it must never be below Decel).
    public double? EmergencyDecel { get; init; }

    // Right-of-way relaxation at junctions (SUMO jmIgnoreFoe*, MSLink::blockedAtTime /
    // MSVehicle::checkLinkLeaderCurrentAndParallel). Raising these makes a fleeing driver force
    // gaps a courteous driver would yield -- the ingredient that turns a rush into gridlock.
    // NOTE: the ignore-foe arms consult a per-vehicle RNG; leave these null for a strictly
    // deterministic run, or set the *speed* gate only where a deterministic threshold suffices.
    public double? JmIgnoreFoeProb { get; init; }
    public double? JmIgnoreFoeSpeed { get; init; }
    public double? JmIgnoreJunctionFoeProb { get; init; }
}
