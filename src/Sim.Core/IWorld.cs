namespace Sim.Core;

// D7 (FastDataPlane ECS readiness -- the FDP-shaped seam / adapter, READINESS ONLY, TASKS.md
// line ~603). Mirrors the surface FDP's own `World`/`View` expose (`World.CreateEntity`/
// `AddComponent`/`GetComponentRW<T>`/`Query().With<T>()`/`view.GetCommandBuffer()` -- see
// FastDataPlane Docs/architectural-rules.md) at the granularity this engine actually needs: a
// way to reach the deferred-structural-mutation buffer (`GetCommandBuffer()`, the
// `view.GetCommandBuffer()` analog) and a way to reach the "active, on-road vehicle" query (the
// `Query().With<Inserted>().Without<Arrived>().Build()` analog) every hot-path system already
// walks (D6's `ActiveVehicleQuery`/`ActiveVehicles()`).
//
// This is the drop-in point: `Engine` is rewritten by this rung to hold an `IWorld` and route
// every `_commandBuffer`/`ActiveVehicles()` call through it, instead of touching the concrete
// `CommandBuffer`/`_vehicles` list directly. A later `Fdp.Core`-backed `IWorld` implementation
// could be substituted for `World` (below) WITHOUT touching any system in `Engine.cs` -- but
// this project does NOT add that backend, or any `Fdp.Core`/`FastDataPlane` reference (the
// briefing's "readiness, not integration" line): `World` is the ONE backend this rung ships,
// wrapping the exact same in-house `List<VehicleRuntime>`/`CommandBuffer` store built across
// D3-D6.
//
// Why `ActiveVehicles()` returns the concrete `ActiveVehicleQuery` STRUCT, not an `IQuery`/
// `IEnumerable<VehicleRuntime>` interface (CLAUDE.md rule 5 / this rung's "absolute constraint
// 3"): FDP's own `Query()` surface is struct-based for exactly this reason -- boxing a struct
// enumerator behind an interface (or wrapping it in `IEnumerable<T>`) forces a heap allocation
// on every single `foreach`, every vehicle, every step, which is precisely the per-step alloc
// D4 spent a whole rung removing (BASELINE.md's D4 row: 735.8 B -> 207.1 B/veh-step). A
// factory METHOD that returns the struct BY VALUE keeps the seam real (callers go through
// `IWorld`, not `Engine._vehicles` directly) while staying zero-alloc: the `foreach` over the
// returned `ActiveVehicleQuery` still compiles to the same duck-typed `MoveNext()`/`Current`
// pattern VehicleQuery.cs's own header comment describes, with no interface dispatch inside the
// loop body itself (only the ONE call to `ActiveVehicles()` crosses the interface boundary, not
// each enumeration step). This is the explicit design note the briefing asks for: the "query
// abstraction" IS `IWorld.ActiveVehicles()`'s struct-returning factory shape, not a separate
// `IQuery`/`IEnumerable` type.
internal interface IWorld
{
    // The FDP `view.GetCommandBuffer()` analog: the ONE reusable, deferred structural-mutation
    // buffer (ChangeLane/ReplaceRoute/Destroy/Flush) every phase-barrier flush in Engine.cs
    // (UpdateReroutes/ExecuteMoves/DecideSpeedGainChanges) already targets.
    ICommandBuffer GetCommandBuffer();

    // The FDP `Query().With<Inserted>().Without<Arrived>().Build()` analog, returned as the
    // concrete zero-alloc struct (see this interface's own header comment for why).
    ActiveVehicleQuery ActiveVehicles();
}
