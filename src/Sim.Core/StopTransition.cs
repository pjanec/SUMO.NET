namespace Sim.Core;

// The plan-phase's proposed update to the front of a vehicle's own stop queue (see
// StopRuntime), threaded through MoveIntent so ExecuteMoves is the only place that mutates it
// (CLAUDE.md rule 3). Mirrors the three outcomes of MSVehicle::processNextStop for a
// non-waypoint stop:
//   - Resume=true:  duration expired this step -> resumeFromStopping() pops the stop.
//   - Resume=false, Reached=true: newly reached (or still-held) -- write back Reached/duration.
// (There is no fourth "not reached yet" case: Engine.ProcessNextStop returns a null
// StopTransition when nothing needs to change, exactly like MSVehicle.cpp's tail
// `return currentVelocity;` with no side effect on `stop.reached`.)
public readonly record struct StopTransition(bool Resume, bool Reached, double RemainingDuration);
