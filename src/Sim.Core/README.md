# SumoSharp.Core

An **unofficial** C#/.NET reimplementation of [Eclipse SUMO](https://eclipse.dev/sumo/)'s
microscopic traffic-simulation core, validated for **behavioural parity** against SUMO. Load a SUMO
network, build traffic at runtime, step it deterministically (or run it async), read vehicle state
with zero-allocation spans, and inject live external obstacles — for **games, training pipelines, and
digital twins**.

> **Not affiliated with or endorsed by the Eclipse SUMO project.** "SUMO" is a trademark of the
> Eclipse Foundation. This is an independent, clean-room-style reimplementation of the simulation
> algorithms; it is not the SUMO software.

## License

`EPL-2.0 OR GPL-2.0-or-later` (inherited from SUMO — this is a derivative work). **EPL-2.0 is weak,
file-level copyleft:** a proprietary game or application **may** link this library and keep its own
source closed; you only need to keep the SUMO-derived files under EPL and publish modifications *to
those files*. This is not legal advice — consult counsel for commercial use.

## What it does (and does not)

Reimplements SUMO's **per-step microsimulation** (Krauss car-following, LC2013 lane changing,
junctions, traffic lights, rail) on a data-oriented, parallel-ready core. It **consumes** SUMO's
file formats — you still use SUMO's `netconvert` to build the `.net.xml` network. It does not
reimplement `netconvert`, routing import, OSM parsing, emissions, or persons.

## Quick start

```csharp
using Sim.Core;

var engine = new Engine();

// Load a SUMO scenario (net + demand + config) ...
engine.LoadScenario("net.net.xml", "rou.rou.xml", "config.sumocfg");

// ... or start from a network only and build traffic at runtime:
engine.LoadNetwork("net.net.xml");
var car = engine.DefineVType(new VTypeParams { Sigma = 0.0, MaxSpeed = 13.89 });
var v   = engine.SpawnVehicle(car, fromEdge: "A", toEdge: "E");   // routed by shortest path

// Step the simulation and read live state (zero-alloc columnar spans).
engine.Step();
foreach (var h in engine.VehicleHandles)
    if (engine.TryGetVehicle(h, out var s))
        Console.WriteLine($"{s.VehicleId}: lane={s.LaneId} pos={s.Pos:F1} x={s.X:F1} y={s.Y:F1} z={s.Z:F1}");

// Inject a live external obstacle (a pedestrian, a stalled car, a detection) — cars react.
var lane = engine.GetLane("A_0");
var ped  = engine.AddObstacle(lane, frontPos: 120.0, length: 0.5, latPos: 0.0, width: 0.6);
engine.UpdateObstacle(ped, frontPos: 121.0, speed: 1.2);   // per-step correction, zero-alloc
engine.RemoveObstacle(ped);

// Reroute / redirect / despawn at runtime.
engine.Reroute(v, avoidEdges: new[] { "BD" });
engine.SetDestination(v, "E");
engine.Despawn(v);
```

### Async (game / digital-twin loop)

```csharp
var runner = new SimulationRunner(engine);
runner.Start(targetHz: 30);                       // background thread steps the sim
var h = runner.Invoke(e => e.SpawnVehicle(car, edges));   // mutations applied at the step boundary
// ... on your render thread, read the immutable published snapshot each frame:
var snap = runner.Snapshot;                        // SoA columns + events, safe to read cross-thread
```

## Determinism

Phase-1 scenarios are exactly reproducible (`sigma=0`, fixed depart, Euler integration). The engine
uses per-entity seeded RNG, so results never depend on thread scheduling. `Step()` reproduces `Run()`
bit-for-bit; the parallel plan reproduces the single-threaded trajectory.

## Links

- Repository & docs: https://github.com/pjanec/SUMO.NET
- API design of record: `docs/SUMOSHARP-API.md`
