// StreamingLoopback -- a tutorial-style walkthrough of the transport-neutral replication contract
// (Sim.Replication.IReplicationSink / IReplicationSource) using the in-memory, non-DDS binding. Run
// it with:
//   dotnet run --project samples/StreamingLoopback
using Sim.Core;
using Sim.Replication;

internal static class Program
{
    private static int Main()
    {
        Console.WriteLine("StreamingLoopback — transport-neutral replication, no DDS involved.");

        // 1) InMemoryReplicationBus is a same-process implementation of IReplicationSink/IReplicationSource
        //    -- one binding among several (DdsPublisher/DdsSubscriber in SumoSharp.Replication.Dds are the
        //    real DDS binding of the SAME two interfaces). A consumer coding against Sink/Source never
        //    needs to know which transport is underneath.
        var bus = new InMemoryReplicationBus();

        // A handle is just (Index, Generation) -- no live Engine is needed to demonstrate the wire
        // contract, so we fabricate a couple by hand.
        var vehA = new VehicleHandle(0, 1);
        var vehB = new VehicleHandle(1, 1);

        // 2) Publish the ONE-TIME static lane geometry (durable topic) a remote viewer needs to draw
        //    roads without the .net.xml file.
        var lanes = new[]
        {
            new GeometryCodec.LaneGeo(handle: 0, isInternal: false, width: 3.2f, length: 120f,
                points: new (float X, float Y)[] { (0f, 0f), (120f, 0f) }),
            new GeometryCodec.LaneGeo(handle: 1, isInternal: false, width: 3.2f, length: 80f,
                points: new (float X, float Y)[] { (120f, 0f), (200f, 0f) }),
        };
        bus.Sink.PublishGeometry(lanes);
        Console.WriteLine($"published {lanes.Length} lane(s) of geometry");

        // 3) Publish a spawn (durable, once per vehicle) announcing its physical dims.
        bus.Sink.PublishLifecycle(new LifecycleRecord(vehA, isSpawn: true, vTypeId: 0, length: 4.5f, width: 1.8f));
        bus.Sink.PublishLifecycle(new LifecycleRecord(vehB, isSpawn: true, vTypeId: 0, length: 4.5f, width: 1.8f));
        Console.WriteLine("published lifecycle: spawn A, spawn B");

        // 4) Publish 3 per-frame updates (volatile topic) with each vehicle's Pos advancing along its lane.
        for (var step = 0u; step < 3; step++)
        {
            var time = step * 1.0;
            var movers = new[]
            {
                new VehicleRecord(vehA, DrModel.LaneArc, laneHandle: 0,
                    pos: 10.0 + step * 5.0, posLat: 0.0, speed: 5.0, accel: 0.0, latSpeed: 0.0,
                    upcoming: new UpcomingLanes(stackalloc[] { 0, 1 })),
                new VehicleRecord(vehB, DrModel.LaneArc, laneHandle: 1,
                    pos: 20.0 + step * 3.0, posLat: 0.0, speed: 3.0, accel: 0.0, latSpeed: 0.0,
                    upcoming: new UpcomingLanes(stackalloc[] { 1 })),
            };
            bus.Sink.PublishFrame(step, time, movers);
            Console.WriteLine($"published frame {step} @ t={time:F1}s: A.pos={movers[0].Pos:F1} B.pos={movers[1].Pos:F1}");
        }

        // 5) Pump drains the queue into the receive-side registries -- exactly the same Pump-then-read
        //    pattern DdsSubscriber uses, so a caller cannot tell which binding it holds.
        bus.Source.Pump();

        Console.WriteLine();
        Console.WriteLine("--- read back through IReplicationSource (transport-neutral) ---");
        Console.WriteLine($"published {lanes.Length} lanes -> received {bus.Source.Geometry.Count} " +
                           $"(GeometryComplete={bus.Source.GeometryComplete})");
        Console.WriteLine($"published 2 spawns -> received {bus.Source.Dims.Count} dims entries");

        foreach (var handle in new[] { vehA, vehB })
        {
            var hist = bus.Source.History[handle];
            var newest = hist[hist.Count - 1]; // newest-last: index Count-1 is the most recent sample
            Console.WriteLine(
                $"published {hist.Count} frame(s) for {handle} -> newest sample: " +
                $"t={newest.TimestampSeconds:F1}s lane={newest.Record.LaneHandle} pos={newest.Record.Pos:F1} " +
                $"speed={newest.Record.Speed:F1}");
        }

        Console.WriteLine($"LatestVehicleSampleTime = {bus.Source.LatestVehicleSampleTime:F1}s");
        Console.WriteLine("done — a consumer coded only against IReplicationSource never touched DDS.");
        return 0;
    }
}
