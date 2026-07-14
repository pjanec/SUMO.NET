using Sim.Core;
using Sim.Ingest;

namespace Sim.Evac;

// PANIC-EVAC-PHASE5-DESIGN.md §4 (T2.1): the Tier-1 demo -- a realistic organic town, loaded with its
// own committed demand (`LoadScenario`, not the grid demos' manual spawn-and-`Track`), an incident at a
// busy interior junction, and the director in AUTO-TRACK mode (design §3): the evac layer never needs
// to see all ~400 concurrent vehicles, only the ones that drive into the working region around the
// incident. `EvacGridScenario`/`EvacTlsScenario` and their tests are untouched -- this is a new,
// additive sibling built the same way (a fixture, so demo/tests only change here).
public static class EvacOrganicScenario
{
    private const string NetFile = "net.net.xml";
    private const string RouFile = "rou.rou.xml";
    private const string CfgFile = "config.sumocfg";

    // scenarios/_bench/city-organic-L2: 274 junctions, 1186 edges, 2 lanes, 618 trips, ~406 peak
    // concurrent (see NOTES.md). The net's real bounding box is x in [0, 2077.51], y in [0, 2426.81]
    // (computed from the committed net -- not the [850,1300]x[1500,1650] sub-range some docs assume),
    // centroid ~ (1038.8, 1213.4).
    //
    // Incident: junction "415" (traffic_light, x=1083.17, y=1229.14) -- the real, non-internal junction
    // closest to the net's centroid, with 8 incoming lanes (a 4-way, 2-lanes-per-approach signalized
    // intersection: the busiest kind of interior junction in this net), so it carries real
    // boundary-to-boundary through-traffic. StartTime 90 lands after ~90s/~18% of the uniformly-spread
    // 618 departures (see NOTES.md/rou.rou.xml) so real congestion already exists near the junction when
    // the incident fires.
    //
    // Radius 400 is deliberately LARGE (a major incident whose panic blankets the town centre, not a
    // single-intersection scare) -- the demo's whole point is to SHOW a large-scale evac: mass panic ->
    // gridlock on the few town-edge exits -> mass abandonment -> a massive foot exodus (measured: ~174
    // panic, ~151 cars abandoned, ~600 pedestrians). The town is sparse (~370 vehicles over 274
    // junctions), so a small radius catches only a handful of cars (radius 70 gave just 5 pedestrians);
    // the large radius is what turns the sparse mesh into a dramatic exodus. Locality still holds: the
    // town's far edges/corners are all > WorkingRadius from the incident and stay pure parity traffic
    // (EvacOrganicDemoTests.EvacStaysLocal: ~159 of ~371 vehicles never tracked), which is the property
    // that keeps the evac layer's cost bounded regardless of city size (design §1).
    public static Incident DefaultIncident => new(X: 1083.17, Y: 1229.14, StartTime: 90.0, Radius: 400.0);

    // A handful of true boundary edges (each leads to a real "dead_end" junction, i.e. the edge of the
    // generated mesh), picked to spread around the compass from the incident so a fleeing car has a
    // reachable away-exit regardless of which direction it flees: south, west, east, north, and two far
    // corners. Best-effort per design §4 -- flee still works via the aggressive FleePreset even without
    // these; reroute just has more choices with them.
    public static readonly string[] ExitEdges =
    {
        "2406",   // -> dead-end (113.64, 243.66)   -- SW corner
        "1519",   // -> dead-end (297.39, 2357.61)  -- NW corner
        "2264",   // -> dead-end (1046.32, 0.0)     -- due south
        "2451",   // -> dead-end (0.0, 1525.69)     -- due west
        "1484",   // -> dead-end (2077.51, 1067.57) -- due east
        "293",    // -> dead-end (972.27, 2122.49)  -- due north
    };

    public static EvacConfig DefaultConfig() => new()
    {
        AutoTrackInWorkingRegion = true,
        WorkingRadius = 550.0,           // track the whole jam that backs up around the larger incident
        EnableLineOfSight = false,       // a security incident is HEARD, not just seen: everyone in the
                                         // blast radius panics regardless of cars occluding the view
        PedestriansPerCar = 4,           // multi-occupant cars -> a visibly massive foot crowd on abandonment
        SafeRadius = 200.0,              // peds must jog farther out -> a wider, more legible exodus ring
        BlockedDwellSeconds = 1.5,       // boxed-in panicked drivers give up and bail sooner
        ExitEdges = ExitEdges,
    };

    // Build a fresh engine + director on the committed organic town: `LoadScenario` pulls net + demand
    // + config (vTypes/routes/trips/TLS all come from the scenario files -- nothing spawned manually
    // here, unlike EvacGridScenario.Build), and the director auto-attaches to whatever drives into the
    // working region as the loaded demand inserts under its own Tick() loop (design §8).
    public static (Engine Engine, EvacDirector Director) Build(
        string netPath, string rouPath, string cfgPath, Incident? incident = null, EvacConfig? config = null)
    {
        var engine = new Engine();
        engine.LoadScenario(netPath, rouPath, cfgPath);

        var net = NetworkParser.Parse(netPath);
        var scenarioConfig = ScenarioConfigParser.Parse(cfgPath);
        var stepLength = scenarioConfig.StepLength > 0 ? scenarioConfig.StepLength : 1.0;

        var director = new EvacDirector(
            engine, net, incident ?? DefaultIncident, config ?? DefaultConfig(), stepLength);

        return (engine, director);
    }

    // Convenience overload defaulting to the committed scenario paths under `repoRoot`.
    public static (Engine Engine, EvacDirector Director) Build(
        string repoRoot, Incident? incident = null, EvacConfig? config = null)
    {
        var scenarioDir = Path.Combine(repoRoot, "scenarios", "_bench", "city-organic-L2");
        return Build(
            Path.Combine(scenarioDir, NetFile),
            Path.Combine(scenarioDir, RouFile),
            Path.Combine(scenarioDir, CfgFile),
            incident, config);
    }
}
