namespace Sim.Core;

// SUMOSHARP-API.md §10: a per-step vehicle lifecycle event, drained by the host from Engine.Events each
// frame (it rides the same Step()-published snapshot as the read columns). Zero-alloc and thread-clean:
// no C# delegates. The host correlates an event to a vehicle it is tracking by VehicleHandle.
public enum SimEventKind
{
    // The vehicle passed SUMO-parity queued insertion and is now on the road (Pending -> Active).
    Departed = 0,

    // The vehicle finished its route (or was despawned) and left the simulation (-> Arrived).
    Arrived = 1,

    // Reserved: insertion gave up after a timeout. Not emitted yet (the engine queues indefinitely).
    InsertionFailed = 2,

    // Reserved: the vehicle was teleported past a jam. Not emitted yet (teleport is off in phase 1).
    Teleported = 3,
}

public readonly struct SimEvent
{
    public readonly VehicleHandle Handle;
    public readonly SimEventKind Kind;

    public SimEvent(VehicleHandle handle, SimEventKind kind)
    {
        Handle = handle;
        Kind = kind;
    }
}
