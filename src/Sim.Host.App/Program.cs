using System.Diagnostics;
using System.Globalization;
using CycloneDDS.Runtime;
using Sim.Core;
using Sim.Host;
using Sim.Ingest;
using Sim.LiveCity;
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
//
// docs/LIVE-CITY-VIEWERS-DESIGN.md §7, -TASKS.md Stage E (E3):
//   dotnet run --project src/Sim.Host.App -- --live-city [--transport dds|inmem]
//       [--hz <n>] [--seconds <n> | --steps <n>]
//
// `--live-city` runs Sim.LiveCity.LiveCitySim (the SAME coupled cars+peds+crossing-yield host every
// other live-city viewer consumes) instead of loading a --scenario, and publishes BOTH the vehicle
// topic set and the ped topic set from this ONE process over ONE net via LiveCitySim's own additive
// record/DDS tee params (`recordVehSink`/`recordPedSink`) -- the crossing-yield gate stays server-side
// inside LiveCitySim; only the resulting poses cross the wire. Mutually exclusive with --scenario/--spawn.
string? scenarioArg = null;
var transport = "dds";
var hz = 10.0;
double? secondsArg = null;
int? stepsArg = null;
var spawn = 0;
var liveCity = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--scenario":
            scenarioArg = args[++i];
            break;
        case "--live-city":
            liveCity = true;
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

if (transport is not ("dds" or "inmem"))
{
    Console.Error.WriteLine($"Sim.Host.App: unknown --transport '{transport}' (expected dds|inmem).");
    return 1;
}

if (liveCity)
{
    if (scenarioArg is not null || spawn > 0)
    {
        Console.Error.WriteLine("Sim.Host.App: --live-city is mutually exclusive with --scenario/--spawn.");
        return 1;
    }

    return RunLiveCity(transport, hz, secondsArg, stepsArg);
}

if (scenarioArg is null)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project src/Sim.Host.App -- --scenario <dir|net.xml> " +
        "[--transport dds|inmem] [--hz <n>] [--seconds <n> | --steps <n>] [--spawn <n>]\n" +
        "   or: dotnet run --project src/Sim.Host.App -- --live-city " +
        "[--transport dds|inmem] [--hz <n>] [--seconds <n> | --steps <n>]");
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

