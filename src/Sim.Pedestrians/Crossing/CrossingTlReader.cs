using System.Globalization;
using System.Xml.Linq;

namespace Sim.Pedestrians.Crossing;

// One <phase> of a <tlLogic> (docs/PEDESTRIAN-POC-PLAN.md POC-2).
public sealed record TlPhaseSpec(double Duration, string State);

// One parsed <tlLogic id=".." offset=".."> program -- the 'static' phase-cycle case only (POC-0's
// fixture uses type="static"; an actuated crossing program is out of POC-2's scope, same scoping
// Sim.Core.TrafficLightState itself declares for the parity side).
public sealed record TlProgramSpec(string Id, double Offset, IReadOnlyList<TlPhaseSpec> Phases)
{
    public double CycleLength => Phases.Sum(p => p.Duration);
}

// One tl-controlled entry link INTO a crossing edge (the walkingarea -> crossing <connection>, e.g.
// ":c_w1" -> ":c_c0" tl="c" linkIndex="20").
public sealed record CrossingLink(string CrossingEdgeId, string TlId, int LinkIndex);

// Reads ONLY the <tlLogic> phase programs and the crossing-gating <connection> entries out of a
// net.net.xml, using System.Xml.Linq directly -- deliberately a SECOND, independent read of the same
// committed file PedNetworkParser.cs already reads, not a call into Sim.Ingest.NetworkParser /
// Sim.Ingest.NetworkModel / Sim.Core.TrafficLightState. Sim.Pedestrians.csproj's own header comment is
// explicit that this project "must never reference Sim.Ingest or any parity source"
// (docs/PEDESTRIAN-DESIGN.md §0 Principle 6), so this mirrors PedNetworkParser's "separate ped ingest"
// pattern rather than reusing the parity-side tlLogic model, even though the two are reading the exact
// same XML elements.
//
// A crossing's *entry* link (walkingarea -> crossing) carries the tl/linkIndex that gates it; the
// *exit* link (crossing -> far walkingarea) is always uncontrolled (no tl attribute) -- nothing needs
// to stop a pedestrian who has already committed to crossing. So exactly one tl-controlled
// <connection> targets each signalized crossing edge (verified against POC-0's net.net.xml: each of
// ":c_c0".":c_c1",":c_c2",":c_c3" receives exactly one connection with a "tl" attribute).
public static class CrossingTlReader
{
    public static IReadOnlyDictionary<string, TlProgramSpec> LoadPrograms(string netPath)
    {
        var root = LoadRoot(netPath);
        var result = new Dictionary<string, TlProgramSpec>(StringComparer.Ordinal);

        foreach (var tlLogic in root.Elements("tlLogic"))
        {
            var id = (string)tlLogic.Attribute("id")!;
            var offset = ParseDoubleOrDefault((string?)tlLogic.Attribute("offset"), 0.0);
            var phases = new List<TlPhaseSpec>();
            foreach (var phase in tlLogic.Elements("phase"))
            {
                phases.Add(new TlPhaseSpec(
                    Duration: ParseDouble((string)phase.Attribute("duration")!),
                    State: (string)phase.Attribute("state")!));
            }

            result[id] = new TlProgramSpec(id, offset, phases);
        }

        return result;
    }

    // All tl-controlled crossing-entry links in the net, keyed by crossing edge id -- one pass over the
    // file (each signalized crossing edge receives exactly one tl connection; see the type remarks). Use
    // this instead of calling FindCrossingLink in a loop when classifying many crossings (e.g. a whole
    // crop), so a large net is parsed ONCE rather than once per crossing.
    public static IReadOnlyDictionary<string, CrossingLink> LoadCrossingLinks(string netPath)
    {
        var root = LoadRoot(netPath);
        var result = new Dictionary<string, CrossingLink>(StringComparer.Ordinal);
        foreach (var connection in root.Elements("connection"))
        {
            var to = (string?)connection.Attribute("to");
            var tl = (string?)connection.Attribute("tl");
            var linkIndexAttr = (string?)connection.Attribute("linkIndex");
            if (to is null || tl is null || linkIndexAttr is null || result.ContainsKey(to))
            {
                continue;
            }

            result[to] = new CrossingLink(to, tl, int.Parse(linkIndexAttr, CultureInfo.InvariantCulture));
        }

        return result;
    }

    // The tl-controlled entry link for the crossing edge `crossingEdgeId` (e.g. ":c_c0"), or null when
    // the crossing has no signalized entry connection (an unsignalized crossing, or a malformed net).
    public static CrossingLink? FindCrossingLink(string netPath, string crossingEdgeId)
    {
        var root = LoadRoot(netPath);
        foreach (var connection in root.Elements("connection"))
        {
            if ((string?)connection.Attribute("to") != crossingEdgeId)
            {
                continue;
            }

            var tl = (string?)connection.Attribute("tl");
            var linkIndexAttr = (string?)connection.Attribute("linkIndex");
            if (tl is null || linkIndexAttr is null)
            {
                continue;
            }

            return new CrossingLink(crossingEdgeId, tl, int.Parse(linkIndexAttr, CultureInfo.InvariantCulture));
        }

        return null;
    }

    private static XElement LoadRoot(string netPath)
    {
        var doc = XDocument.Load(netPath);
        return doc.Root ?? throw new InvalidOperationException($"'{netPath}' has no root element.");
    }

    private static double ParseDouble(string s) =>
        double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static double ParseDoubleOrDefault(string? s, double fallback) =>
        s is null ? fallback : ParseDouble(s);
}
