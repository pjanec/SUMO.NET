using Sim.Core;
using Sim.Ingest;

namespace Sim.Evac;

// PANIC-EVAC-PHASE5-TIER2-DESIGN.md §3b / TASKS T2.5: the Tier-2 CITY-SCALE demo builder. Mirrors
// EvacOrganicScenario's shape exactly (LoadScenario the committed net+demand, director in auto-track
// mode, a large central incident, boundary ExitEdges) but targets the committed 10k-class host
// `scenarios/_bench/city-15000` (design §3a, option A: 24x24 `netgenerate --grid` mesh, 1 lane,
// ~13-17k peak concurrent -- see provenance.txt) instead of the organic town. The whole point of this
// scenario is to stress the two O(m^2) crowd solvers at a low-thousands working-region population
// (design §1/§7), so BOTH Tier-2 spatial hashes are turned ON by default here (unlike
// EvacOrganicScenario, which defaults them off to keep its own goldens/tests untouched).
public static class EvacCityScenario
{
    private const string NetFile = "net.net.xml";
    private const string RouFile = "rou.rou.xml";
    private const string CfgFile = "config.sumocfg";

    // scenarios/_bench/city-15000: a 24x24 `netgenerate --grid --grid.length=500 -L 1` mesh (576
    // priority junctions, letters A..X = columns 0..23, numbers 0..23 = rows, 500 m spacing), extent
    // x,y in [0, 11500], measured peak ~17639 / steady ~13131 concurrent (provenance.txt). Junctions
    // are named "<col><row>" (e.g. "L11" at (5500,5500)); there is no dead-end junction type on this
    // net (netgenerate's boundary junctions are still "priority", just degree-2/3) -- "fringe" here
    // means an edge whose far end IS a perimeter junction (col A/X or row 0/23), the city-15000
    // analogue of EvacOrganicScenario's dead-end edges.
    //
    // Incident: the geometric centre of the mesh (5750,5750) -- exactly between junctions L11
    // (5500,5500) and M12 (6000,6000), so it isn't pinned to one junction's local geometry. Radius
    // 1800 / WorkingRadius 2200 are TUNED (not guessed) against a measured tracked-count: at the
    // network's steady-state density (~13131 concurrent over a 11500x11500 area), a WorkingRadius-2200
    // disc traps a working-region population in the low thousands once the incident has had time to
    // pull in the surrounding jam (see EvacCityDemoTests.CascadeStresses / Sim.EvacProfile --city for
    // the measured number). StartTime 150 lands after the city has ramped up (insertion period
    // ~0.058s/vehicle means several thousand vehicles are already inserted and in transit by t=150,
    // per provenance.txt's tuned_insertion_period_s) so real congestion already exists near the centre
    // when the incident fires.
    public static Incident DefaultIncident => new(X: 5750.0, Y: 5750.0, StartTime: 150.0, Radius: 1800.0);

    // A handful of true fringe edges (each leads to a real perimeter junction of the 24x24 mesh),
    // spread around the compass from the incident so a fleeing car has a reachable away-exit
    // regardless of which direction it flees: two opposite corners, then due south/west/east/north.
    // Best-effort per design §4 -- flee still works via the aggressive FleePreset even without these;
    // reroute just has more choices with them.
    public static readonly string[] ExitEdges =
    {
        "A1A0",     // -> A0 (0.00, 0.00)         -- SW corner
        "A22A23",   // -> A23 (0.00, 11500.00)    -- NW corner
        "W0X0",     // -> X0 (11500.00, 0.00)     -- SE corner
        "W23X23",   // -> X23 (11500.00, 11500.00) -- NE corner
        "L1L0",     // -> L0 (5500.00, 0.00)      -- due south
        "L22L23",   // -> L23 (5500.00, 11500.00) -- due north
        "B11A11",   // -> A11 (0.00, 5500.00)     -- due west
        "W12X12",   // -> X12 (11500.00, 6000.00) -- due east
    };

    public static EvacConfig DefaultConfig() => new()
    {
        AutoTrackInWorkingRegion = true,
        WorkingRadius = 2200.0,          // track the jam that backs up around the large central incident
        EnableLineOfSight = false,       // a security incident is HEARD, not just seen (mirrors organic)
        PedestriansPerCar = 4,           // multi-occupant cars -> a visibly massive foot crowd on abandonment
        // SafeRadius MUST exceed WorkingRadius (2200): a car can be blocked/converted anywhere in the
        // working region, up to WorkingRadius away from the incident, so a SafeRadius smaller than
        // WorkingRadius (an early cut at 300 -- borrowed from organic's much-smaller-scale ratio without
        // rescaling) let peds spawn ALREADY past it, skipping the visible "fleeing" (cyan) phase
        // entirely (observed: every disc in the --evac-city viz output was already "escaped" green).
        // 2500 keeps every spawn point (<= WorkingRadius) genuinely not-yet-safe, so the flee-to-escape
        // transition is visible on this much bigger mesh, mirroring organic's intent.
        SafeRadius = 2500.0,
        BlockedDwellSeconds = 1.5,       // boxed-in panicked drivers give up and bail sooner
        ExitEdges = ExitEdges,
        // PANIC-EVAC-PHASE5-TIER2-DESIGN.md §2/§5(3): this demo's whole point is to prove the Tier-2
        // spatial hashes make the low-thousands working-region population tractable -- both are ON by
        // default here (unlike EvacOrganicScenario, whose default stays off to keep its own goldens
        // byte-identical). Proven bit-identical to brute-force (EvacCrowdSpatialHashTests et al.).
        UseCrowdSpatialHash = true,
    };

    // Build a fresh engine + director on the committed 10k-class city net: `LoadScenario` pulls net +
    // demand + config (nothing spawned manually here), and the director auto-attaches to whatever
    // drives into the working region as the loaded demand inserts under its own Tick() loop.
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
        var scenarioDir = Path.Combine(repoRoot, "scenarios", "_bench", "city-15000");
        return Build(
            Path.Combine(scenarioDir, NetFile),
            Path.Combine(scenarioDir, RouFile),
            Path.Combine(scenarioDir, CfgFile),
            incident, config);
    }
}
