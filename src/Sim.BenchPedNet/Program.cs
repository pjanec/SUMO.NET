using System.Globalization;
using Sim.Core;
using Sim.Core.Orca;
using Sim.Replication;

// POC-7b (docs/PEDESTRIAN-POC-PLAN.md POC-7; docs/PEDESTRIAN-DESIGN.md §7): the SINGLE-STREAM bandwidth
// deliverable. The owner's CORRECTED framing (see the design doc's §7 header note) is that DDS multicast
// is ONE stream every IG channel reads -- there is no per-channel culling to build or measure, so the
// only figure that matters is the aggregate bytes/sec on that one stream. This tool builds the target
// population, ENCODES it through the real `FrameCodec` (the same codec `Sim.Replication` ships), and
// measures the actual returned byte counts -- it does not hand-compute record-size * count arithmetic
// for anything the codec can encode for real.
//
// Deliberately a SEPARATE project, NOT part of `dotnet test` (same convention as Sim.Bench / Sim.BenchCity
// / Sim.BenchCrowd -- see their own header comments): these are population/rate MODELING choices (a
// representative path length, a DR-gated sent fraction, a heartbeat interval), not a golden/parity
// assertion, so they must never gate the hermetic offline loop. Run manually:
//   dotnet run -c Release --project src/Sim.BenchPedNet
//
// No System.Random anywhere (CLAUDE.md) -- population generation uses Sim.Core.VehicleRng (seeded
// SplitMix64), exactly the engine's own per-entity RNG convention, so a rerun is bit-identical.
internal static class Program
{
    private const double CarRate = 10.0; // Hz, matches DESIGN.md §4.2/§7
    private const double PedHighRate = 10.0; // Hz, matches DESIGN.md §7 "FreeKinematic ... verbatim"
    private const double HeartbeatInterval = 3.0; // seconds, matches DESIGN.md §7 "sub-1 Hz heartbeat"
    private const double PathLifetime = 60.0; // seconds -- the amortization window the task spec names
    private const int PathPoints = 16; // representative low-power leg waypoint count
    private const double DrGatedFraction = 0.60; // "a realistic sent-fraction" per the task spec
    private const int DdsMaxSampleBytes = 64 * 1024; // ~64 KiB DDS sample chunking, for the note only

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var cars = 10_000;
        var highPeds = 10_000;
        var lowPeds = 90_000;

        for (var a = 0; a < args.Length; a++)
        {
            switch (args[a])
            {
                case "--cars": cars = int.Parse(args[++a]); break;
                case "--high-peds": highPeds = int.Parse(args[++a]); break;
                case "--low-peds": lowPeds = int.Parse(args[++a]); break;
            }
        }

        var totalPeds = highPeds + lowPeds;

        Console.WriteLine("POC-7b bandwidth benchmark -- single DDS-multicast stream");
        Console.WriteLine($"population: {cars} cars, {highPeds} high-power peds, {lowPeds} low-power peds ({totalPeds} peds total)");
        Console.WriteLine($"rates: cars/high-peds @ {CarRate} Hz, low-power heartbeat every {HeartbeatInterval} s, path lifetime {PathLifetime} s");
        Console.WriteLine($"DR-gated sent fraction (typical): {DrGatedFraction:P0}");
        Console.WriteLine();

        // --- Real encodes (the source of every number below) ---

        var vehicleRecs = BuildVehicles(cars, seed: 1);
        var highPedRecs = BuildPedFreeKinematic(highPeds, seed: 2);
        var allPedFreeKinematicRecs = BuildPedFreeKinematic(totalPeds, seed: 2); // scenario (b): all promoted
        var naiveCrowdRecs = BuildCrowd(totalPeds, seed: 2); // scenario (c): unquantized comparison
        var pathSample = BuildPathArc(1, seed: 3, PathPoints)[0];
        var heartbeatSample = BuildPathArc(1, seed: 3, pointCount: 0)[0];

