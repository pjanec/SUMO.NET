using Sim.Pedestrians.Crossing;
using Xunit;

namespace Sim.Pedestrians.Tests.Crossing;

// POC-2 (docs/PEDESTRIAN-POC-PLAN.md POC-2): verifies CrossingTlReader/TlProgramCrossingSignal parse
// POC-0's real net.net.xml <tlLogic id="c"> program correctly, by cross-checking WalkAllowed against
// the phase table hand-derived from the net's own <tlLogic>/<connection> elements:
//
//   tlLogic "c" phases (cumulative): [0,37) [37,42) [42,45) [45,82) [82,87) [87,90), cycle 90s.
//   Crossing entry links (from <connection ... tl="c" linkIndex="N">):
//     :c_c0 (north, "cn nc")  <- :c_w1  linkIndex 20
//     :c_c1 (east,  "ce ec")  <- :c_w2  linkIndex 21
//     :c_c2 (south, "cs sc")  <- :c_w3  linkIndex 22
//     :c_c3 (west,  "cw wc")  <- :c_w0  linkIndex 23
//   Phase state chars at indices 20-23:
//     phase0 (gGGggrrrrrgGGggrrrrrrGrG): r G r G  -> east(21)+west(23) walk
//     phase1..2 (all-red/yellow clearance): r r r r
//     phase3 (rrrrrgGGggrrrrrgGGggGrGr): G r G r  -> north(20)+south(22) walk
//     phase4..5 (all-red/yellow clearance): r r r r
public class CrossingTlReaderTests
{
    private static string NetPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "net.net.xml");

    [Fact]
    public void LoadPrograms_ParsesTlLogicC_WithSixPhasesAndNinetySecondCycle()
    {
        var programs = CrossingTlReader.LoadPrograms(NetPath);

        Assert.True(programs.ContainsKey("c"));
        var program = programs["c"];
        Assert.Equal(6, program.Phases.Count);
        Assert.Equal(90.0, program.CycleLength, precision: 6);
        Assert.Equal(0.0, program.Offset, precision: 6);
        Assert.Equal(new[] { 37.0, 5.0, 3.0, 37.0, 5.0, 3.0 }, program.Phases.Select(p => p.Duration));
    }

    [Theory]
    [InlineData(":c_c0", "c", 20)]
    [InlineData(":c_c1", "c", 21)]
    [InlineData(":c_c2", "c", 22)]
    [InlineData(":c_c3", "c", 23)]
    public void FindCrossingLink_ResolvesEachCrossingsEntryConnection(string crossingEdgeId, string expectedTl, int expectedLinkIndex)
    {
        var link = CrossingTlReader.FindCrossingLink(NetPath, crossingEdgeId);

        Assert.NotNull(link);
        Assert.Equal(expectedTl, link!.TlId);
        Assert.Equal(expectedLinkIndex, link.LinkIndex);
    }

    [Fact]
    public void FindCrossingLink_UnknownEdge_ReturnsNull()
    {
        Assert.Null(CrossingTlReader.FindCrossingLink(NetPath, ":does_not_exist"));
    }

    // North (link 20) and south (link 22) crossings walk together during phase3 [45,82); everything
    // else (including the two clearance windows) is don't-walk for them.
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(36.9, false)]
    [InlineData(45.0, true)]
    [InlineData(81.9, true)]
    [InlineData(82.0, false)]
    [InlineData(89.9, false)]
    [InlineData(90.0, false)]     // cycle restart: phase0 begins again, north/south closed
    [InlineData(135.0, true)]     // 90 + 45: second cycle's phase3
    public void NorthAndSouthCrossings_WalkOnlyDuringPhase3(double now, bool expectedWalk)
    {
        var programs = CrossingTlReader.LoadPrograms(NetPath);
        var program = programs["c"];

        var north = new TlProgramCrossingSignal(program, 20);
        var south = new TlProgramCrossingSignal(program, 22);

        Assert.Equal(expectedWalk, north.WalkAllowed(now));
        Assert.Equal(expectedWalk, south.WalkAllowed(now));
    }

    // East (link 21) and west (link 23) crossings walk together during phase0 [0,37); closed
    // everywhere else, including the clearance windows.
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(36.9, true)]
    [InlineData(37.0, false)]
    [InlineData(44.9, false)]
    [InlineData(45.0, false)]
    [InlineData(89.9, false)]
    [InlineData(90.0, true)]      // cycle restart: phase0 begins again, east/west open
    [InlineData(126.9, true)]     // 90 + 36.9
    public void EastAndWestCrossings_WalkOnlyDuringPhase0(double now, bool expectedWalk)
    {
        var programs = CrossingTlReader.LoadPrograms(NetPath);
        var program = programs["c"];

        var east = new TlProgramCrossingSignal(program, 21);
        var west = new TlProgramCrossingSignal(program, 23);

        Assert.Equal(expectedWalk, east.WalkAllowed(now));
        Assert.Equal(expectedWalk, west.WalkAllowed(now));
    }

    [Fact]
    public void CrossingSignalFactory_BindsRealNetSignal_MatchingHandDerivedTable()
    {
        var network = PedNetworkParser.Load(NetPath);
        var westCrossing = network.Crossings.Single(c => c.Id == ":c_c3_0");
        Assert.Equal("c", westCrossing.TlLogicId);

        var signal = CrossingSignalFactory.ForCrossing(NetPath, westCrossing);

        Assert.True(signal.WalkAllowed(10.0));
        Assert.False(signal.WalkAllowed(50.0));
        Assert.True(signal.WalkAllowed(100.0)); // 90 + 10
    }
}
