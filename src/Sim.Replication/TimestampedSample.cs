namespace Sim.Replication;

// docs/SUMOSHARP-PACKAGING-DESIGN.md §5 / D9 — the transport-neutral dead-reckoning sample: a
// VehicleRecord (the DR field set: Handle, Model, LaneHandle, Pos, PosLat, Speed, Accel, LatSpeed,
// Upcoming) paired with the arrival/sim time it was observed at. This is the data-model API type;
// each transport (DDS today, TCP/UDP later) is responsible for filling one of these from its own
// wire representation. Field-for-field identical to (and a drop-in replacement for) the ad-hoc
// DdsSubscriber.VehicleSample it is meant to eventually supersede -- same ctor parameter order
// (timestampSeconds, record) -- so a later consumer swap is a pure type substitution, no logic
// change.
public readonly struct TimestampedSample
{
    public TimestampedSample(double timestampSeconds, VehicleRecord record)
    {
        TimestampSeconds = timestampSeconds;
        Record = record;
    }

    public double TimestampSeconds { get; }
    public VehicleRecord Record { get; }
}