        var carsTypical = EncodeVehicleClass("cars (quantized-N/A, existing 48 B VehicleRecord)", vehicleRecs, SentCount(cars, DrGatedFraction), CarRate);
        var carsWorst = EncodeVehicleClass("cars, 100% sent", vehicleRecs, cars, CarRate);
        var highPedsTypical = EncodePedFreeKinematicClass("high-power peds (18 B quantized)", highPedRecs, SentCount(highPeds, DrGatedFraction), PedHighRate);
        var highPedsWorst = EncodePedFreeKinematicClass("high-power peds, 100% sent", highPedRecs, highPeds, PedHighRate);
        var lowPedsTypical = EncodeLowPowerClass("low-power peds (PathArc, amortized + heartbeat)", lowPeds, pathSample, heartbeatSample);

        var spikePeds = EncodePedFreeKinematicClass("ALL peds promoted to FreeKinematic, 100% sent", allPedFreeKinematicRecs, totalPeds, PedHighRate);
        var naivePeds = EncodeCrowdClass("ALL peds, unquantized CrowdRecord (32 B), 100% sent", naiveCrowdRecs, totalPeds, PedHighRate);

        Console.WriteLine("--- Scenario (a): typical (LOD split, DR-gated) ---");
        Print(carsTypical);
        Print(highPedsTypical);
        Print(lowPedsTypical);
        var typicalTotal = carsTypical.BytesPerSecond + highPedsTypical.BytesPerSecond + lowPedsTypical.BytesPerSecond;
        PrintTotal(typicalTotal);
        Console.WriteLine();

        Console.WriteLine("--- Scenario (b): worst-case spike (all peds promoted, everything at 100%) ---");
        Print(carsWorst);
        Print(spikePeds);
        var spikeTotal = carsWorst.BytesPerSecond + spikePeds.BytesPerSecond;
        PrintTotal(spikeTotal);
        Console.WriteLine();

        Console.WriteLine("--- Scenario (c): naive baseline (unquantized FreeKinematic for all peds) ---");
        Print(carsWorst);
        Print(naivePeds);
        var naiveTotal = carsWorst.BytesPerSecond + naivePeds.BytesPerSecond;
        PrintTotal(naiveTotal);
        Console.WriteLine();

        Console.WriteLine("--- Reference rows (typical-vs-worst per class) ---");
        Print(carsTypical); Print(carsWorst);
        Print(highPedsTypical); Print(highPedsWorst);
        Console.WriteLine();

        const double budgetMbit = 500.0;
        Console.WriteLine($"Budget: {budgetMbit} Mbit/s (50% of a 1 Gbit link). DDS per-sample/framing overhead is extra");
        Console.WriteLine("on top of the payload bytes measured here (chunked into ~64 KiB DDS samples) -- a small");
        Console.WriteLine("multiplier, not modeled precisely. See docs/PEDESTRIAN-POC7B-FINDINGS.md for the write-up.");
        Console.WriteLine();
        ReportChunking("cars @ 100%", cars, FrameCodec.VehicleRecordSize);
        ReportChunking("peds @ 100% (18 B quantized)", totalPeds, FrameCodec.PedFreeKinematicRecordSize);
        ReportChunking("peds @ 100% (32 B unquantized, naive)", totalPeds, FrameCodec.CrowdRecordSize);

