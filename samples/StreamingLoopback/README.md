# StreamingLoopback

A tutorial-style walkthrough of **SumoSharp.Replication**'s transport-neutral streaming contract —
`IReplicationSink` / `IReplicationSource` — exercised end to end with **no DDS involved**.

> Unofficial, independent C# reimplementation of Eclipse SUMO's microscopic simulation core. Not
> affiliated with or endorsed by the Eclipse SUMO project.

## What this shows

`InMemoryReplicationBus` is a same-process, non-DDS **binding** of `IReplicationSink` /
`IReplicationSource` — proving the contract is transport-neutral rather than an after-the-fact
abstraction over DDS. The sample:

- Publishes one-time static lane **geometry** (`bus.Sink.PublishGeometry(...)`).
- Publishes a vehicle **lifecycle** record (`bus.Sink.PublishLifecycle(new LifecycleRecord(...))`).
- Publishes three per-frame **vehicle updates** with each vehicle's position advancing
  (`bus.Sink.PublishFrame(step, time, movers)`).
- Calls `bus.Source.Pump()` to drain the queue, then reads the SAME data back through the
  receive-side contract: `bus.Source.Geometry`, `bus.Source.Dims`, `bus.Source.History[handle]`
  (newest-last sample buffer), and `bus.Source.LatestVehicleSampleTime`.
- Prints a "published X → received X" narrative for each topic, so it is visible that nothing except
  the two interfaces was touched.

A consumer coding only against `IReplicationSink`/`IReplicationSource` cannot tell — and never needs to
know — which binding sits underneath. `DdsPublisher`/`DdsSubscriber` (in `Sim.Viewer.Core` today, and
the standalone `SumoSharp.Replication.Dds` package) implement the exact same two interfaces over
CycloneDDS; swapping `InMemoryReplicationBus` for a DDS pair is a pure binding substitution, no
consumer code change.

Every call in `Program.cs` is commented inline, in order, as a tutorial.

## Installing the package (in your own project)

This sample uses a `ProjectReference` into `src/Sim.Replication` because SumoSharp isn't published to
nuget.org yet. In a real consumer project you would instead run:

```bash
dotnet add package SumoSharp.Replication
```

## Run it

```bash
dotnet run --project samples/StreamingLoopback
```

It publishes lane geometry, a spawn for two vehicles, and three advancing frames of position updates,
then reads all of it back through `IReplicationSource` and prints the round trip.

## See also

DDS is just another `IReplicationSink`/`IReplicationSource` binding — `SumoSharp.Replication.Dds`
wires the same two interfaces to CycloneDDS.NET for real network replication (used by `src/Sim.Viewer`
and `src/Sim.Viewer.Raylib`); this sample proves the contract works with zero network/DDS dependency
at all.
