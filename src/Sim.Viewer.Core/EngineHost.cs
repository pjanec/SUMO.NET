using Sim.Core;
using Sim.Ingest;

namespace Sim.Viewer.Core;

// docs/SUMOSHARP-NATIVE-VIEWER.md P0: the render-agnostic engine driver for the native viewer, lifted
// from Sim.LiveHost/SimHost.cs (SUMOSHARP-API.md §11) with the web/JSON/ImGui plumbing stripped out. Owns
// one Engine driven by a SimulationRunner, and the parsed NetworkModel the renderer needs for road
// geometry. Two modes, auto-selected from the input path exactly like SimHost:
//   * SCENARIO mode -- the input dir has a *.rou.xml AND a *.sumocfg (a committed scenario dir):
//     Engine.LoadScenario drives the scenario's OWN demand.
//   * SANDBOX mode -- a bare net.net.xml (no demand): Engine.LoadNetwork + a runtime random-traffic
//     spawner keeps the roads busy so a bare net still shows traffic.
public sealed class EngineHost : IDisposable
{
    private readonly string _netPath;
    private readonly string? _rouPath;
    private readonly string? _cfgPath;
    private readonly bool _scenarioMode;
    private readonly string[] _normalEdges;

    private Engine _engine = null!;
    private SimulationRunner _runner = null!;
    private VTypeHandle _vType;
    private VTypeHandle _truckType;
    private Random _rng = new(12345);

    private Timer? _spawnTimer;
    private volatile bool _randomTraffic;

    public NetworkModel Network { get; }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public bool ScenarioMode => _scenarioMode;

    public SimulationSnapshot Snapshot => _runner.Snapshot;

    public EngineHost(string netPath)
    {
        _netPath = netPath;
        Network = NetworkParser.Parse(netPath);
        _normalEdges = Network.EdgesById.Keys.Where(e => !e.StartsWith(':')).ToArray();

        // Scenario mode iff the net sits beside a rou.rou.xml AND a .sumocfg (a committed scenario dir) --
        // same detection SimHost uses.
        var dir = Path.GetDirectoryName(Path.GetFullPath(netPath));
        if (dir is not null)
        {
            _rouPath = Directory.EnumerateFiles(dir, "*.rou.xml").FirstOrDefault();
            _cfgPath = Directory.EnumerateFiles(dir, "*.sumocfg").FirstOrDefault();
        }

        _scenarioMode = _rouPath is not null && _cfgPath is not null;
        _randomTraffic = !_scenarioMode; // sandbox: random traffic is the traffic; scenario: off by default

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var lane in Network.LanesByHandle)
        {
            foreach (var (x, y) in lane.Shape)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;

        BuildSim();

        // Front-load a burst of spawn attempts in sandbox mode so the network already has traffic on it
        // from the very first published Snapshot, instead of relying solely on the periodic wall-clock
        // Timer below. SimHost's browser demo runs for minutes, so waiting out the timer's 500ms dueTime
        // is invisible there; a short-lived headless run (e.g. the P0 Xvfb screenshot recipe, a few
        // hundred milliseconds to a few seconds of real wall time) can otherwise race the Timer and finish
        // before it has fired even once, or fire only once or twice, leaving the roads empty or sparse.
        // SpawnOne() itself gates on `_randomTraffic` (false in scenario mode, so this is a no-op there)
        // and each call is independently queued via SimulationRunner.Post, applied at the next Tick
        // boundary exactly like the timer's own calls -- purely additive, same code path.
        if (_randomTraffic)
        {
            for (var i = 0; i < 60; i++)
            {
                SpawnOne();
            }
        }

        // Keep replenishing traffic thereafter (vehicles that reach their destination despawn).
        _spawnTimer = new Timer(_ => SpawnOne(), null, dueTime: 500, period: 900);
    }

    private void BuildSim()
    {
        var engine = new Engine();
        if (_scenarioMode)
        {
            engine.LoadScenario(_netPath, _rouPath!, _cfgPath!); // drives the scenario's OWN demand
        }
        else
        {
            engine.LoadNetwork(_netPath);
        }

        _vType = engine.DefaultVType;
        // A long vehicle so the random spawner can show swept-path off-tracking ("swing wide") on turns.
        _truckType = engine.DefineVType(new VTypeParams { VClass = "truck", Length = 12.0 });

        var runner = new SimulationRunner(engine);
        runner.EnableSnapshotPool(capacity: 3);
        // Local mode has no dead reckoning to smooth over sparse updates, so a higher rate than the web
        // demo's 2 Hz is fine -- the renderer draws the authoritative Snapshot every frame.
        runner.Start(targetHz: 10.0);

        _engine = engine;
        _runner = runner;
        _rng = new Random(12345);
    }

    private void SpawnOne()
    {
        if (!_randomTraffic || _normalEdges.Length < 2)
        {
            return;
        }

        if (_runner.Snapshot.Count > 80)
        {
            return;
        }

        var from = _normalEdges[_rng.Next(_normalEdges.Length)];
        var to = _normalEdges[_rng.Next(_normalEdges.Length)];
        if (from == to)
        {
            return;
        }

        var vt = _rng.Next(3) == 0 ? _truckType : _vType; // ~1/3 trucks, to show off-tracking on turns
        _runner.Post(e =>
        {
            try
            {
                e.SpawnVehicle(vt, from, to, departPos: 0.0, departSpeed: 0.0, departLane: 0);
            }
            catch
            {
                // No route between this random pair -> skip (the next tick tries a fresh pair).
            }
        });
    }

    public void Dispose()
    {
        _spawnTimer?.Dispose();
        _runner?.Dispose();
    }
}