        return 0;
    }

    private static int SentCount(int total, double fraction) => (int)Math.Round(total * fraction);

    // --- Population builders (deterministic, VehicleRng-seeded -- no System.Random) ---

    private static VehicleRecord[] BuildVehicles(int n, ulong seed)
    {
        var recs = new VehicleRecord[n];
        for (var i = 0; i < n; i++)
        {
            var rng = VehicleRng.SeedFor(seed, i);
            var pos = 20.0 + rng.NextDouble() * 400.0;
            var speed = 3.0 + rng.NextDouble() * 12.0;
            var lane = i % 2_000;
            recs[i] = new VehicleRecord(
                new VehicleHandle((uint)i, 1), DrModel.LaneArc, lane,
                pos, posLat: 0.0, speed, accel: 0.0, latSpeed: 0.0,
                new UpcomingLanes(new[] { lane + 1 }));
        }

        return recs;
    }

    private static PedFreeKinematicRecord[] BuildPedFreeKinematic(int n, ulong seed)
    {
        var recs = new PedFreeKinematicRecord[n];
        for (var i = 0; i < n; i++)
        {
            var rng = VehicleRng.SeedFor(seed, i);
            var x = (rng.NextDouble() - 0.5) * 2_000.0; // a ~2 km square district
            var y = (rng.NextDouble() - 0.5) * 2_000.0;
            var speed = 0.8 + rng.NextDouble() * 1.4; // ~0.8-2.2 m/s walking speed
            var heading = rng.NextDouble() * 2.0 * Math.PI;
            var vx = speed * Math.Cos(heading);
            var vy = speed * Math.Sin(heading);
            var radius = 0.22 + rng.NextDouble() * 0.15;
            recs[i] = new PedFreeKinematicRecord(new VehicleHandle((uint)i, 1), x, y, vx, vy, radius);
        }

        return recs;
    }

    private static CrowdRecord[] BuildCrowd(int n, ulong seed)
    {
        var recs = new CrowdRecord[n];
        for (var i = 0; i < n; i++)
        {
            var rng = VehicleRng.SeedFor(seed, i);
            var x = (rng.NextDouble() - 0.5) * 2_000.0;
            var y = (rng.NextDouble() - 0.5) * 2_000.0;
            var speed = 0.8 + rng.NextDouble() * 1.4;
            var heading = rng.NextDouble() * 2.0 * Math.PI;
            var vx = speed * Math.Cos(heading);
            var vy = speed * Math.Sin(heading);
            var radius = 0.22 + rng.NextDouble() * 0.15;
            recs[i] = new CrowdRecord(new VehicleHandle((uint)i, 1), x, y, z: 0.0, vx, vy, radius);
        }

        return recs;
    }

    // pointCount == 0 builds the "heartbeat" stand-in: a PathArc record with no waypoints (see
    // EncodeLowPowerClass's comment for why this is a fair, codec-real model of the liveness ping).
    private static PathArcRecord[] BuildPathArc(int n, ulong seed, int pointCount)
    {
        var recs = new PathArcRecord[n];
        for (var i = 0; i < n; i++)
        {
            var rng = VehicleRng.SeedFor(seed, i);
            var points = new Vec2[pointCount];
            var x = (rng.NextDouble() - 0.5) * 2_000.0;
            var y = (rng.NextDouble() - 0.5) * 2_000.0;
            for (var k = 0; k < pointCount; k++)
            {
                x += (rng.NextDouble() - 0.5) * 40.0; // a wandering ~40 m leg spacing
                y += (rng.NextDouble() - 0.5) * 40.0;
                points[k] = new Vec2(x, y);
            }

            recs[i] = new PathArcRecord(new VehicleHandle((uint)i, 1), speed: 1.2, startTime: 0.0, points);
        }

        return recs;
    }

    // --- Real-codec measurement ---

    private readonly record struct ClassResult(string Name, int RecordsSent, int RecordSizeBytes, int FrameBytes, double RatePerSecond, double BytesPerSecond)
    {
        public double MbitPerSecond => BytesPerSecond * 8.0 / 1_000_000.0;
    }

    private static ClassResult EncodeVehicleClass(string name, VehicleRecord[] all, int sentCount, double rateHz)
    {
        var slice = all.AsSpan(0, sentCount);
        var buf = new byte[FrameCodec.VehicleFrameSize(sentCount)];
        var written = FrameCodec.WriteVehicleFrame(buf, step: 0, time: 0f, slice);
        return new ClassResult(name, sentCount, FrameCodec.VehicleRecordSize, written, rateHz, written * rateHz);
    }

    private static ClassResult EncodePedFreeKinematicClass(string name, PedFreeKinematicRecord[] all, int sentCount, double rateHz)
    {
        var slice = all.AsSpan(0, sentCount);
        var buf = new byte[FrameCodec.PedFreeKinematicFrameSize(sentCount)];
        var written = FrameCodec.WritePedFreeKinematicFrame(buf, step: 0, time: 0f, slice);
        return new ClassResult(name, sentCount, FrameCodec.PedFreeKinematicRecordSize, written, rateHz, written * rateHz);
    }

    private static ClassResult EncodeCrowdClass(string name, CrowdRecord[] all, int sentCount, double rateHz)
    {
        var slice = all.AsSpan(0, sentCount);
        var buf = new byte[FrameCodec.CrowdFrameSize(sentCount)];
        var written = FrameCodec.WriteCrowdFrame(buf, step: 0, time: 0f, slice);
        return new ClassResult(name, sentCount, FrameCodec.CrowdRecordSize, written, rateHz, written * rateHz);
    }

    // The low-power class never sends per-step position -- its steady-state cost is the ONE-TIME path,
    // amortized over its lifetime, plus a periodic heartbeat. Both are measured through the real
    // `WritePathArcFrame` codec path (a heartbeat is modeled as a PathArc record with zero waypoints --
    // handle + speed + startTime + a zero point-count, 14 B -- which is a real, codec-encoded value, not
    // an invented number; a production liveness message could be smaller still, so this is a conservative
    // stand-in, not an optimistic one).
    private static ClassResult EncodeLowPowerClass(string name, int lowPedCount, PathArcRecord pathSample, PathArcRecord heartbeatSample)
    {
        var pathBuf = new byte[FrameCodec.PathArcFrameSize(new[] { pathSample })];
        var pathWritten = FrameCodec.WritePathArcFrame(pathBuf, step: 0, time: 0f, new[] { pathSample });
        var pathRecordBytes = pathWritten - FrameCodec.HeaderSize; // per-ped payload, header amortizes across a real batch

        var hbBuf = new byte[FrameCodec.PathArcFrameSize(new[] { heartbeatSample })];
        var hbWritten = FrameCodec.WritePathArcFrame(hbBuf, step: 0, time: 0f, new[] { heartbeatSample });
        var heartbeatRecordBytes = hbWritten - FrameCodec.HeaderSize;

        var pathBytesPerSecond = pathRecordBytes / PathLifetime * lowPedCount;
        var heartbeatBytesPerSecond = heartbeatRecordBytes / HeartbeatInterval * lowPedCount;
        var totalBytesPerSecond = pathBytesPerSecond + heartbeatBytesPerSecond;

        Console.WriteLine($"    (path record: {pathRecordBytes} B/{PathPoints}-pt leg amortized over {PathLifetime:F0} s; " +
                           $"heartbeat record: {heartbeatRecordBytes} B every {HeartbeatInterval:F0} s; {lowPedCount} low-power peds)");

        return new ClassResult(name, lowPedCount, RecordSizeBytes: 0, FrameBytes: 0, RatePerSecond: 0, totalBytesPerSecond);
    }

    private static void Print(ClassResult r) =>
        Console.WriteLine($"  {r.Name,-58} sent={r.RecordsSent,7}  {r.BytesPerSecond,14:N0} B/s  {r.MbitPerSecond,10:F2} Mbit/s");

    private static void PrintTotal(double bytesPerSecond)
    {
        var mbit = bytesPerSecond * 8.0 / 1_000_000.0;
        const double budget = 500.0;
        var headroomPct = (1.0 - mbit / budget) * 100.0;
        Console.WriteLine($"  {"TOTAL (single multicast stream)",-58}              {bytesPerSecond,14:N0} B/s  {mbit,10:F2} Mbit/s" +
                           $"   [{(mbit <= budget ? "FITS" : "OVER")} 500 Mbit/s budget, {headroomPct:F1}% headroom]");
    }

    private static void ReportChunking(string label, int recordCount, int recordSize)
    {
        var maxPerChunk = FrameChunker.MaxRecordsForPayload(DdsMaxSampleBytes, recordSize);
        var chunks = FrameChunker.ChunkCount(recordCount, maxPerChunk);
        Console.WriteLine($"  {label,-40}: {recordCount} records @ {recordSize} B -> {chunks} DDS samples/tick (<= {DdsMaxSampleBytes / 1024} KiB each)");
    }
}
