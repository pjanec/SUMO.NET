using Sim.Ingest;

namespace Sim.Core;

// Per-vehicle mutable runtime state, plus the immutable spawn template (Def) it was created
// from. Kept as one record-per-vehicle for now rather than a struct-of-arrays split: with a
// single rung-1 vehicle there is nothing yet to gain from the SoA reshape, and DESIGN.md's
// struct-of-arrays push is about the *data layout* paying for itself once many vehicles/
// systems exist -- deferring it here blocks nothing (Kinematics/MoveIntent are already
// separable structs, so an eventual SoA split is a mechanical extraction, not a redesign).
internal sealed class VehicleRuntime
{
    public required VehicleDef Def { get; init; }

    // Resolved (fully-defaulted) vType parameters (Sim.Ingest.VTypeDefaults) -- the car-
    // following model reads these, never the raw .rou.xml VType with its optional fields.
    public required ResolvedVType VType { get; init; }

    public bool Inserted;

    // Set once Pos reaches ArrivalPos (route end) during execute; distinct from Inserted so
    // InsertDepartingVehicles never mistakes "arrived" for "not yet departed" and re-inserts.
    public bool Arrived;
    public string LaneId = string.Empty;

    // Route-end position, in the same lane-relative units as Kinematics.Pos. Rung 1's route is
    // a single edge, so Pos is directly comparable to that edge's lane length; multi-edge
    // routes will need cumulative-length or per-edge-boundary tracking, deferred until a
    // scenario needs it.
    public double ArrivalPos;
    public Kinematics Kinematics;
    public MoveIntent Intent;

    // Rung 5: this vehicle's scheduled stops (Sim.Ingest.VehicleDef.Stops), in route order.
    // Front-of-queue only is ever consulted (MSVehicle::myStops is a deque with the same
    // front-only access pattern) -- populated once at LoadScenario time from the immutable Def,
    // then only ever mutated (front popped/updated) by Engine.ExecuteMoves.
    public Queue<StopRuntime> Stops { get; } = new();

    // Rung 8b: SUMO's MSLCM_LC2013::myKeepRightProbability -- a stateful per-vehicle accumulator
    // for the keep-right (Rechtsfahrgebot) lane-change incentive. Starts at 0 (SUMO's ctor
    // default); only ever mutated by Engine.ExecuteMoves from the plan phase's MoveIntent
    // (CLAUDE.md rule 3 -- Plan writes only MoveIntent, never this field directly).
    public double KeepRightProbability;
}
