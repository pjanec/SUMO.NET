using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// P0-D (docs/HIGH-DENSITY-P0-DESIGN.md "P0-D"): pure/offline unit tests for the new
// SummaryOutputParser attributes (halting/stopped/meanSpeedRelative), StatisticOutputParser, and
// SummaryComparator, against synthetic inline XML/records -- no engine, no SUMO, no network. The
// end-to-end engine-vs-golden acceptance check is RungHDp0dSummaryOutputParityTests
// (scenarios/44-summary-output).
public class RungHDp0dSummaryStatisticUnitTests
{
    [Fact]
    public void SummaryOutputParser_ReadsHaltingStoppedAndMeanSpeedRelative()
    {
        var xml = """
            <summary>
                <step time="41.000" running="5" arrived="0" halting="1" stopped="1"
                      meanSpeed="11.836167" meanSpeedRelative="0.852136"/>
            </summary>
            """;

        var records = SummaryOutputParser.ParseXml(xml);

        var step = Assert.Single(records);
        Assert.Equal(41.0, step.Time);
        Assert.Equal(5, step.Running);
        Assert.Equal(1, step.Halting);
        Assert.Equal(1, step.Stopped);
        Assert.Equal(11.836167, step.MeanSpeed);
        Assert.Equal(0.852136, step.MeanSpeedRelative);
    }

    [Fact]
    public void SummaryOutputParser_MeanSpeedRelativeSentinel_ParsesAsNull()
    {
        var xml = """
            <summary>
                <step time="0.000" running="1" arrived="0" halting="1" stopped="0"
                      meanSpeed="-1.000000" meanSpeedRelative="-1.000000"/>
            </summary>
            """;

        var step = Assert.Single(SummaryOutputParser.ParseXml(xml));

        Assert.Null(step.MeanSpeed);
        Assert.Null(step.MeanSpeedRelative);
    }

    // Pre-P0-D schema (no halting/stopped/meanSpeedRelative attributes at all) must still parse --
    // extension is additive, not a breaking schema change.
    [Fact]
    public void SummaryOutputParser_OmittedNewAttributes_DefaultToZeroAndNull()
    {
        var xml = """<summary><step time="1.00" running="2" arrived="0" meanSpeed="9.50"/></summary>""";

        var step = Assert.Single(SummaryOutputParser.ParseXml(xml));

        Assert.Equal(0, step.Halting);
        Assert.Equal(0, step.Stopped);
        Assert.Null(step.MeanSpeedRelative);
        Assert.Equal(9.50, step.MeanSpeed);
    }

    [Fact]
    public void StatisticOutputParser_ReadsTeleportsElement()
    {
        var xml = """
            <statistics>
                <performance begin="0.000" end="100.000"/>
                <teleports total="3" jam="1" yield="2" wrongLane="0"/>
            </statistics>
            """;

        var record = StatisticOutputParser.ParseXml(xml);

        Assert.Equal(3, record.TeleportsTotal);
        Assert.Equal(1, record.TeleportsJam);
        Assert.Equal(2, record.TeleportsYield);
        Assert.Equal(0, record.TeleportsWrongLane);
    }

    [Fact]
    public void StatisticOutputParser_ZeroTeleports_MatchesPhase1Golden()
    {
        var xml = """<statistics><teleports total="0" jam="0" yield="0" wrongLane="0"/></statistics>""";

        var record = StatisticOutputParser.ParseXml(xml);

        Assert.Equal(0, record.TeleportsTotal);
    }

    [Fact]
    public void SummaryComparator_IdenticalSeries_Passes()
    {
        var series = new[]
        {
            new SummaryStepRecord(0.0, Running: 1, Arrived: 0, MeanSpeed: 0.0, Halting: 1, Stopped: 0, MeanSpeedRelative: 0.0),
            new SummaryStepRecord(1.0, Running: 1, Arrived: 0, MeanSpeed: 2.6, Halting: 0, Stopped: 0, MeanSpeedRelative: 0.187185),
            new SummaryStepRecord(2.0, Running: 5, Arrived: 0, MeanSpeed: null, Halting: 5, Stopped: 1, MeanSpeedRelative: null),
        };

        var result = SummaryComparator.Compare(series, series);

        Assert.True(result.IsMatch);
        Assert.Null(result.FirstDivergenceStep);
        Assert.Empty(result.MissingSteps);
        Assert.Empty(result.ExtraSteps);
    }

