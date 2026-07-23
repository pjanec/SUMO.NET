# SumoSharp.LiveCity

The shared "live city" coupled-sim host: cars (SumoSharp's `Engine` + Krauss car-following) and a
pedestrian crowd (`SumoSharp.Pedestrians`) sharing one net, with cars yielding to pedestrians on a
crosswalk via a composite crowd-footprint source (`Engine.CrowdSource`).

`LiveCitySim` reproduces `src/Sim.Viz/SceneGen.cs`'s `BuildLiveCity` wiring and per-tick order as a
real-time, steppable, publish-ready object: construct it with a `LiveCityConfig`, call `Step()` once per
tick, and read back the coupled scene with `Sample()` (or consume the in-memory replication wires exposed
via `VehicleSource` / `PedSource`).

Unofficial, independent reimplementation; not affiliated with the Eclipse SUMO project.
