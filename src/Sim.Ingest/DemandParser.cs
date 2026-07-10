using System.Globalization;
using System.Xml.Linq;

namespace Sim.Ingest;

// Parses the rung-1 subset of .rou.xml: <vType>, <route>, <vehicle>. Missing optional
// attributes fall back to documented SUMO defaults where the value is purely numeric/simple;
// symbolic values (departPos="base"/"random", departSpeed="max", departLane="free"/"best",
// etc.) are NOT resolved here -- that placement/defaulting logic is a Task 3+ concern. This
// parser only has to be correct for rung 1's fully-numeric attributes.
public static class DemandParser
{
    // C2-iv: SUMO's built-in default vType id (SUMOVTypeParameter's DEFAULT_VEHTYPE) -- the type a
    // <vehicle> with no type= falls back to.
    private const string DefaultVehTypeId = "DEFAULT_VEHTYPE";

    public static DemandModel Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return ParseDocument(XDocument.Load(stream));
    }

    public static DemandModel ParseXml(string xml) => ParseDocument(XDocument.Parse(xml));

    private static DemandModel ParseDocument(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("rou.xml has no root element.");

        var vTypes = new List<VType>();
        var vTypesById = new Dictionary<string, VType>();
        foreach (var vTypeEl in root.Elements("vType"))
        {
            var vType = new VType(
                Id: RequireAttribute(vTypeEl, "id"),
                VClass: vTypeEl.Attribute("vClass")?.Value,
                Sigma: ParseNullableDouble(vTypeEl, "sigma"),
                MaxSpeed: ParseNullableDouble(vTypeEl, "maxSpeed"),
                Accel: ParseNullableDouble(vTypeEl, "accel"),
                Decel: ParseNullableDouble(vTypeEl, "decel"),
                Tau: ParseNullableDouble(vTypeEl, "tau"),
                MinGap: ParseNullableDouble(vTypeEl, "minGap"),
                Length: ParseNullableDouble(vTypeEl, "length"),
                EmergencyDecel: ParseNullableDouble(vTypeEl, "emergencyDecel"),
                SpeedFactor: ParseNullableDouble(vTypeEl, "speedFactor"),
                // Rung A3: a vType ATTRIBUTE, not a <param> child -- SUMO's getJMParam reads the
                // attribute map (SUMOVTypeParameter's map of junction-model params populated
                // straight from the <vType>'s own XML attributes for jm* names).
                JmDriveAfterRedTime: ParseNullableDouble(vTypeEl, "jmDriveAfterRedTime"),
                // Rung ER2: emergency ignore-FOE junction-model attributes, read from the <vType>'s
                // own XML attribute map exactly like jmDriveAfterRedTime (SUMO's getJMParam).
                JmIgnoreFoeProb: ParseNullableDouble(vTypeEl, "jmIgnoreFoeProb"),
                JmIgnoreFoeSpeed: ParseNullableDouble(vTypeEl, "jmIgnoreFoeSpeed"),
                JmIgnoreJunctionFoeProb: ParseNullableDouble(vTypeEl, "jmIgnoreJunctionFoeProb"),
                // Rung ER3 (give-way): whether this vType carries an active blue-light siren (our
                // opt-in model of SUMO's MSDevice_Bluelight assignment, `has.bluelight.device`).
                // Default false -> no give-way is ever induced, so every existing scenario (incl.
                // the emergency-privilege scenarios 16/50/51/52, whose EVs set NO bluelight) is
                // byte-identical. Read as a vType attribute `hasBluelight="true"`.
                HasBluelight: ParseNullableBool(vTypeEl, "hasBluelight"),
                // C11-i: SUMOVTypeParameter.cpp's carFollowModel="..." vType attribute (a plain
                // string tag name -- "Krauss", "IDM", etc. -- SUMOXMLDefinitions::CarFollowModels).
                CarFollowModel: vTypeEl.Attribute("carFollowModel")?.Value);

            vTypes.Add(vType);
            vTypesById[vType.Id] = vType;
        }

        var routes = new List<Route>();
        var routesById = new Dictionary<string, Route>();
        foreach (var routeEl in root.Elements("route"))
        {
            var route = new Route(
                Id: RequireAttribute(routeEl, "id"),
                Edges: RequireAttribute(routeEl, "edges").Split(' ', StringSplitOptions.RemoveEmptyEntries));

            routes.Add(route);
            routesById[route.Id] = route;
        }

        var vehicles = new List<VehicleDef>();
        var needsDefaultVehType = false;
        foreach (var vehicleEl in root.Elements("vehicle"))
        {
            var vehId = RequireAttribute(vehicleEl, "id");

            var stops = vehicleEl.Elements("stop")
                .Select(stopEl => new StopDef(
                    LaneId: RequireAttribute(stopEl, "lane"),
                    StartPos: ParseNullableDouble(stopEl, "startPos") ?? 0.0,
                    EndPos: ParseNullableDouble(stopEl, "endPos") ?? 0.0,
                    Duration: ParseNullableDouble(stopEl, "duration") ?? 0.0))
                .ToList();

            // C2-iv: a vehicle's route is either a `route=` reference to a top-level <route id=...>
            // OR a nested <route edges="..."/> (duarouter's default EMBEDDED-route output). For the
            // embedded form, synthesize a named Route so the rest of the pipeline (route-by-id) is
            // unchanged -- keyed by a per-vehicle id (SUMO names an embedded route after its vehicle;
            // the '!' prefix keeps it from clashing with a user route id).
            var routeId = vehicleEl.Attribute("route")?.Value;
            if (routeId is null)
            {
                var embeddedRouteEl = vehicleEl.Element("route")
                    ?? throw new InvalidDataException(
                        $"<vehicle id='{vehId}'> has neither a route= attribute nor a nested <route>.");
                routeId = $"!{vehId}";
                var embeddedRoute = new Route(
                    routeId,
                    RequireAttribute(embeddedRouteEl, "edges").Split(' ', StringSplitOptions.RemoveEmptyEntries));
                routes.Add(embeddedRoute);
                routesById[routeId] = embeddedRoute;
            }

            // C2-iv: a <vehicle> with no type= uses SUMO's built-in DEFAULT_VEHTYPE (synthesized
            // after the loop), rather than throwing on an empty vType id.
            var typeId = vehicleEl.Attribute("type")?.Value;
            if (typeId is null)
            {
                typeId = DefaultVehTypeId;
                needsDefaultVehType = true;
            }

            vehicles.Add(new VehicleDef(
                Id: vehId,
                TypeId: typeId,
                RouteId: routeId,
                Depart: ParseNullableDouble(vehicleEl, "depart") ?? 0.0,
                DepartPos: ParseNullableDouble(vehicleEl, "departPos") ?? 0.0,
                DepartSpeed: ParseNullableDouble(vehicleEl, "departSpeed") ?? 0.0,
                DepartLaneIndex: ParseNullableInt(vehicleEl, "departLane") ?? 0,
                Stops: stops));
        }

        // C2-iv: synthesize DEFAULT_VEHTYPE when referenced-but-undeclared -- SUMOVTypeParameter's
        // built-in default (vClass passenger, every param at its class default incl. sigma 0.5;
        // VTypeDefaults.Resolve fills the nulls). If the user DID declare their own DEFAULT_VEHTYPE,
        // that one is already in the map and wins.
        if (needsDefaultVehType && !vTypesById.ContainsKey(DefaultVehTypeId))
        {
            var defaultVehType = new VType(Id: DefaultVehTypeId, VClass: null, Sigma: null);
            vTypes.Add(defaultVehType);
            vTypesById[DefaultVehTypeId] = defaultVehType;
        }

        return new DemandModel(vTypes, vTypesById, routes, routesById, vehicles);
    }

    private static double? ParseNullableDouble(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return value is null ? null : double.Parse(value, CultureInfo.InvariantCulture);
    }

    // Rung ER3: SUMO's boolean attribute forms (utils/common/StringUtils::toBool accepts
    // true/1/x/t/yes and false/0/-/f/no). null when the attribute is absent (-> the vType-default
    // false in VTypeDefaults.Resolve).
    private static bool? ParseNullableBool(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        if (value is null)
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() is "true" or "1" or "x" or "t" or "yes";
    }

    private static int? ParseNullableInt(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return value is null ? null : int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string RequireAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value
        ?? throw new InvalidDataException($"<{element.Name}> is missing required attribute '{name}'.");
}
