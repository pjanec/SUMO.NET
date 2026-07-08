namespace Sim.Harness;

/// <summary>
/// VB-6 (VIZ_BENCH_TASKS.md Phase 2): one completed trip, as read from either a real SUMO
/// <c>--tripinfo-output</c> file or the engine's tripinfo ANALOG (<see cref="TripInfoParser"/>
/// reads both through the same schema subset -- see that class's header comment). Only the
/// fields the aggregate/statistical comparator needs are carried; SUMO's tripinfo has many more
/// attributes (routeLength, waitingTime, rerouteNo, devices, ...) that are irrelevant to
/// aggregate throughput/duration/speed comparison and are simply never read.
/// </summary>
public sealed record TripInfoRecord(
    string Id,
    double Depart,
    double Duration,
    double? ArrivalSpeed);