// docs/LIVE-CITY-VIEWERS-DESIGN.md §7, -TASKS.md Stage E (E3) -- the combined cars+peds `--live-city`
// producer. Builds ONE Sim.LiveCity.LiveCitySim (the shared coupled-sim host every other live-city
// viewer/producer consumes) over a vehicle sink AND a ped sink for the chosen `--transport`, then steps
// it on a WALL-CLOCK accumulator (Fix 5 -- sim-time tracks real elapsed wall time 1:1 regardless of
// `--hz`; `--hz` only paces how often the loop polls/wakes, decoupled from the step-and-publish rate),
// respecting `--seconds`/`--steps` exactly like the --scenario loop above. LiveCitySim's own internal
// in-memory bus is what the coupled sim uses to feed itself; the sinks passed
// here are the ADDITIVE `recordVehSink`/`recordPedSink` tee params (docs/LIVE-CITY-VIEWERS-DESIGN.md §2.2,
// this task's own ped-tee addition) -- i.e. this producer captures BOTH streams without LiveCitySim ever
// knowing about DDS. `--transport inmem` wires the SAME tee onto a same-process InMemoryReplicationBus/
// InMemoryPedReplicationBus pair and self-consumes (Pump()s both buses each step) so the whole plumbing
// is exercised with no native DDS dependency -- the non-flaky gate this task's own success condition asks
// for ("assert/log that BOTH vehicle frames AND ped crowd frames are produced/consumed in-process").
static int RunLiveCity(string transport, double hz, double? secondsArg, int? stepsArg)
{
    var repoRoot = FindRepoRoot();
    var cfg = LiveCityConfig.ForRepoRoot(repoRoot);

    Console.WriteLine(
        $"Sim.Host.App: --live-city dataset='{cfg.DatasetDir}' transport={transport} hz={hz:F1} " +
        $"carCap={cfg.CarTargetConcurrent} yield={cfg.YieldEnabled}");

    DdsParticipant? participant = null;
    InMemoryReplicationBus? vehBus = null;
    InMemoryPedReplicationBus? pedBus = null;
    IReplicationSink vehSink;
    IPedReplicationSink pedSink;

    if (transport == "dds")
    {
        participant = new DdsParticipant();
        vehSink = new DdsReplicationSink(participant);
        pedSink = new DdsPedReplicationSink(participant);
    }
    else
    {
        vehBus = new InMemoryReplicationBus();
        pedBus = new InMemoryPedReplicationBus();
        vehSink = vehBus.Sink;
        pedSink = pedBus.Sink;
    }

    using var sim = new LiveCitySim(cfg, vehSink, pedSink);

    try
    {
        if (transport == "dds")
        {
            // DDS discovery is async -- give any already-running readers time to match before the step
            // loop starts publishing (mirrors the --scenario path's own settle sleep, LoopbackSelfTest's
            // proven pattern).
            Thread.Sleep(500);
        }

        var stopRequested = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // let the loop below exit cleanly (dispose the sinks/participant) instead of
                              // the process dying mid-write.
            stopRequested = true;
        };

        // Default run length when neither --seconds nor --steps is given. Shorter than the --scenario
        // path's 30s default: the coupled sim ramps to peak density within a few seconds and this mode is
        // primarily exercised as a bounded self-test/demo run, not a long-lived service.
        const double defaultSeconds = 20.0;
        var pollInterval = TimeSpan.FromSeconds(1.0 / hz);
        var startWall = Stopwatch.StartNew();
        var stepCount = 0;
        // ~once per sim second -- keyed off cfg.Dt (the sim's own step length) now that --hz no longer
        // dictates the step rate (see the wall-accumulator remark below).
        var logEveryNSteps = Math.Max(1, (int)Math.Round(1.0 / cfg.Dt));

        Console.WriteLine(
            $"Sim.Host.App: --live-city publishing headless (Ctrl-C to stop" +
            (stepsArg is { } capSteps ? $"; capped at {capSteps} steps"
                : secondsArg is { } capSeconds ? $"; capped at {capSeconds:F0}s"
                : $"; capped at {defaultSeconds:F0}s") + ").");

        // Fix 5 -- decouple the sim step from the publish/poll cadence. LiveCitySim.Step() BOTH advances
        // the sim by cfg.Dt sim-seconds AND publishes that step onto the vehSink/pedSink tee (its own
        // remarks) -- there is no separate "publish without stepping" call. The OLD loop called
        // sim.Step() once per --hz poll tick, so it advanced `cfg.Dt * hz` sim-seconds per wall-second:
        // at the documented `--hz 10` example that is 0.5 * 10 = 5 sim-seconds/wall-second (5x real-time,
        // and the DDS subscriber saw a snap-on-correction sawtooth from the DrClock playout racing ahead
        // of real time). Instead, accumulate REAL wall-clock elapsed time (via the Stopwatch below) and
        // only call sim.Step() when at least one cfg.Dt of wall time is owed -- sim-time then tracks
        // wall-time 1:1 regardless of --hz. `--hz` now governs only how often this outer loop wakes to
        // check the accumulator / respond to Ctrl-C / pace CPU use -- a higher --hz polls more often but
        // still only steps (and publishes) at the ~1/cfg.Dt cadence the accumulator owes, which is why
        // both `--hz 10` and `--hz 2` converge on the same ~1.0 sim-time/wall-time ratio.
        var simAccumSeconds = 0.0;
        var lastWallSeconds = 0.0;

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

            var nowWallSeconds = startWall.Elapsed.TotalSeconds;
            simAccumSeconds += nowWallSeconds - lastWallSeconds;
            lastWallSeconds = nowWallSeconds;

            while (simAccumSeconds >= cfg.Dt && (stepsArg is null || stepCount < stepsArg))
            {
                sim.Step();
                stepCount++;
                simAccumSeconds -= cfg.Dt;

                if (transport == "inmem")
                {
                    vehBus!.Source.Pump();
                    pedBus!.Source.Pump();
                }

                if (stepCount % logEveryNSteps == 0)
                {
                    var ratio = nowWallSeconds > 0.0 ? sim.Time / nowWallSeconds : 0.0;
                    Console.WriteLine(
                        $"Sim.Host.App: --live-city t={sim.Time:F1}s step={stepCount} peakCars={sim.PeakCars} " +
                        $"peakPeds={sim.PeakPeds} peakOccupiedCrossings={sim.PeakOccupiedCrossings} " +
                        $"simTimeOverWallRatio={ratio:F2}" +
                        (transport == "inmem"
                            ? $" | consumer: vehiclesInHistory={vehBus!.Source.History.Count} " +
                              $"latestPedCrowdFrame={pedBus!.Source.LatestCrowdFrame.Count} " +
                              $"pedLifecyclesSeen={pedBus.Source.Lifecycles.Count}"
                            : string.Empty));
                }
            }

            // Pace the outer poll loop to --hz: sleep off whatever time this iteration didn't use. This
            // no longer paces the STEP rate (the wall accumulator above does that) -- only how often the
            // loop wakes to check it.
            var elapsed = Stopwatch.GetElapsedTime(tickStart);
            var toSleep = pollInterval - elapsed;
            if (toSleep > TimeSpan.Zero)
            {
                Thread.Sleep(toSleep);
            }
        }

        Console.WriteLine(
            $"Sim.Host.App: --live-city stopped after {stepCount} steps, final sim time={sim.Time:F1}s, " +
            $"peakCars={sim.PeakCars}, peakPeds={sim.PeakPeds}, peakOccupiedCrossings={sim.PeakOccupiedCrossings}, " +
            $"carYieldObservations={sim.CarYieldObservations}.");

        if (transport == "inmem")
        {
            // The non-flaky gate this task's success condition asks for: both streams must have been
            // produced by LiveCitySim's tee AND actually consumed by the same-process bus source --
            // non-vacuous (nonzero vehicles AND nonzero peds), never just "no exception was thrown".
            var vehiclesSeen = vehBus!.Source.History.Count;
            var pedFrameCount = pedBus!.Source.LatestCrowdFrame.Count;
            var pedLifecyclesSeen = pedBus.Source.Lifecycles.Count;
            Console.WriteLine(
                $"Sim.Host.App: --live-city inmem self-test: vehiclesInConsumerHistory={vehiclesSeen}, " +
                $"latestPedCrowdFrameCount={pedFrameCount}, pedLifecyclesSeen={pedLifecyclesSeen}.");

            if (vehiclesSeen == 0 || (pedFrameCount == 0 && pedLifecyclesSeen == 0))
            {
                Console.Error.WriteLine(
                    "Sim.Host.App: --live-city inmem self-test FAILED -- expected nonzero vehicles AND " +
                    "nonzero peds to have crossed the tee onto the consumer bus.");
                return 3;
            }
        }
    }
    finally
    {
        vehSink.Dispose();
        pedSink.Dispose();
        participant?.Dispose();
    }

    return 0;
}

// Walk up from the running assembly's directory to the directory containing Traffic.sln -- the same
// pattern DemoCatalog.RepoRoot() (src/Sim.Viewer/DemoCatalog.cs) uses, copied here so this headless host
// (which never references Sim.Viewer) can resolve it independently. CLAUDE.md prime directive 1: never
// hardcode an absolute VM path -- resolve the repo root, don't assume it.
static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Traffic.sln")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName
        ?? throw new InvalidOperationException("Could not locate repo root (Traffic.sln not found above assembly).");
}
