using Sim.Core;
using Sim.Replication;
using Xunit;

namespace Sim.ParityTests;

// docs/SUMOSHARP-PACKAGING-DESIGN.md §5 / D9 -- the transport-neutral timestamped sample + per-
// vehicle history buffer (Stage P2-A). Covers the capacity-bounded drop-oldest-first append
// behaviour, the newest-last index ordering, and the "last index with TimestampSeconds <= query"
// bracket scan that DrClock's interpolation walk relies on (see DrClock.cs's history[i].
// TimestampSeconds <= sampleT loop).
public class VehicleSampleHistoryTests
{
    private static TimestampedSample SampleAt(double timestampSeconds) =>
        new(timestampSeconds, new VehicleRecord(
            new VehicleHandle(1, 1), DrModel.LaneArc, laneHandle: 0,
            pos: timestampSeconds, posLat: 0, speed: 0, accel: 0, latSpeed: 0, upcoming: default));

    [Fact]
    public void Append_PastCapacity_DropsOldestFirst()
    {
        var history = new VehicleSampleHistory(capacity: 3);

        for (var t = 1; t <= 5; t++)
        {
            history.Append(SampleAt(t));
        }

        Assert.Equal(3, history.Count);
        Assert.Equal(3.0, history[0].TimestampSeconds);
        Assert.Equal(4.0, history[1].TimestampSeconds);
        Assert.Equal(5.0, history[2].TimestampSeconds);
    }

    [Fact]
    public void Indexer_IsNewestLast()
    {
        var history = new VehicleSampleHistory(capacity: 8);

        history.Append(SampleAt(1));
        history.Append(SampleAt(2));
        history.Append(SampleAt(3));

        Assert.Equal(3.0, history[history.Count - 1].TimestampSeconds);
    }

    // Replicates DrClock's bracket loop: the last index whose TimestampSeconds <= the query time
    // (clamped to index 0 if the query is older than every retained sample).
    private static int BracketIndex(IVehicleSampleHistory history, double queryTime)
    {
        var idx = 0;
        for (var i = 0; i < history.Count; i++)
        {
            if (history[i].TimestampSeconds <= queryTime)
            {
                idx = i;
            }
        }

        return idx;
    }

    [Theory]
    [InlineData(4.5, 3)] // between ts=4 (index 3) and ts=5 (index 4) -> bracket at ts=4
    [InlineData(0.0, 0)] // below the oldest retained sample -> clamps to index 0
    [InlineData(99.0, 4)] // above the newest sample -> the newest index
    public void BracketScan_FindsExpectedIndex(double queryTime, int expectedIndex)
    {
        var history = new VehicleSampleHistory(capacity: 8);
        for (var t = 1; t <= 5; t++)
        {
            history.Append(SampleAt(t));
        }

        Assert.Equal(expectedIndex, BracketIndex(history, queryTime));
    }
}