    // A deliberate integer-count mismatch (halting off by one) must be caught EXACTLY -- P0-D's
    // running/halting/stopped counts get zero tolerance, unlike the float means.
    [Fact]
    public void SummaryComparator_HaltingCountMismatch_FailsExactly()
    {
        var expected = new[] { new SummaryStepRecord(0.0, Running: 5, Arrived: 0, MeanSpeed: 10.0, Halting: 2, Stopped: 0, MeanSpeedRelative: 0.7) };
        var actual = new[] { new SummaryStepRecord(0.0, Running: 5, Arrived: 0, MeanSpeed: 10.0, Halting: 3, Stopped: 0, MeanSpeedRelative: 0.7) };

        var result = SummaryComparator.Compare(actual, expected);

        Assert.False(result.IsMatch);
        Assert.Equal(0.0, result.FirstDivergenceStep);
        var halting = result.Attributes.Single(a => a.Attribute == "halting");
        Assert.False(halting.WithinTolerance);
        Assert.Equal(1.0, halting.MaxAbsError);
    }

    // A meanSpeed difference within 1e-3 passes; outside it fails -- the float means get a real
    // (non-zero) tolerance, unlike the exact integer counts.
    [Fact]
    public void SummaryComparator_MeanSpeedWithinTolerance_Passes_OutsideTolerance_Fails()
    {
        var expected = new[] { new SummaryStepRecord(0.0, Running: 1, Arrived: 0, MeanSpeed: 10.0, MeanSpeedRelative: 0.5) };

        var withinTol = new[] { new SummaryStepRecord(0.0, Running: 1, Arrived: 0, MeanSpeed: 10.0005, MeanSpeedRelative: 0.5) };
        Assert.True(SummaryComparator.Compare(withinTol, expected).IsMatch);

        var outsideTol = new[] { new SummaryStepRecord(0.0, Running: 1, Arrived: 0, MeanSpeed: 10.1, MeanSpeedRelative: 0.5) };
        var result = SummaryComparator.Compare(outsideTol, expected);
        Assert.False(result.IsMatch);
        Assert.False(result.Attributes.Single(a => a.Attribute == "meanSpeed").WithinTolerance);
    }

    // -1 sentinel (null) vs -1 sentinel (null) is an exact pass, not a spurious divergence.
    [Fact]
    public void SummaryComparator_BothMeanSpeedSentinel_Passes()
    {
        var series = new[] { new SummaryStepRecord(0.0, Running: 0, Arrived: 0, MeanSpeed: null, MeanSpeedRelative: null) };

        Assert.True(SummaryComparator.Compare(series, series).IsMatch);
    }

    // sentinel (null) vs a real value is a hard divergence, not silently treated as a near-zero
    // numeric difference.
    [Fact]
    public void SummaryComparator_SentinelVsRealValue_Fails()
    {
        var expected = new[] { new SummaryStepRecord(0.0, Running: 1, Arrived: 0, MeanSpeed: 5.0) };
        var actual = new[] { new SummaryStepRecord(0.0, Running: 1, Arrived: 0, MeanSpeed: null) };

        var result = SummaryComparator.Compare(actual, expected);

        Assert.False(result.IsMatch);
        Assert.False(result.Attributes.Single(a => a.Attribute == "meanSpeed").WithinTolerance);
    }

    [Fact]
    public void SummaryComparator_MissingAndExtraSteps_ReportedAndFailMatch()
    {
        var expected = new[]
        {
            new SummaryStepRecord(0.0, Running: 1, Arrived: 0, MeanSpeed: 5.0),
            new SummaryStepRecord(1.0, Running: 1, Arrived: 0, MeanSpeed: 5.0),
        };
        var actual = new[]
        {
            new SummaryStepRecord(0.0, Running: 1, Arrived: 0, MeanSpeed: 5.0),
            new SummaryStepRecord(2.0, Running: 1, Arrived: 0, MeanSpeed: 5.0),
        };

        var result = SummaryComparator.Compare(actual, expected);

        Assert.False(result.IsMatch);
        Assert.Equal(new[] { 1.0 }, result.MissingSteps);
        Assert.Equal(new[] { 2.0 }, result.ExtraSteps);
    }
}
