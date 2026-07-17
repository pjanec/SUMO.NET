using System.Reflection;
using Sim.Core;
using Xunit;

namespace Sim.ParityTests;

// P1E-6 (HIGH-DENSITY-P1E-DESIGN.md §11) -- pre-insertion rerouting. SUMO's device.rerouting
// reroutes each equipped vehicle ONCE at/around departure (MSDevice_Routing::preInsertionReroute),
// not only periodically -- Engine.PreInsertionReroute, wired into InsertDepartingVehicles, ports
// this. These are BEHAVIOURAL/FUNCTIONAL tests against the engine's own TrajectorySet and (via
// reflection, matching RungHDp1e4RerouteDeviceTests' own idiom) a couple of internal invariants --
// there is no scenario-level golden here (that is owned separately, per CLAUDE.md).
//
// Fixture: reuses the COMMITTED scenarios/_fixtures/routing-diamond/net.net.xml (the same diamond
// SA -> {AB,AC} -> {BD,CD} -> DE net RungHDp1e4RerouteDeviceTests itself uses) -- the top path
// (SA AB BD DE, 1010m) is shorter/preferred over the bottom detour (SA AC CD DE, 1268m) at
// free-flow speed. A single stopped "blocker" (depart=0, stops indefinitely just past the start
// of AB) is enough on its own to congest AB: the blocker itself is the first (and only) vehicle
// ever on AB, so AB's edge-speed latches "delayed" as soon as the blocker enters (~t=36, the time
// to cross SA) and its smoothed speed collapses toward 0 within a handful of adaptation intervals
// -- comfortably below the threshold needed for AB's smoothed travel-time effort to exceed the
// AC/CD detour's fixed free-flow effort. A single "probe" vehicle, equipped for device.rerouting,
// departs LATE (well after that convergence) so its OWN pre-insertion reroute -- not the periodic
// pass, which is given a period far longer than the run so it can never fire -- is what decides
// its route. (Deliberately no extra "follower" traffic: a queue of many vehicles on the shared SA
// prefix would itself block the probe from moving at all, confounding the very route choice this
// test isolates.)
public class RungHDp1e6PreInsertionRerouteTests
{
    private static readonly string NetPath = Path.Combine(
        RepoRoot(), "scenarios", "_fixtures", "routing-diamond", "net.net.xml");

    private static Engine LoadEngine(string rouXml, string cfgXml)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hd-p1e6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var rouPath = Path.Combine(dir, "rou.rou.xml");
        var cfgPath = Path.Combine(dir, "config.sumocfg");
        File.WriteAllText(rouPath, rouXml);
        File.WriteAllText(cfgPath, cfgXml);

