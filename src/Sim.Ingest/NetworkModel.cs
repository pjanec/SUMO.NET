namespace Sim.Ingest;

// Immutable network model (DESIGN.md: "immutable arrays, not entities"). Parsed once from
// .net.xml and never mutated afterward; the engine reads it, it does not own it.
//
// Position model (ported from sumo/src/microsim/MSLane.cpp, MSVehicle.cpp): a lane's shape
// is a polyline in global x/y. A vehicle's authoritative position is lane-relative arc-length
// (MSVehicle::myState.myPos) along that polyline; global x/y is *derived* by walking the shape
// (MSLane::geometryPositionAtOffset), never the other way around.
public sealed record Lane(
    string Id,
    string EdgeId,
    int Index,
    double Speed,
    double Length,
    IReadOnlyList<(double X, double Y)> Shape);

public sealed record Edge(
    string Id,
    string From,
    string To,
    IReadOnlyList<Lane> Lanes);

// Ported from a net.xml top-level <connection>: from/to are edge ids (which may themselves be
// internal edges, e.g. ":J_0" -> "JE" for the internal lane's own outgoing continuation --
// rung 9a's route-lane-sequence resolution only ever looks up connections whose `from` is a
// normal edge, but all connections are parsed uniformly here since the source file does not
// distinguish them either). `Via` is the internal lane traversed between `from`/`to` at a
// junction; absent (null) when the connection crosses no junction interior (not exercised by
// this scenario, but tolerated).
public sealed record Connection(
    string From,
    int FromLane,
    string To,
    int ToLane,
    string? Via);

public sealed record NetworkModel(
    IReadOnlyList<Edge> Edges,
    IReadOnlyDictionary<string, Edge> EdgesById,
    IReadOnlyDictionary<string, Lane> LanesById,
    IReadOnlyList<Connection> Connections,
    IReadOnlyDictionary<(string FromEdge, int FromLane, string ToEdge), Connection> ConnectionsByFromLaneTo)
{
    // Rung 9a: resolves the ordered lane-id sequence a vehicle traverses along `routeEdges`,
    // starting at `departLaneIndex` on the first edge -- ported from how SUMO's route/lane
    // machinery expands a route's edge sequence through each junction's connection/via lane
    // (MSLane::getInternalFollowingLane / MSEdge's Successors, conceptually: pick the
    // <connection> whose fromLane matches the current lane index, append its via internal lane
    // if present, then the destination toLane). For a single-edge route this degenerates to
    // exactly `[<edge>_<departLaneIndex>]`, matching every prior single-lane rung's behavior
    // (CLAUDE.md-mandated regression: no change for single-edge routes).
    //
    // Scoped out (not needed by rung 9a's single-lane-per-edge, straight-through scenario):
    // multi-lane lane-choice/continuity heuristics when more than one connection matches the
    // same (fromEdge, fromLaneIndex, toEdge) key, and junction right-of-way (rung 9b) -- this
    // purely resolves the lane sequence, it makes no yielding decision.
    public IReadOnlyList<string> ResolveLaneSequence(IReadOnlyList<string> routeEdges, int departLaneIndex)
    {
        var sequence = new List<string>();
        var currentLaneIndex = departLaneIndex;
        var firstEdge = EdgesById[routeEdges[0]];
        var currentLane = firstEdge.Lanes.First(l => l.Index == currentLaneIndex);
        sequence.Add(currentLane.Id);

        for (var i = 0; i < routeEdges.Count - 1; i++)
        {
            var fromEdgeId = routeEdges[i];
            var toEdgeId = routeEdges[i + 1];
            var key = (fromEdgeId, currentLaneIndex, toEdgeId);

            if (!ConnectionsByFromLaneTo.TryGetValue(key, out var connection))
            {
                throw new InvalidDataException(
                    $"No <connection> found from edge '{fromEdgeId}' lane {currentLaneIndex} to edge '{toEdgeId}'.");
            }

            if (connection.Via is { } via)
            {
                sequence.Add(via);
            }

            var toEdge = EdgesById[toEdgeId];
            var toLane = toEdge.Lanes.First(l => l.Index == connection.ToLane);
            sequence.Add(toLane.Id);
            currentLaneIndex = connection.ToLane;
        }

        return sequence;
    }
}
