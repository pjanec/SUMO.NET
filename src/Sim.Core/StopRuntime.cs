namespace Sim.Core;

// Runtime mirror of a single scheduled <stop> (Sim.Ingest.StopDef), ported from the fields
// MSVehicle::MSStop (sumo/src/microsim/MSStop.h) actually reads for a non-waypoint lane stop
// (stop.getSpeed()==0): lane/startPos/endPos/duration plus the `reached` flag and the
// per-step-decremented `duration` countdown (MSVehicle.cpp's processNextStop, ~lines 1613-1897).
// Only ever mutated during Execute (Engine.ExecuteMoves applies a StopTransition computed
// during Plan) -- CLAUDE.md rule 3: the plan phase must not write shared/runtime state, even
// state as narrowly-scoped as "this vehicle's own next stop."
internal sealed class StopRuntime
{
    public required string LaneId { get; init; }
    public required double StartPos { get; init; }
    public required double EndPos { get; init; }

    // MSStop::getMinDuration's fallback (no until/ended modeled): the configured <stop
    // duration="..."/> in seconds, used to (re)initialize RemainingDuration once reached.
    public required double Duration { get; init; }

    public bool Reached;
    public double RemainingDuration;
}
