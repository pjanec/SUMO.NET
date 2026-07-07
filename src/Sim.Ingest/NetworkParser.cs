using System.Globalization;
using System.Xml.Linq;

namespace Sim.Ingest;

// Parses the rung-1 subset of SUMO's post-netconvert .net.xml: <edge> containing one or more
// <lane>, plus (rung 9a) internal (junction-interior) edges/lanes and top-level <connection>
// elements. Tolerant of missing optional attributes (documented defaults below); required
// attributes throw a clear error rather than silently defaulting, since a missing id/shape
// signals a parser-subset gap, not a legitimate omission.
public static class NetworkParser
{
    public static NetworkModel Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return ParseDocument(XDocument.Load(stream));
    }

    public static NetworkModel ParseXml(string xml) => ParseDocument(XDocument.Parse(xml));

    private static NetworkModel ParseDocument(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("net.xml has no root element.");

        var edges = new List<Edge>();
        var edgesById = new Dictionary<string, Edge>();
        var lanesById = new Dictionary<string, Lane>();

        foreach (var edgeEl in root.Elements("edge"))
        {
            // Rung 9a: internal (junction-interior) edges are now parsed too -- a multi-edge
            // route's lane sequence passes through them (e.g. ":J_2_0"). They have no from/to
            // (tolerated: From/To default to "" below, same as any edge missing the attribute).
            var edgeId = RequireAttribute(edgeEl, "id");
            var from = edgeEl.Attribute("from")?.Value ?? string.Empty;
            var to = edgeEl.Attribute("to")?.Value ?? string.Empty;

            var lanes = new List<Lane>();
            foreach (var laneEl in edgeEl.Elements("lane"))
            {
                var lane = new Lane(
                    Id: RequireAttribute(laneEl, "id"),
                    EdgeId: edgeId,
                    Index: int.Parse(RequireAttribute(laneEl, "index"), CultureInfo.InvariantCulture),
                    Speed: double.Parse(RequireAttribute(laneEl, "speed"), CultureInfo.InvariantCulture),
                    Length: double.Parse(RequireAttribute(laneEl, "length"), CultureInfo.InvariantCulture),
                    Shape: ParseShape(RequireAttribute(laneEl, "shape")));

                lanes.Add(lane);
                lanesById[lane.Id] = lane;
            }

            var edge = new Edge(edgeId, from, to, lanes);
            edges.Add(edge);
            edgesById[edgeId] = edge;
        }

        var connections = new List<Connection>();
        var connectionsByFromLaneTo = new Dictionary<(string, int, string), Connection>();

        foreach (var connEl in root.Elements("connection"))
        {
            // A <connection>'s from/to are always present in netconvert output; fromLane/toLane
            // default to "0" only for parser robustness (every connection in scope for this
            // rung specifies them explicitly). `via` (the internal lane traversed at a
            // junction) is absent for connections that cross no junction interior.
            var from = RequireAttribute(connEl, "from");
            var to = RequireAttribute(connEl, "to");
            var fromLane = int.Parse(connEl.Attribute("fromLane")?.Value ?? "0", CultureInfo.InvariantCulture);
            var toLane = int.Parse(connEl.Attribute("toLane")?.Value ?? "0", CultureInfo.InvariantCulture);
            var via = connEl.Attribute("via")?.Value;

            var connection = new Connection(from, fromLane, to, toLane, via);
            connections.Add(connection);
            // Last-wins on a duplicate key is a non-issue for this rung's straight-through,
            // single-connection-per-(fromEdge,fromLane,toEdge) network.
            connectionsByFromLaneTo[(from, fromLane, to)] = connection;
        }

        return new NetworkModel(edges, edgesById, lanesById, connections, connectionsByFromLaneTo);
    }

    private static IReadOnlyList<(double X, double Y)> ParseShape(string shape)
    {
        var points = new List<(double, double)>();
        foreach (var pair in shape.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var coords = pair.Split(',');
            var x = double.Parse(coords[0], CultureInfo.InvariantCulture);
            var y = double.Parse(coords[1], CultureInfo.InvariantCulture);
            points.Add((x, y));
        }

        return points;
    }

    private static string RequireAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value
        ?? throw new InvalidDataException($"<{element.Name}> is missing required attribute '{name}'.");
}
