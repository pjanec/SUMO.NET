using Sim.Core.Orca;

namespace Sim.Core.Bridge;

// The cross-regime bridge coordinator (docs/LANELESS-DIRECTION.md, "the cross-regime bridge"):
// steps a lane Engine (laneless-RVO vehicles) and an open-space OrcaCrowd in LOCKSTEP so the two
// populations mutually avoid, exchanging only FROZEN world-space snapshots each step (the same
// plan/execute discipline both engines use internally). Neither regime's solver changes; each simply
// gains the other's movers as footprint neighbours:
//   - Direction A (crowd avoids vehicles): each step the coupling reads every vehicle's world state
//     (via the export-observer seam), turns each vehicle into a short chain of world discs covering
//     its footprint, and hands them to the crowd (OrcaCrowd.SetExternalObstacles) -- so a pedestrian's
//     ORCA yields for a car.
//   - Direction B (vehicles avoid crowd): the engine's CrowdSource is set to the crowd, so each
//     laneless-RVO vehicle projects nearby crowd agents onto its lane and forbids their lateral band
//     (Engine.ComputeRvoLateral) -- so a car swerves for a pedestrian.
// Both sides yield fully (one-sided from each perspective): a conservative mutual double-yield that is
// always collision-safe. The engine advances one step at a time via Run(1) (its timeline carries over
// across calls), and the crowd steps with the SAME dt, giving a symmetric one-step-latency coupling.
//
// Deterministic and gated: with no coupling constructed, Engine.CrowdSource stays null and the crowd
// is standalone -- both regimes are byte-identical to their un-bridged behaviour (the determinism hash
// is unaffected; nothing here is reachable from a committed golden).
public sealed class CrossRegimeCoupling : ISimExportObserver
{
    private readonly Engine _engine;
    private readonly OrcaCrowd _crowd;
    private readonly double _dt;
    private readonly Func<string, (double HalfWidth, double Length)> _vehicleGeometry;

    // Captured per-frame vehicle snapshots (filled by the export observer during Run(1)'s emit).
    private readonly List<VehicleExportSnapshot> _frame = new();
    private WorldDisc[] _discBuf = new WorldDisc[32];

    // Discs per vehicle covering its footprint spine. Capped so a dense scene cannot explode the
    // crowd's neighbour lists; a car (5 m / 0.9 m half-width) uses ~6.
    private const int MaxDiscsPerVehicle = 6;

    public CrossRegimeCoupling(
        Engine engine,
        OrcaCrowd crowd,
        double dt,
        Func<string, (double HalfWidth, double Length)> vehicleGeometry)
    {
        _engine = engine;
        _crowd = crowd;
        _dt = dt;
        _vehicleGeometry = vehicleGeometry;
        _engine.CrowdSource = _crowd;      // Direction B
        _engine.AddExportObserver(this);   // Direction A read seam
    }

    // The vehicle world states captured at the most recent step's emit (front-centre X/Y, naviDeg
    // Angle, Speed, PosLat, ...). Exposed so a driver/test can inspect the lane side each step without
    // re-parsing a TrajectorySet.
    public IReadOnlyList<VehicleExportSnapshot> LastFrame => _frame;

    void ISimExportObserver.OnFrameBegin(double time) => _frame.Clear();

    void ISimExportObserver.OnVehicleExported(in VehicleExportSnapshot snapshot) => _frame.Add(snapshot);

    // Advance BOTH regimes one lockstep. Order: run the engine one step (its plan reads the crowd's
    // current committed state -> Direction B; its emit fills _frame), then feed the fresh vehicle discs
    // to the crowd and step it (-> Direction A). Symmetric one-step latency.
    public void Step()
    {
        _engine.Run(1);
        var n = BuildVehicleDiscs();
        _crowd.SetExternalObstacles(_discBuf.AsSpan(0, n));
        _crowd.Step(_dt);
    }

    public void Advance(int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            Step();
        }
    }

    // Turn each captured vehicle into a chain of world discs covering its footprint, dead-reckoned
    // with the vehicle's world velocity. Returns the count written into _discBuf.
    private int BuildVehicleDiscs()
    {
        var needed = _frame.Count * MaxDiscsPerVehicle;
        if (_discBuf.Length < needed)
        {
            _discBuf = new WorldDisc[Math.Max(needed, _discBuf.Length * 2)];
        }

        var n = 0;
        foreach (var v in _frame)
        {
            var (halfWidth, length) = _vehicleGeometry(v.VehicleType);

            // naviDegree (0 = north/+Y, clockwise) -> world heading unit vector (sin, cos), matching
            // LaneGeometry.PositionAtOffset's angle convention. Velocity = Speed * heading.
            var navi = v.Angle * Math.PI / 180.0;
            var hx = Math.Sin(navi);
            var hy = Math.Cos(navi);
            var vx = v.Speed * hx;
            var vy = v.Speed * hy;

            // Discs from the front (v.X, v.Y) backward along -heading, spaced to cover [front, front-length].
            var count = Math.Clamp((int)Math.Ceiling(length / halfWidth), 1, MaxDiscsPerVehicle);
            var spacing = count > 1 ? length / (count - 1) : 0.0;
            for (var d = 0; d < count; d++)
            {
                var back = d * spacing;
                _discBuf[n++] = new WorldDisc(v.X - hx * back, v.Y - hy * back, vx, vy, halfWidth);
            }
        }

        return n;
    }
}
