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

    // P1: the runner is rebuilt in place on Restart() (SimHost's BuildSim pattern), so every
    // cross-thread read/rebuild of _engine/_runner is guarded by this lock exactly like SimHost.
    private readonly object _lock = new();
    private Engine _engine = null!;
    private SimulationRunner _runner = null!;
    private VTypeHandle _vType;
    private VTypeHandle _truckType;
    private Random _rng = new(12345);

    // P1: injected obstacle world-points, for the renderer's red-X marker -- mirrors SimHost's
    // _obsLock/_obstacles split (obstacle bookkeeping is independent of the engine rebuild lock).
    private readonly object _obsLock = new();
    private readonly List<(double X, double Y)> _obstacles = new();

    private Timer? _spawnTimer;
    private volatile bool _randomTraffic;

    public NetworkModel Network { get; }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public bool ScenarioMode => _scenarioMode;

    public SimulationSnapshot Snapshot
    {
        get
        {
            lock (_lock)
            {
                return _runner.Snapshot;
            }
        }
    }

    // Current state of the runtime random-traffic spawner, for the ImGui checkbox binding.
    public bool RandomTraffic => _randomTraffic;

    // Turn the runtime random-traffic spawner on/off (independent of mode) -- SimHost's SetRandomTraffic.
    public void SetRandomTraffic(bool on) => _randomTraffic = on;

    // Thread-safe snapshot of injected obstacle world-points, for the renderer's red-X marker.
    public IReadOnlyList<(double X, double Y)> ObstaclePoints
    {
        get
        {
            lock (_obsLock)
            {
                return _obstacles.ToArray();
            }
        }
    }

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

    // (Re)build the engine + runner from scratch, at t=0 -- SimHost's BuildSim pattern. Under _lock so
    // no frame/spawn/obstacle-inject races the swap; the old runner is disposed once swapped out.
    private void BuildSim()
    {
        lock (_lock)
        {
            var old = _runner;

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

            old?.Dispose();
        }

        lock (_obsLock)
        {
            _obstacles.Clear();
        }
    }

    // Rebuild the sim from t=0 (re-queues the scenario demand / empties the sandbox). Obstacles cleared.
    public void Restart() => BuildSim();

    // A world-point click (already WORLD coordinates) -> project to the nearest lane and inject a
    // full-lane obstacle; vehicles queue behind it. Ignored if the click is far from any lane. Ported
    // from Sim.LiveHost/SimHost.cs's InjectObstacleAtWorld.
    public void InjectObstacleAtWorld(double wx, double wy)
    {
        if (!TryProjectToLane(wx, wy, out var laneId, out var pos, out var sx, out var sy, out var dist)
            || dist > 15.0)
        {
            return;
        }

        SimulationRunner runner;
        lock (_lock)
        {
            runner = _runner;
        }

        try
        {
            runner.Invoke(e => e.AddObstacle(e.GetLane(laneId), frontPos: pos, length: 2.0));
        }
        catch
        {
            return; // runner disposed mid-restart -> drop this click
        }

        lock (_obsLock)
        {
            _obstacles.Add((sx, sy));
        }
    }

    public void ClearObstacles()
    {
        SimulationRunner runner;
        lock (_lock)
        {
            runner = _runner;
        }

        try
        {
            runner.Invoke<object?>(e => { e.ClearObstacles(); return null; });
        }
        catch
        {
            // runner disposed mid-restart -> the rebuild already cleared obstacles
        }

        lock (_obsLock)
        {
            _obstacles.Clear();
        }
    }

    // Nearest lane to a world point, plus the along-lane position and the projected point. Ported from
    // Sim.LiveHost/SimHost.cs's TryProjectToLane; Network is parsed once and never mutated across a
    // restart, so this needs no lock.
    private bool TryProjectToLane(double wx, double wy,
        out string laneId, out double pos, out double sx, out double sy, out double dist)
    {
        laneId = string.Empty;
        pos = 0.0;
        sx = 0.0;
        sy = 0.0;
        dist = double.PositiveInfinity;
        var bestD2 = double.PositiveInfinity;

        foreach (var lane in Network.LanesByHandle)
        {
            var shape = lane.Shape;
            if (shape.Count < 2)
            {
                continue;
            }

            var acc = 0.0;
            for (var i = 0; i < shape.Count - 1; i++)
            {
                var (px, py) = shape[i];
                var (qx, qy) = shape[i + 1];
                var dx = qx - px;
                var dy = qy - py;
                var segLen2 = dx * dx + dy * dy;
                var t = segLen2 > 0 ? Math.Clamp(((wx - px) * dx + (wy - py) * dy) / segLen2, 0.0, 1.0) : 0.0;
                var cx = px + dx * t;
                var cy = py + dy * t;
                var d2 = (wx - cx) * (wx - cx) + (wy - cy) * (wy - cy);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    laneId = lane.Id;
                    pos = acc + t * Math.Sqrt(segLen2);
                    sx = cx;
                    sy = cy;
                }

                acc += Math.Sqrt(segLen2);
            }
        }

        dist = Math.Sqrt(bestD2);
        return !double.IsPositiveInfinity(bestD2);
    }

    private void SpawnOne()
    {
        if (!_randomTraffic || _normalEdges.Length < 2)
        {
            return;
        }

        lock (_lock)
        {
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
    }

    public void Dispose()
    {
        _spawnTimer?.Dispose();
        lock (_lock)
        {
            _runner?.Dispose();
        }
    }
}
