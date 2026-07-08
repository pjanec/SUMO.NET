using Sim.Ingest;
using Xunit;

namespace Sim.ParityTests;

// Rung C2-iv (ingest robustness -- NOT a parity axis): let stock duarouter/randomTrips output load
// directly. Two DemandParser additions: (1) EMBEDDED routes -- <route edges="..."/> nested inside a
// <vehicle> (duarouter's default output form, vs the two-part <route id=.../> + route= reference);
// (2) DEFAULT_VEHTYPE synthesis -- a <vehicle> with no type= falls back to SUMO's built-in
// DEFAULT_VEHTYPE instead of throwing on an empty vType id. These are offline unit tests over
// DemandParser only; no engine/scenario behavior changes (every committed scenario uses explicit
// type= and top-level routes, so both new code paths stay inert).
public class RungC2ivIngestRobustnessTests
{
    [Fact]
    public void EmbeddedRoute_IsSynthesizedAsNamedRoute_AndReferencedByVehicle()
    {
        const string rou = """
            <routes>
                <vType id="car" vClass="passenger" sigma="0"/>
                <vehicle id="v0" type="car" depart="0">
                    <route edges="E0 E1 E2"/>
                </vehicle>
            </routes>
            """;

        var demand = DemandParser.ParseXml(rou);

        var vehicle = Assert.Single(demand.Vehicles);
        Assert.False(string.IsNullOrEmpty(vehicle.RouteId));
        Assert.True(demand.RoutesById.ContainsKey(vehicle.RouteId));
        Assert.Equal(new[] { "E0", "E1", "E2" }, demand.RoutesById[vehicle.RouteId].Edges);
    }

    [Fact]
    public void MissingType_FallsBackToSynthesizedDefaultVehType_ResolvingToPassengerDefaults()
    {
        const string rou = """
            <routes>
                <route id="r" edges="E0 E1"/>
                <vehicle id="v0" route="r" depart="0"/>
            </routes>
            """;

        var demand = DemandParser.ParseXml(rou);

        var vehicle = Assert.Single(demand.Vehicles);
        Assert.Equal("DEFAULT_VEHTYPE", vehicle.TypeId);
        Assert.True(demand.VTypesById.ContainsKey("DEFAULT_VEHTYPE"));

        // Resolves to SUMO's DEFAULT_VEHTYPE (vClass passenger, sigma 0.5 default, class param table).
        var resolved = VTypeDefaults.Resolve(demand.VTypesById["DEFAULT_VEHTYPE"]);
        Assert.Equal(5.0, resolved.Length, 1e-9);
        Assert.Equal(2.5, resolved.MinGap, 1e-9);
        Assert.Equal(0.5, resolved.Sigma, 1e-9);
    }

    [Fact]
    public void ExplicitDefaultVehType_IsNotOverwrittenBySynthesis()
    {
        const string rou = """
            <routes>
                <vType id="DEFAULT_VEHTYPE" vClass="passenger" sigma="0" maxSpeed="5"/>
                <route id="r" edges="E0 E1"/>
                <vehicle id="v0" route="r" depart="0"/>
            </routes>
            """;

        var demand = DemandParser.ParseXml(rou);

        var vehicle = Assert.Single(demand.Vehicles);
        Assert.Equal("DEFAULT_VEHTYPE", vehicle.TypeId);
        // The user's explicit DEFAULT_VEHTYPE (sigma 0, maxSpeed 5) must win over the synthesized one.
        var resolved = VTypeDefaults.Resolve(demand.VTypesById["DEFAULT_VEHTYPE"]);
        Assert.Equal(0.0, resolved.Sigma, 1e-9);
        Assert.Equal(5.0, resolved.MaxSpeed, 1e-9);
    }

    [Fact]
    public void StockDuarouterForm_EmbeddedRoute_PlusMissingType_LoadsTogether()
    {
        const string rou = """
            <routes>
                <vehicle id="v0" depart="0">
                    <route edges="E0 E1"/>
                </vehicle>
            </routes>
            """;

        var demand = DemandParser.ParseXml(rou);

        var vehicle = Assert.Single(demand.Vehicles);
        Assert.Equal("DEFAULT_VEHTYPE", vehicle.TypeId);
        Assert.True(demand.VTypesById.ContainsKey("DEFAULT_VEHTYPE"));
        Assert.Equal(new[] { "E0", "E1" }, demand.RoutesById[vehicle.RouteId].Edges);
    }
}
