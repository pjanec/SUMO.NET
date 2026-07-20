using Sim.Core.Orca;

namespace Sim.Pedestrians.Lod;

// The PathArc dead-reckoning model (docs/PEDESTRIAN-DESIGN.md §7; docs/PEDESTRIAN-POC-PLAN.md POC-3):
// a pedestrian's position along a fixed polyline is a PURE function of (path, startTime, speed, now) --
// no neighbour state, no System.Random, no hidden mutable state. This is the load-bearing identity the
// whole POC rests on: the server calls THIS SAME function to advance a low-power ped, and the headless
// IG calls it again to reconstruct that ped from the one-time path broadcast -- so "server == IG for
// low-power" follows from sharing the code, not from independently matching two implementations.
//
// Allocation-light: no LINQ, no per-call allocation, a single forward walk of the polyline.
public static class PathArcMotion
{
    // World position at arc-length speed * max(0, now - startTime) along `path`, clamped at the final
    // vertex once that arc-length exceeds the path's total length.
    public static Vec2 PositionAt(IReadOnlyList<Vec2> path, double startTime, double speed, double now)
    {
        var s = ArcLength(startTime, speed, now);
        return Walk(path, s, out _);
    }

    // Direction of the segment the walk currently sits on, times `speed` -- Vec2.Zero once clamped at
    // the final vertex (the agent has arrived and stopped).
    public static Vec2 VelocityAt(IReadOnlyList<Vec2> path, double startTime, double speed, double now)
    {
        var s = ArcLength(startTime, speed, now);
        Walk(path, s, out var direction);
        return direction * speed;
    }

    // W1 (docs/PEDESTRIAN-WEAVE-PRODUCTION-DESIGN.md §1, §3): the weave-aware sample. One forward walk of
    // `path` to arc-length `s` returns the centreline point AND the unit tangent AND the interpolated
    // half-width there -- everything LateralWeave needs to place `centre + tangent.PerpCW * offset`, without a
    // second walk. `halfWidths` is per-vertex (parallel to `path`); when null the corridor half-width is 0
    // (weave OFF -> pose is exactly the centreline, byte-identical to PositionAt). This is the SHARED evaluator
    // both the server (PedLodManager.PositionOf -> ActivityTimeline.PoseAt) and the IG
    // (HeadlessIg.ReconstructSample -> the decoded PoseAt) call, so server==IG holds by construction.
    public static Vec2 SampleAt(
        IReadOnlyList<Vec2> path, IReadOnlyList<double>? halfWidths, double s, out Vec2 tangent, out double halfWidth)
    {
        tangent = Vec2.Zero;
        halfWidth = 0.0;

        if (path.Count == 0)
        {
            return Vec2.Zero;
        }

        if (path.Count == 1)
        {
            halfWidth = halfWidths is { Count: > 0 } ? halfWidths[0] : 0.0;
            return path[0];
        }

        var remaining = s < 0.0 ? 0.0 : s;
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            var seg = b - a;
            var len = seg.Abs;
            if (len <= 1e-12)
            {
                continue; // degenerate (duplicate-point) segment: no arc-length, skip it
            }

            if (remaining <= len)
            {
                var t = remaining / len;
                tangent = seg / len;
                if (halfWidths != null && halfWidths.Count == path.Count)
                {
                    halfWidth = halfWidths[i] + ((halfWidths[i + 1] - halfWidths[i]) * t);
                }

                return new Vec2(a.X + (seg.X * t), a.Y + (seg.Y * t));
            }

            remaining -= len;
        }

        // clamped at the final vertex (arrived): tangent stays Zero, half-width is the last vertex's.
        halfWidth = halfWidths is { Count: > 0 } ? halfWidths[^1] : 0.0;
        return path[^1];
    }

    private static double ArcLength(double startTime, double speed, double now) =>
        speed * Math.Max(0.0, now - startTime);

    // Total arc-length of `path` -- the sum of segment lengths, skipping degenerate (duplicate-point)
    // segments exactly like Walk does. ActivityTimeline (docs/PEDESTRIAN-LIVELINESS-DESIGN.md §2) uses
    // this to size a Walk segment's duration (pathLength / speed) without duplicating this walk.
    public static double PathLength(IReadOnlyList<Vec2> path)
    {
        if (path.Count < 2)
        {
            return 0.0;
        }

        var total = 0.0;
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var len = (path[i + 1] - path[i]).Abs;
            if (len > 1e-12)
            {
                total += len;
            }
        }

        return total;
    }

    // Walks `path` to arc-length `s`, returning the world point and (via `direction`) the unit
    // direction of the segment it landed on. `direction` is Vec2.Zero once `s` reaches or exceeds the
    // path's total length (clamped at the final vertex -- no more motion).
    private static Vec2 Walk(IReadOnlyList<Vec2> path, double s, out Vec2 direction)
    {
        if (path.Count == 0)
        {
            direction = Vec2.Zero;
            return Vec2.Zero;
        }

        if (path.Count == 1)
        {
            direction = Vec2.Zero;
            return path[0];
        }

        var remaining = s;
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            var seg = b - a;
            var len = seg.Abs;
            if (len <= 1e-12)
            {
                continue; // degenerate (duplicate-point) segment: no arc-length, skip it
            }

            if (remaining <= len)
            {
                var t = remaining / len;
                direction = seg / len;
                return new Vec2(a.X + (seg.X * t), a.Y + (seg.Y * t));
            }

            remaining -= len;
        }

        direction = Vec2.Zero; // clamped at the final vertex
        return path[^1];
    }
}
