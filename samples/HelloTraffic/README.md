# HelloTraffic

The smallest possible consumer of **SumoSharp.Core** — a tutorial-style walkthrough of the "quick
start" from `src/Sim.Core/README.md`, turned into a runnable console program.

> Unofficial, independent C# reimplementation of Eclipse SUMO's microscopic simulation core. Not
> affiliated with or endorsed by the Eclipse SUMO project.

## What this shows

- Loading a committed SUMO network with `Engine.LoadNetwork(...)` (no demand file needed — traffic is
  built at runtime, the pattern a game or digital twin uses).
- Defining a vehicle type at runtime with `Engine.DefineVType(new VTypeParams { ... })`, and using the
  engine's built-in `Engine.DefaultVType` as the alternative.
- Spawning vehicles with `Engine.SpawnVehicle(type, fromEdge, toEdge)`, routed by the engine's own
  shortest-path router.
- Stepping the simulation with `Engine.Step()` and reading live vehicle state back with **zero-allocation
  columnar spans**: `Engine.VehicleHandles` + `Engine.TryGetVehicle(handle, out VehicleState state)`.

Every call in `Program.cs` is commented inline, in order, as a tutorial.

## Installing the package (in your own project)

This sample uses a `ProjectReference` into `src/Sim.Core` because SumoSharp isn't published to
nuget.org yet. In a real consumer project you would instead run:

```bash
dotnet add package SumoSharp.Core
```

which pulls in `SumoSharp.Ingest` transitively (the `.net.xml`/`.rou.xml` parser SumoSharp.Core
depends on).

## Run it

```bash
dotnet run --project samples/HelloTraffic
```

or point it at any SUMO network:

```bash
dotnet run --project samples/HelloTraffic -- path/to/net.net.xml
```

By default it loads the committed `scenarios/15-reroute/net.net.xml`, spawns two vehicles
(`S -> A -> B -> D -> E` and `A -> B -> D -> E`), steps 20 times, and prints each vehicle's lane,
position, and speed at every step.

## See also

`samples/SumoSharp.GameHostSample` builds on the same API for a game-engine-shaped integration
(async `SimulationRunner`, obstacle injection, render interpolation).
