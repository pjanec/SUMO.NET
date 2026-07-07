namespace Sim.Core;

// D7 (FastDataPlane ECS readiness -- the FDP-shaped seam / adapter): the ONE in-house `IWorld`
// backend this rung ships (see `IWorld.cs`'s header comment for the full seam rationale and why
// no `Fdp.Core` reference is added). Wraps the SAME `List<VehicleRuntime>` and `CommandBuffer`
// instance `Engine` already owned before this rung -- both handed in by reference at
// construction (a `List<T>`/class instance is itself a reference, so `Engine`'s own
// `LoadScenario` `Clear()`/`Add()` calls on that same list object, and its own `CommandBuffer`
// recording, are visible through `World` with no extra plumbing and no per-`LoadScenario`
// reconstruction needed).
//
// Byte-identical (this rung's done-condition): `World` adds no new state and makes no new
// decision -- `GetCommandBuffer()` returns the exact `CommandBuffer` instance `Engine` already
// constructed once and reused all scenario long; `ActiveVehicles()` constructs the exact same
// `ActiveVehicleQuery` value `Engine.ActiveVehicles()` used to construct directly (D6's
// `new(_vehicles)`), now via one extra (non-allocating) indirection. Nothing about WHAT is
// visited, WHEN a mutation applies, or in what order changes by routing through this class
// instead of touching `_vehicles`/`CommandBuffer` inline.
internal sealed class World : IWorld
{
    private readonly List<VehicleRuntime> _vehicles;
    private readonly ICommandBuffer _commandBuffer;

    public World(List<VehicleRuntime> vehicles, ICommandBuffer commandBuffer)
    {
        _vehicles = vehicles;
        _commandBuffer = commandBuffer;
    }

    public ICommandBuffer GetCommandBuffer() => _commandBuffer;

    // Zero-alloc struct-returning factory -- see IWorld.ActiveVehicles()'s own comment for why
    // this must stay a concrete struct return, never an IQuery/IEnumerable<T> interface.
    public ActiveVehicleQuery ActiveVehicles() => new(_vehicles);
}
