using Sim.Core.Orca;

namespace Sim.Pedestrians.Obstacles;

// Oriented-box obstacle support for the pedestrian path (POC-5, docs/PEDESTRIAN-POC-PLAN.md POC-5
// Req 3 "dodge external car parked across a sidewalk"; docs/PEDESTRIAN-DESIGN.md §3a). This does NOT
// add a new obstacle type to OrcaCrowd -- a parked-car footprint is just a 4-vertex CLOSED polyline,
// and OrcaCrowd.AddObstacle already takes exactly that (docs/PEDESTRIAN-DESIGN.md §3: "Today
// OrcaCrowd has ... closed-polyline walls"). This class only turns the natural "oriented box"
// parameterisation (center, half-extents, heading) into the vertex list AddObstacle wants, in the
// winding OrcaCrowd's solid-obstacle convention requires.
//
// Winding: OrcaCrowd.AddObstacle ports RVO2's obstacle convention verbatim -- solid obstacles wind
// COUNTERCLOCKWISE (interior on the LEFT of each directed edge), confirmed against the existing
// parity coverage's own wall fixture (tests/Sim.ParityTests/OrcaStaticObstacleTests.cs
// WallBlocksStraightPath_...: vertices ordered (minX,minY) -> (maxX,minY) -> (maxX,maxY) ->
// (minX,maxY), which traces CCW under the standard math convention (+X right, +Y up) this codebase
// uses throughout). Corners() below reproduces exactly that ordering for an oriented box.
public static class BoxObstacle
{
    // The 4 corners of an oriented box, wound CCW starting at the "rear-right" corner (local
    // (-halfExtentX, -halfExtentY)), ready to hand straight to OrcaCrowd.AddObstacle. `angleRadians`
    // rotates the box's local +X axis (its "length" direction) counterclockwise from world +X, same
    // convention as the rest of this codebase's planar geometry (Vec2, no separate rotation type).
    public static Vec2[] Corners(Vec2 center, double halfExtentX, double halfExtentY, double angleRadians)
    {
        if (halfExtentX <= 0.0 || halfExtentY <= 0.0)
        {
            throw new ArgumentException("Box half-extents must be positive.");
        }

        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);

        Vec2 ToWorld(double lx, double ly) =>
            new(center.X + (lx * cos) - (ly * sin), center.Y + (lx * sin) + (ly * cos));

        return new[]
        {
            ToWorld(-halfExtentX, -halfExtentY),
            ToWorld(halfExtentX, -halfExtentY),
            ToWorld(halfExtentX, halfExtentY),
            ToWorld(-halfExtentX, halfExtentY),
        };
    }

    // Overload for callers that already have 4 corners (e.g. from a footprint elsewhere in the
    // simulation) rather than a center/half-extent/angle triple; the design doc's "or 4 corners"
    // case. Vertices are taken as given -- the caller is responsible for CCW winding (see class
    // remarks); AddObstacle itself has no winding-validation hook to check against.
    public static Vec2[] FromCorners(Vec2 a, Vec2 b, Vec2 c, Vec2 d) => new[] { a, b, c, d };

    // Nearest point to `p` on or in the box (the box treated as a SOLID convex quad, matching
    // OrcaCrowd's solid-obstacle semantics): `p` itself when inside, else the nearest boundary
    // point. Test-support helper (POC-5 success condition 1's "distance from ped center to box
    // interior") -- not consumed by OrcaCrowd itself, which only needs the vertex list from
    // Corners()/FromCorners() above.
    public static Vec2 NearestPoint(IReadOnlyList<Vec2> corners, Vec2 p)
    {
        if (Contains(corners, p))
        {
            return p;
        }

        var best = corners[0];
        var bestDistSq = double.MaxValue;
        var n = corners.Count;
        for (var i = 0; i < n; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % n];
            var candidate = NearestPointOnSegment(a, b, p);
            var distSq = (candidate - p).AbsSq;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = candidate;
            }
        }

        return best;
    }

    // Even-odd point-in-polygon over the box's implicitly-closed ring (matches the winding-agnostic
    // test used elsewhere in this codebase, e.g. PolygonGeometry.Contains).
    public static bool Contains(IReadOnlyList<Vec2> corners, Vec2 p)
    {
        var inside = false;
        var n = corners.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var vi = corners[i];
            var vj = corners[j];
            var crosses = (vi.Y > p.Y) != (vj.Y > p.Y);
            if (crosses && p.X < ((vj.X - vi.X) * (p.Y - vi.Y) / (vj.Y - vi.Y)) + vi.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static Vec2 NearestPointOnSegment(Vec2 a, Vec2 b, Vec2 p)
    {
        var ab = b - a;
        var abLenSq = ab.AbsSq;
        if (abLenSq <= 1e-12)
        {
            return a;
        }

        var t = Vec2.Dot(p - a, ab) / abLenSq;
        t = Math.Clamp(t, 0.0, 1.0);
        return a + (t * ab);
    }
}
