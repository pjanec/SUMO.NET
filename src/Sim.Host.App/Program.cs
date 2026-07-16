using System.Diagnostics;
using System.Globalization;
using CycloneDDS.Runtime;
using Sim.Core;
using Sim.Host;
using Sim.Ingest;
using Sim.Replication;
using Sim.Replication.Dds;

// docs/DEMO-CITY3D-DESIGN.md "src/Sim.Host.App — the generic headless DDS host":
//   dotnet run --project src/Sim.Host.App -- --scenario <dir|net.xml> [--transport dds|inmem]
//       [--hz <n>] [--seconds <n> | --steps <n>] [--spawn <n>]
//
// Loads a scenario (net + rou + cfg), drives Sim.Core.Engine through a manually-ticked
// Sim.Core.SimulationRunner, and hands each step's SimulationSnapshot to
// Sim.Host.ReplicationPublisher, which translates it into the transport-neutral
// Sim.Replication wire records and writes them through whichever IReplicationSink --transport
// selects: `dds` (Sim.Replication.Dds.DdsReplicationSink, the real remote transport) or `inmem`
// (Sim.Replication.InMemoryReplicationBus, a same-process self-test with no native dependency).
// No rendering, no GPU -- this is the reusable headless host the City3D remote viewer (T2.2) and
// any other Sim.Replication consumer subscribes to.
string? scenarioArg = null;
var transport = "dds";
var hz = 10.0;
double? secondsArg = null;
int? stepsArg = null;
var spawn = 0;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--scenario":
            scenarioArg = args[++i];
            break;
        case "--transport":
            transport = args[++i];
            break;
        case "--hz":
            hz = double.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--seconds":
            secondsArg = double.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--steps":
            stepsArg = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--spawn":
            spawn = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        default:
            Console.Error.WriteLine($"Sim.Host.App: unknown argument '{args[i]}'.");
            return 1;
    }
}

if (scenarioArg is null)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project src/Sim.Host.App -- --scenario <dir|net.xml> " +
        "[--transport dds|inmem] [--hz <n>] [--seconds <n> | --steps <n>] [--spawn <n>]");
    return 1;
}

if (transport is not ("dds" or "inmem"))
{
    Console.Error.WriteLine($"Sim.Host.App: unknown --transport '{transport}' (expected dds|inmem).");
    return 1;
}

// Accept either a scenario directory (resolve its *.net.xml) or a direct net.xml path, then resolve
// the sibling *.rou.xml / *.sumocfg from the same directory -- every committed scenario dir (dev
// through the perf ladder) carries its own demand + config alongside the net (mirrors
// Sim.Viewer/Program.cs's ResolveNetPath + EngineHost's scenario-dir detection).
var netPath = ResolveNetPath(scenarioArg);
var scenarioDir = Path.GetDirectoryName(Path.GetFullPath(netPath))
    ?? throw new InvalidOperationException($"Could not resolve a directory for net path '{netPath}'.");
var rouPath = Directory.EnumerateFiles(scenarioDir, "*.rou.xml").FirstOrDefault()
    ?? throw new FileNotFoundException($"No *.rou.xml found alongside '{netPath}'.");
var cfgPath = Directory.EnumerateFiles(scenarioDir, "*.sumocfg").FirstOrDefault()
    ?? throw new FileNotFoundException($"No *.sumocfg found alongside '{netPath}'.");

Console.WriteLine($"Sim.Host.App: scenario='{scenarioDir}' transport={transport} hz={hz:F1} spawn={spawn}");

var engine = new Engine();
engine.LoadScenario(netPath, rouPath, cfgPath);
// The engine parses+owns its own copy of the network internally but does not expose it; the publisher
// needs a NetworkModel for lane geometry, so parse it again here (same source file, same result --
// EngineHost.cs does the identical double-parse for the same reason).
var network = NetworkParser.Parse(netPath);

using var runner = new SimulationRunner(engine);
runner.EnableSnapshotPool(capacity: 3);

if (spawn > 0)
{
    SpawnAmbient(engine, runner, spawn);
}

var publisher = new ReplicationPublisher();

DdsParticipant? participant = null;
InMemoryReplicationBus? bus = null;
IReplicationSink sink;

if (transport == "dds")
{
    participant = new DdsParticipant();
    sink = new DdsReplicationSink(participant);
}
else
{
    bus = new InMemoryReplicationBus();
    sink = bus.Sink;
}

