namespace Sim.Core.Mixed;

// P0-2 (docs/PEDESTRIAN-TASKS.md, docs/PEDESTRIAN-DESIGN.md §3(d)): a stable, zero-allocation reference
// to a vehicle slot in MixedTrafficCrowd, laid out to MIRROR OrcaHandle (Sim.Core.Orca.OrcaHandle) --
// P0-1's just-landed template for this exact pattern -- which itself mirrors the engine's existing
// handle convention (Sim.Core.ObstacleHandle, Sim.Core.VehicleHandle). Same 2-field shape, but its OWN,
// DISTINCT id space: a MixedTrafficHandle must never be interchanged with an OrcaHandle (or an
// ObstacleHandle/VehicleHandle) even if the numeric Index happens to coincide -- they address
// completely different crowds/stores.
//
// Generation 0 is never handed out for a live slot (MixedTrafficCrowd starts every slot's generation at
// 1, and bumps it again on every Remove), so `default(MixedTrafficHandle)` (== Invalid) never resolves
// to a live vehicle -- exactly OrcaHandle's / ObstacleHandle's "generation 0 is reserved" convention.
public readonly record struct MixedTrafficHandle(int Index, uint Generation)
{
    // The never-valid sentinel (Index 0, Generation 0). A live slot's generation is always >= 1, so this
    // is never equal to a real handle.
    public static MixedTrafficHandle Invalid => default;

    // Cheap, crowd-independent check: true unless this is the Invalid sentinel. This does NOT confirm
    // the slot is still alive in any particular MixedTrafficCrowd (that requires the generation to match
    // the crowd's live state, e.g. via MixedTrafficCrowd.IsAlive(handle)) -- it only rules out the
    // default/never-assigned value, the same cheap guard ObstacleHandle/OrcaHandle callers get from
    // comparing against their sentinel.
    public bool IsValid => this != Invalid;

    public override string ToString() => $"Mixed#{Index}.{Generation}";
}
