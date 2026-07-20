using System.Collections.Generic;
using Sim.Core.Orca;
using Sim.Pedestrians.Lod;
using Xunit;

namespace Sim.Pedestrians.Tests.Lod;

// W1 (docs/PEDESTRIAN-WEAVE-PRODUCTION-DESIGN.md §1, §3, §8): the deterministic weave injected at the SHARED
// evaluator (PathArcMotion.SampleAt / ActivityTimeline.Evaluate). The load-bearing claims:
//   1. weave OFF (no HalfWidths) is byte-identical to the pre-weave centreline pose (no regression);
//   2. weave ON places the pose exactly at centre + tangent.PerpCW * (CenterShift + Offset);
//   3. server==IG is bit-for-bit after an Encode/Decode round-trip (seed + global seed + per-vertex widths);
//   4. the weave tapers to 0 at the leg ends, so the segment-anchor chain (Walk->Pause) stays continuous.
public class WeaveEvaluatorTests
{
    // A straight 40 m eastbound leg, constant 2 m half-width, walking at 1.3 m/s.
    private static readonly IReadOnlyList<Vec2> Path = new[] { new Vec2(0, 0), new Vec2(40, 0) };
    private static readonly IReadOnlyList<double> Widths = new[] { 2.0, 2.0 };
    private const double Speed = 1.3;
    private const ulong Seed = 0xABCDEF12UL;
    private const ulong GlobalSeed = 42UL;

    private static ActivityTimeline WeaveLeg() =>
        new(t0: 0.0, new[] { new WalkSegment(Path, Speed, Widths) }, Seed, GlobalSeed);

    [Fact]
    public void WeaveOff_IsByteIdenticalToCentreline()
    {
        // No HalfWidths + seed 0 == weave inactive: the Walk pose must equal the plain PathArc centreline
        // exactly, so every pre-weave timeline/scenario is unaffected.
        var plain = new ActivityTimeline(0.0, new[] { new WalkSegment(Path, Speed) });
        for (var now = 0.0; now <= 30.0; now += 0.5)
        {
            var pose = plain.PoseAt(now);
            var centre = PathArcMotion.PositionAt(Path, 0.0, Speed, now);
            Assert.Equal(centre.X, pose.Pos.X, precision: 15);
            Assert.Equal(centre.Y, pose.Pos.Y, precision: 15);
        }
    }

    [Fact]
    public void WeaveOn_PoseEqualsCentrePlusNormalTimesLateral()
    {
        // The exact composition the design specifies, recomputed independently from LateralWeave.
        var tl = WeaveLeg();
        var routeLen = PathArcMotion.PathLength(Path);
        for (var now = 0.5; now <= 29.0; now += 0.5)
        {
            var pose = tl.PoseAt(now);
            var s = Speed * now;
            var centre = PathArcMotion.SampleAt(Path, Widths, s, out var tangent, out var hw);
            var c = LateralWeave.CenterShift(s, now, routeLen, GlobalSeed, 0.35 * hw, WeaveParams.Default);
            var room = c > hw ? 0.0 : hw - c;
            var off = LateralWeave.Offset(s, routeLen, Seed, room, WeaveParams.Default);
            var expected = centre + (tangent.PerpCW * (c + off));
            Assert.Equal(expected.X, pose.Pos.X, precision: 15);
            Assert.Equal(expected.Y, pose.Pos.Y, precision: 15);
        }
    }

    [Fact]
    public void WeaveOn_ActuallyLeavesTheCentreline()
    {
        // Guard against a vacuous pass: somewhere in the interior the weave must move the ped OFF the
        // centreline (here the centreline is y=0, so |y| must exceed a visible threshold).
        var tl = WeaveLeg();
        var maxOff = 0.0;
        for (var now = 1.0; now <= 29.0; now += 0.25)
        {
            maxOff = System.Math.Max(maxOff, System.Math.Abs(tl.PoseAt(now).Pos.Y));
        }

        Assert.True(maxOff > 0.3, $"weave should visibly leave the centreline; max |y| was {maxOff:F3}");
    }

    [Fact]
    public void ServerEqualsIg_BitForBit_AfterWireRoundTrip()
    {
        // THE server==IG guarantee: the decoded timeline must reproduce the server pose to the last bit,
        // across the whole leg -- seed, global seed, and per-vertex widths all survive the wire.
        var server = WeaveLeg();
        var ig = ActivityTimelineWire.Decode(ActivityTimelineWire.Encode(server));

        Assert.Equal(server.Seed, ig.Seed);
        Assert.Equal(server.GlobalSeed, ig.GlobalSeed);

        for (var now = 0.0; now <= 32.0; now += 0.1)
        {
            var a = server.PoseAt(now);
            var b = ig.PoseAt(now);
            Assert.Equal(a.Pos.X, b.Pos.X, precision: 15);
            Assert.Equal(a.Pos.Y, b.Pos.Y, precision: 15);
            Assert.Equal(a.Heading.X, b.Heading.X, precision: 15);
            Assert.Equal(a.Heading.Y, b.Heading.Y, precision: 15);
        }
    }

    [Fact]
    public void Weave_TapersToZero_AtLegEnds_ForAnchorContinuity()
    {
        // The weave offset must vanish at s=0 and s=L so the Walk pose meets the centreline endpoints the
        // segment-anchor chain (Walk->Pause / Walk->Dwell) was built from -- otherwise a lateral pop appears
        // at every segment boundary.
        var tl = WeaveLeg();
        var routeLen = PathArcMotion.PathLength(Path);
        var startPose = tl.PoseAt(0.0);            // s = 0
        var endPose = tl.PoseAt(routeLen / Speed); // s = L
        Assert.Equal(0.0, startPose.Pos.Y, precision: 9);
        Assert.Equal(0.0, endPose.Pos.Y, precision: 9);
        Assert.Equal(40.0, endPose.Pos.X, precision: 9);
    }
}