try
{
    publisher.PublishGeometryOnce(network, sink);

    if (transport == "dds")
    {
        // DDS discovery is async -- give any already-running readers time to match before the step
        // loop starts publishing (Sim.Viewer/LoopbackSelfTest.cs's proven pattern).
        Thread.Sleep(500);
    }

    var stopRequested = false;
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true; // let the loop below exit cleanly (dispose the sink/participant) instead of
                          // the process dying mid-write.
        stopRequested = true;
    };

    // Default run length when neither --seconds nor --steps is given.
    const double defaultSeconds = 30.0;
    var stepInterval = TimeSpan.FromSeconds(1.0 / hz);
    var startWall = Stopwatch.StartNew();
    var stepCount = 0;
    var publishedFrames = 0;
    var logEveryNSteps = Math.Max(1, (int)Math.Round(hz)); // ~once per sim second

    Console.WriteLine($"Sim.Host.App: publishing headless from '{netPath}' (Ctrl-C to stop" +
        (stepsArg is { } capSteps ? $"; capped at {capSteps} steps"
            : secondsArg is { } capSeconds ? $"; capped at {capSeconds:F0}s"
            : $"; capped at {defaultSeconds:F0}s") + ").");

    while (!stopRequested)
    {
        if (stepsArg is { } maxSteps && stepCount >= maxSteps)
        {
            break;
        }

        if (stepsArg is null)
        {
            var capSecondsLoop = secondsArg ?? defaultSeconds;
            if (startWall.Elapsed.TotalSeconds >= capSecondsLoop)
            {
                break;
            }
        }

        var tickStart = Stopwatch.GetTimestamp();

        runner.Tick();
        var snap = runner.Snapshot;
        publisher.PublishStep(snap, sink);
        stepCount++;
        publishedFrames++;

        if (transport == "inmem")
        {
            bus!.Source.Pump();
        }

        if (stepCount % logEveryNSteps == 0)
        {
            Console.WriteLine(
                $"Sim.Host.App: t={snap.Time:F1}s vehicles={snap.Count} publishedFrames={publishedFrames}");

            if (transport == "inmem")
            {
                var src = bus!.Source;
                Console.WriteLine(
                    $"Sim.Host.App: consumer received: {src.History.Count} vehicles in history, " +
                    $"geometry complete={src.GeometryComplete}");
            }
        }

        // Pace to --hz: sleep off whatever time this iteration didn't use.
        var elapsed = Stopwatch.GetElapsedTime(tickStart);
        var toSleep = stepInterval - elapsed;
        if (toSleep > TimeSpan.Zero)
        {
            Thread.Sleep(toSleep);
        }
    }

    Console.WriteLine(
        $"Sim.Host.App: stopped after {stepCount} steps ({publishedFrames} published frames), " +
        $"final sim time={runner.Snapshot.Time:F1}s.");
}
finally
{
    if (sink is IDisposable disposableSink)
    {
        disposableSink.Dispose();
    }

    participant?.Dispose();
}

return 0;

// Accept either a scenario/sandbox directory (resolve its *.net.xml) or a direct net.xml path.
static string ResolveNetPath(string path)
{
    if (Directory.Exists(path))
    {
        return Directory.EnumerateFiles(path, "*.net.xml").FirstOrDefault()
            ?? throw new FileNotFoundException($"No *.net.xml found in directory '{path}'.");
    }

    return path;
}

// GameHostSample-style ambient traffic: inject `count` extra routable trips between random
// non-internal edges, ON TOP OF the scenario's own demand (already loaded by LoadScenario). Uses a
// FIXED-seed RNG (never an unseeded System.Random, per CLAUDE.md's determinism rule) so a --spawn run
// is reproducible across processes/threads.
static void SpawnAmbient(Engine engine, SimulationRunner runner, int count)
{
    var normalEdges = new List<int>();
    for (var h = 0; h < engine.EdgeCount; h++)
    {
        if (!engine.GetEdgeId(h).StartsWith(":", StringComparison.Ordinal))
        {
            normalEdges.Add(h);
        }
    }

    if (normalEdges.Count < 2)
    {
        Console.WriteLine("Sim.Host.App: --spawn requested but fewer than 2 spawnable edges; skipping.");
        return;
    }

    var vType = engine.DefaultVType;
    var rng = new Random(12345); // fixed seed -- see method doc.
    var spawned = 0;
    for (var i = 0; i < count; i++)
    {
        var from = normalEdges[rng.Next(normalEdges.Count)];
        var to = normalEdges[rng.Next(normalEdges.Count)];
        if (from == to)
        {
            continue;
        }

        var ok = runner.Invoke(e =>
        {
            try { e.SpawnVehicle(vType, from, to); return true; }
            catch (InvalidOperationException) { return false; } // no route between this pair
        });

        if (ok)
        {
            spawned++;
        }
    }

    Console.WriteLine($"Sim.Host.App: --spawn requested {count} ambient trips, spawned {spawned}.");
}
