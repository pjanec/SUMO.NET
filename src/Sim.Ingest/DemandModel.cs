namespace Sim.Ingest;

// Immutable demand model, parsed once from .rou.xml. Only attributes explicitly present are
// captured for VType (CLAUDE.md/DESIGN.md: --save-state itself does not expand vType defaults,
// so this parser must not invent one either -- resolved defaulting is a separate, later
// cross-check against golden.vtype.json, not this ingest step).
//
// Each field is an optional override on top of the vClass-default table (Sim.Ingest.
// VTypeDefaults): a rou.xml <vType> only ever sets the attributes it explicitly needs (rung 4's
// leader sets maxSpeed="5.00", both rung 4 vTypes set sigma="0"), and everything else is left to
// the resolver's `override ?? default` per attribute -- never invented here.
public sealed record VType(
    string Id,
    string? VClass,
    double? Sigma,
    double? MaxSpeed = null,
    double? Accel = null,
    double? Decel = null,
    double? Tau = null,
    double? MinGap = null,
    double? Length = null,
    double? EmergencyDecel = null,
    double? SpeedFactor = null);

public sealed record Route(
    string Id,
    IReadOnlyList<string> Edges);

public sealed record VehicleDef(
    string Id,
    string TypeId,
    string RouteId,
    double Depart,
    double DepartPos,
    double DepartSpeed,
    int DepartLaneIndex);

public sealed record DemandModel(
    IReadOnlyList<VType> VTypes,
    IReadOnlyDictionary<string, VType> VTypesById,
    IReadOnlyList<Route> Routes,
    IReadOnlyDictionary<string, Route> RoutesById,
    IReadOnlyList<VehicleDef> Vehicles);