        var engine = new Engine();
        engine.LoadScenario(NetPath, rouPath, cfgPath);
        return engine;
    }

    // A stopped "blocker" near the start of AB (same idiom as RungHDp1e4RerouteDeviceTests'
    // CongestionRouXml, minus the extra "follower" traffic -- see the class header) plus one
    // "probe" vehicle on the identical route departing at `probeDepart`.
    private static string BlockerAndProbeRouXml(double probeDepart)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<routes>");
        sb.AppendLine("""    <vType id="car" vClass="passenger" sigma="0"/>""");
        sb.AppendLine("""    <route id="top" edges="SA AB BD DE"/>""");
        sb.AppendLine(
            """    <vehicle id="blocker" type="car" route="top" depart="0" departPos="0" departSpeed="0" departLane="0">""");
        sb.AppendLine("""        <stop lane="AB_0" startPos="50" endPos="55" duration="100000"/>""");
        sb.AppendLine("    </vehicle>");
        sb.AppendLine(
            $"""    <vehicle id="probe" type="car" route="top" depart="{probeDepart.ToString(System.Globalization.CultureInfo.InvariantCulture)}" departPos="0" departSpeed="0" departLane="0"/>""");
        sb.AppendLine("</routes>");
        return sb.ToString();
    }

    private static string CfgXml(int runEnd, double period, int adaptationSteps, double adaptationInterval)
    {
        var reroutingSection = period > 0.0
            ? $"""
                    <device.rerouting.probability value="1.0"/>
                    <device.rerouting.period value="{period.ToString(System.Globalization.CultureInfo.InvariantCulture)}"/>
                    <device.rerouting.adaptation-steps value="{adaptationSteps}"/>
                    <device.rerouting.adaptation-interval value="{adaptationInterval.ToString(System.Globalization.CultureInfo.InvariantCulture)}"/>
                    <routing-algorithm value="dijkstra"/>
                    <device.rerouting.jitter value="false"/>
            """
            : string.Empty;

        return $"""
            <configuration>
                <time><begin value="0"/><end value="{runEnd}"/><step-length value="1"/></time>
                <processing>
                    <time-to-teleport value="-1"/>
                    <default.action-step-length value="1"/>
                    <default.speeddev value="0"/>
            {reroutingSection}        </processing>
                <random_number><seed value="42"/></random_number>
            </configuration>
            """;
    }

    private const double ProbeDepart = 60.0;
    private const int RunEnd = 150;

    // ----- Test 1: pre-insertion reroute wins over an initial slower route, from the vehicle's
    // very first step on the network -- period is set far longer than the run so the PERIODIC
    // pass can never fire (NextRerouteTime = probeDepart + period sits way past RunEnd); only the
    // pre-insertion pass (fired once, at InsertDepartingVehicles, before the probe is placed) can
    // possibly divert it. -----

    [Fact]
    public void PreInsertionReroute_ProbeTakesTheAlternate_FromItsFirstStep_PeriodicCannotHaveFired()
    {
        const double hugePeriod = 100_000.0;

        var engine = LoadEngine(
            BlockerAndProbeRouXml(ProbeDepart),
            CfgXml(RunEnd, period: hugePeriod, adaptationSteps: 6, adaptationInterval: 1.0));

        var trajectory = engine.Run(RunEnd);

        var probePoints = trajectory.AllPoints.Where(p => p.VehicleId == "probe").OrderBy(p => p.Time).ToList();
        Assert.NotEmpty(probePoints);

        // The probe never rides the congested top-path edges (AB/BD) -- it diverted onto the
        // detour (AC/CD) before it was ever placed, i.e. pre-insertion, not periodic (which never
        // fires within this run given hugePeriod). "From its first step": the very first point
        // after the shared SA prefix is already on the detour.
        Assert.DoesNotContain(probePoints, p => p.Lane is "AB_0" or "BD_0");
        Assert.Contains(probePoints, p => p.Lane == "AC_0");
        Assert.Contains(probePoints, p => p.Lane == "CD_0");

        // Internal-state confirmation (mirrors RungHDp1e4RerouteDeviceTests' own reflection idiom):
        // the probe's effective route was actually re-registered to the detour edge list, and its
        // one-shot pre-insertion attempt is marked spent.
        var probeRuntime = FindVehicleRuntime(engine, "probe");
        Assert.True((bool)GetField(probeRuntime, "PreInsertionRerouteDone"));
        Assert.True((bool)GetField(probeRuntime, "RerouteEquipped"));

        var effectiveRouteId = EffectiveRouteId(engine, probeRuntime);
        var edges = RouteEdges(engine, effectiveRouteId);
        Assert.Equal(new[] { "SA", "AC", "CD", "DE" }, edges);

        // The periodic schedule itself is untouched by the pre-insertion pass (§11: SUMO does
        // both; pre-insertion never touches NextRerouteTime).
        var nextRerouteTime = (double)GetField(probeRuntime, "NextRerouteTime");
        Assert.Equal(ProbeDepart + hugePeriod, nextRerouteTime, precision: 6);
    }

    // ----- Test 2: inert-when-off. ReroutePeriod<=0 disables BOTH pre-insertion and periodic
    // rerouting -- the probe strictly follows the initial (slower) route (and, since the blocker
    // never moves, ends up queued behind it on AB rather than ever reaching the detour). -----

    [Fact]
    public void ReroutePeriodZero_NoPreInsertion_ProbeFollowsItsInitialRoute()
    {
        var engine = LoadEngine(
            BlockerAndProbeRouXml(ProbeDepart),
            CfgXml(RunEnd, period: 0.0, adaptationSteps: 6, adaptationInterval: 1.0));

        var trajectory = engine.Run(RunEnd);

        var probePoints = trajectory.AllPoints.Where(p => p.VehicleId == "probe").OrderBy(p => p.Time).ToList();
        Assert.NotEmpty(probePoints);

        // No device.rerouting at all -- the probe never takes the detour, only the original
        // top-path lanes (or internal junction lanes).
        Assert.DoesNotContain(probePoints, p => p.Lane is "AC_0" or "CD_0");
        Assert.Contains(probePoints, p => p.Lane == "AB_0");
        Assert.All(probePoints, p => Assert.True(
            p.Lane is "SA_0" or "AB_0" or "BD_0" or "DE_0" || p.Lane.StartsWith(':'),
            $"unexpected lane '{p.Lane}' with rerouting disabled"));

        var probeRuntime = FindVehicleRuntime(engine, "probe");
        Assert.False((bool)GetField(probeRuntime, "PreInsertionRerouteDone"));
        Assert.False((bool)GetField(probeRuntime, "RerouteEquipped"));
    }

    // ----- Reflection helpers (mirrors RungHDp1e4RerouteDeviceTests' own idiom: VehicleRuntime/
    // Engine's side storage is deliberately not part of the public SDK surface). -----

    private static object GetEngineField(Engine engine, string name)
    {
        var field = typeof(Engine).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Engine field '{name}' not found (reflection).");
        return field.GetValue(engine)!;
    }

    // VehicleRuntime.PreInsertionRerouteDone/RerouteEquipped/NextRerouteTime/EntityIndex are plain
    // public FIELDS.
    private static object GetField(object obj, string name)
    {
        var field = obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{name}' not found on {obj.GetType()} (reflection).");
        return field.GetValue(obj)!;
    }

    // VehicleRuntime.Def and VehicleDef's positional members (Id, RouteId, ...) are auto-PROPERTIES
    // (an init-only property and record positional properties, respectively), not fields.
    private static object GetProp(object obj, string name)
    {
        var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{name}' not found on {obj.GetType()} (reflection).");
        return prop.GetValue(obj)!;
    }

    private static object FindVehicleRuntime(Engine engine, string vehicleId)
    {
        var vehicles = (System.Collections.IEnumerable)GetEngineField(engine, "_vehicles");
        foreach (var v in vehicles)
        {
            var def = GetProp(v, "Def");
            var id = (string)GetProp(def, "Id");
            if (id == vehicleId)
            {
                return v;
            }
        }

        throw new InvalidOperationException($"Vehicle '{vehicleId}' not found in _vehicles (reflection).");
    }

    private static string EffectiveRouteId(Engine engine, object vehicleRuntime)
    {
        var map = (System.Collections.IDictionary)GetEngineField(engine, "_effectiveRouteIdByEntity");
        var entityIndex = (int)GetField(vehicleRuntime, "EntityIndex");
        if (map.Contains(entityIndex))
        {
            return (string)map[entityIndex]!;
        }

        var def = GetProp(vehicleRuntime, "Def");
        return (string)GetProp(def, "RouteId");
    }

    private static List<string> RouteEdges(Engine engine, string routeId)
    {
        var routesById = (System.Collections.IDictionary)GetEngineField(engine, "_routesById");
        var route = routesById[routeId] ?? throw new InvalidOperationException($"Route '{routeId}' not found (reflection).");
        var edges = (System.Collections.IEnumerable)GetProp(route, "Edges");
        var result = new List<string>();
        foreach (var e in edges)
        {
            result.Add((string)e);
        }

        return result;
    }

    // Mirrors RungHDp1e4RerouteDeviceTests.RepoRoot()/RungB2RouterTests.RepoRoot().
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Traffic.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (Traffic.sln not found above test assembly).");
    }
}
