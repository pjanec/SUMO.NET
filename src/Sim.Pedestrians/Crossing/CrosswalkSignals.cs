using System;
using System.Collections.Generic;
using Sim.Core.Orca;
using Sim.Pedestrians.Navigation.Bake;

namespace Sim.Pedestrians.Crossing;

// Phase 2b (docs/LIVE-CITY-CROSSWALK-SIGNAL-DESIGN.md §2b): the ped-side companion to
// CrosswalkSignalSchedule. It pairs the analytic walk-window schedule with the *geometry* of the
// signalized crossings, so the demand-side timeline builder can answer two spatial questions cheaply:
//   * "is world point p on a signalized crossing, and which one?"  -> TryLocate
//   * "arriving at that crossing's kerb at tArrive walking at `speed`, when may I step on?" -> NextWalkStart
// Only crossings that are BOTH baked (have a polygon) AND signalized (in the schedule) are held here;
// everything else is invisible to this provider (TryLocate returns false -> no wait inserted -> the
// car-side occupancy gate stays the safety net for those).
//
// Determinism/parity: this type performs no RNG and no runtime signal polling -- NextWalkStart is a pure
// function of (tArrive, speed) via the closed-form schedule, so a wait it induces keeps the ped a pure
// ActivityTimeline (server==IG holds). It is only ever consulted when a caller injects it into
// PedDemandConfig.CrosswalkSignals (default null -> inert -> every committed ped test byte-identical).
public sealed class CrosswalkSignals
{
    private readonly struct SignalizedCrossing
    {
        public readonly string Id;
        public readonly Vec2[] Verts;
        public readonly double MinX;
        public readonly double MinY;
        public readonly double MaxX;
        public readonly double MaxY;
        public readonly double CrossLength; // conservative walk distance across the crossing (metres)

        public SignalizedCrossing(string id, Vec2[] verts)
        {
            Id = id;
            Verts = verts;
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var v in verts)
            {
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
            }

            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            var dx = maxX - minX;
            var dy = maxY - minY;
            // Over-estimate the ped's on-crossing distance by the polygon's bbox diagonal: an upper bound
            // on any straight walk across it, so the fit test in NextWalkStart is conservative (a ped only
            // steps on when it can clear even this longer walk within one green window -- it never gets
            // caught mid-crossing when the signal flips).
            CrossLength = Math.Sqrt((dx * dx) + (dy * dy));
        }
    }

    private readonly CrosswalkSignalSchedule _schedule;
    private readonly SignalizedCrossing[] _crossings;

    private CrosswalkSignals(CrosswalkSignalSchedule schedule, SignalizedCrossing[] crossings)
    {
        _schedule = schedule;
        _crossings = crossings;
    }

    // How many baked+signalized crossings this provider covers (diagnostic).
    public int SignalizedCount => _crossings.Length;

    public bool IsSignalized(string crossingEdgeId) => _schedule.IsSignalized(crossingEdgeId);

    // Convenience for callers holding a baked crossing polygon's LANE id (":c_c0_0"): maps it to the
    // edge id and reports whether that crossing is signalized. Used by the car-side gate to EXCLUDE
    // signalized crossings (peds only occupy those during walk = car red, so gating them would be a
    // phantom stop); the gate keeps the unsignalized crossings as its safety net.
    public bool IsSignalizedLane(string crossingLaneId) => _schedule.IsSignalized(LaneToEdgeId(crossingLaneId));

    // Build from a net + the baked crossing polygons (typically a crop's Crossing polygons). A polygon is
    // kept iff its Id is a signalized crossing in the schedule read from `netPath`. `actuatedTlIds`
    // (optional) is forwarded to the schedule so detector-driven TLs are excluded (they fall back to the
    // gate). Non-crossing polygons and unsignalized crossings are dropped.
    public static CrosswalkSignals FromNet(
        string netPath, IEnumerable<BakedPolygon> polygons, ISet<string>? actuatedTlIds = null)
    {
        // A baked crossing polygon's Id is the crossing's ped-LANE id (e.g. ":c_c0_0"); the net's
        // <connection>/<tlLogic> gate it by the crossing EDGE id (":c_c0"). Map lane->edge (strip the
        // trailing "_<laneIndex>") so the schedule (which keys on the edge id) lines up, and store each
        // polygon under that same edge id so TryLocate/NextWalkStart speak one id namespace.
        var polyByEdge = new Dictionary<string, BakedPolygon>(StringComparer.Ordinal);
        foreach (var p in polygons)
        {
            if (p.Kind == BakedPolygonKind.Crossing && p.Vertices.Count >= 3)
            {
                polyByEdge[LaneToEdgeId(p.Id)] = p;
            }
        }

        var schedule = CrosswalkSignalSchedule.FromNet(netPath, polyByEdge.Keys, actuatedTlIds);

        var list = new List<SignalizedCrossing>();
        foreach (var kv in polyByEdge)
        {
            if (!schedule.IsSignalized(kv.Key))
            {
                continue; // unsignalized/actuated -> not our concern (car-side gate covers it)
            }

            var verts = new Vec2[kv.Value.Vertices.Count];
            for (var i = 0; i < verts.Length; i++)
            {
                verts[i] = kv.Value.Vertices[i];
            }

            list.Add(new SignalizedCrossing(kv.Key, verts));
        }

        return new CrosswalkSignals(schedule, list.ToArray());
    }

    // SUMO lane id = "<edgeId>_<laneIndex>". A crossing's internal edge id (":c_c0") is its lane id
    // (":c_c0_0") with the trailing "_<digits>" removed. If the id has no such suffix it is returned
    // unchanged (already an edge id).
    private static string LaneToEdgeId(string laneId)
    {
        var us = laneId.LastIndexOf('_');
        if (us <= 0 || us == laneId.Length - 1)
        {
            return laneId;
        }

        for (var i = us + 1; i < laneId.Length; i++)
        {
            if (!char.IsDigit(laneId[i]))
            {
                return laneId; // suffix isn't purely numeric -> not a lane index
            }
        }

        return laneId[..us];
    }

    // Wrap a schedule + explicit crossing polygons directly (tests / callers holding parsed data). Only
    // polygons whose Id the schedule reports as signalized are retained.
    public static CrosswalkSignals ForCrossings(
        CrosswalkSignalSchedule schedule, IEnumerable<BakedPolygon> polygons)
    {
        var list = new List<SignalizedCrossing>();
        foreach (var p in polygons)
        {
            if (p.Kind != BakedPolygonKind.Crossing || p.Vertices.Count < 3 || !schedule.IsSignalized(p.Id))
            {
                continue;
            }

            var verts = new Vec2[p.Vertices.Count];
            for (var i = 0; i < verts.Length; i++)
            {
                verts[i] = p.Vertices[i];
            }

            list.Add(new SignalizedCrossing(p.Id, verts));
        }

        return new CrosswalkSignals(schedule, list.ToArray());
    }

    // Is world point `p` on a signalized crossing? If so, `crossingId` names it. A point is on at most one
    // crossing (crossings don't overlap), so the first bbox+polygon hit wins.
    public bool TryLocate(Vec2 p, out string crossingId)
    {
        for (var ci = 0; ci < _crossings.Length; ci++)
        {
            ref readonly var c = ref _crossings[ci];
            if (p.X < c.MinX || p.X > c.MaxX || p.Y < c.MinY || p.Y > c.MaxY)
            {
                continue;
            }

            if (PointInPolygon(p, c.Verts))
            {
                crossingId = c.Id;
                return true;
            }
        }

        crossingId = string.Empty;
        return false;
    }

    // Earliest absolute time >= tArrive at which a ped may step onto `crossingId` and clear it, walking at
    // `speed`. crossTime is the crossing's conservative walk length / speed; the schedule then returns the
    // earliest walk window that fully contains it. Unsignalized/unknown -> tArrive (no wait).
    public double NextWalkStart(string crossingId, double tArrive, double speed)
    {
        var crossLen = CrossLengthOf(crossingId);
        var crossTime = speed > 0.0 ? crossLen / speed : 0.0;
        return _schedule.NextWalkStart(crossingId, tArrive, crossTime);
    }

    private double CrossLengthOf(string crossingId)
    {
        for (var i = 0; i < _crossings.Length; i++)
        {
            if (string.Equals(_crossings[i].Id, crossingId, StringComparison.Ordinal))
            {
                return _crossings[i].CrossLength;
            }
        }

        return 0.0;
    }

    // Same ray-cast point-in-polygon as CrossingOccupancySource (crossings are small convex quads).
    private static bool PointInPolygon(Vec2 p, Vec2[] v)
    {
        var inside = false;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
        {
            if (((v[i].Y > p.Y) != (v[j].Y > p.Y)) &&
                (p.X < (v[j].X - v[i].X) * (p.Y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X))
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
