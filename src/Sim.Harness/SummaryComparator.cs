namespace Sim.Harness;

/// <summary>
/// P0-D (docs/HIGH-DENSITY-P0-DESIGN.md "P0-D"): compares an engine-produced <c>--summary-output</c>
/// step-series against a SUMO golden's, mirroring <see cref="TrajectoryComparator"/>'s shape
/// (per-attribute max-abs/RMSE, first-divergence step) but keyed by step TIME rather than
/// (vehicle, time), since a summary step has no per-vehicle identity.
///
/// Per docs/HIGH-DENSITY-P0-DESIGN.md's P0-D success conditions: the integer counts
/// (<c>running</c>/<c>halting</c>/<c>stopped</c>) must match EXACTLY (tolerance 0) -- these come
/// straight off <c>ActiveVehicles()</c>/halting-speed/stop-state counts with no floating-point
/// accumulation, so any difference is a real behavioral divergence, not rounding noise. The float
/// means (<c>meanSpeed</c>/<c>meanSpeedRelative</c>) get the 1e-3 default tolerance also used
/// elsewhere in this repo for speed/pos parity.
/// </summary>
public static class SummaryComparator
{
    public const double DefaultMeanTolerance = 1e-3;

    private static readonly string[] Attributes = { "running", "halting", "stopped", "meanSpeed", "meanSpeedRelative" };

    public static SummaryComparisonResult Compare(
        IReadOnlyList<SummaryStepRecord> actual,
        IReadOnlyList<SummaryStepRecord> expected,
        double meanSpeedTolerance = DefaultMeanTolerance,
        double meanSpeedRelativeTolerance = DefaultMeanTolerance)
    {
        double ToleranceFor(string attribute) => attribute switch
        {
            "meanSpeed" => meanSpeedTolerance,
            "meanSpeedRelative" => meanSpeedRelativeTolerance,
            _ => 0.0, // running/halting/stopped: exact integer counts.
        };

        // "last wins" on a duplicate time is not expected from either a real SUMO file or the
        // engine's own per-frame writer (one <step> per sim step), but a plain dictionary keyed
        // by Time is the simplest correct join here -- mirrors how TrajectoryComparator joins on
        // (vehicleId, time) via a keyed lookup rather than an assumed 1:1 ordinal zip.
        var actualByTime = new Dictionary<double, SummaryStepRecord>();
        foreach (var s in actual)
        {
            actualByTime[s.Time] = s;
        }

        var expectedByTime = new Dictionary<double, SummaryStepRecord>();
        foreach (var s in expected)
        {
            expectedByTime[s.Time] = s;
        }

        var missingSteps = expectedByTime.Keys.Except(actualByTime.Keys).OrderBy(t => t).ToList();
        var extraSteps = actualByTime.Keys.Except(expectedByTime.Keys).OrderBy(t => t).ToList();
        var commonTimes = actualByTime.Keys.Intersect(expectedByTime.Keys).OrderBy(t => t).ToList();

        var errorsByAttribute = Attributes.ToDictionary(a => a, _ => new List<double>());
        double? firstDivergence = null;

        foreach (var time in commonTimes)
        {
            var a = actualByTime[time];
            var e = expectedByTime[time];

            foreach (var attribute in Attributes)
            {
                var error = ComputeError(attribute, a, e);
                errorsByAttribute[attribute].Add(error);

                if (firstDivergence is null && error > ToleranceFor(attribute))
                {
                    firstDivergence = time;
                }
            }
        }

        var attributeResults = Attributes.Select(attribute =>
        {
            var errors = errorsByAttribute[attribute];
            var maxAbs = errors.Count > 0 ? errors.Max() : 0.0;
            var rmse = errors.Count > 0 ? Math.Sqrt(errors.Average(e => e * e)) : 0.0;
            return new AttributeComparisonResult(attribute, maxAbs, rmse, maxAbs <= ToleranceFor(attribute));
        }).ToList();

        return new SummaryComparisonResult
        {
            Attributes = attributeResults,
            MissingSteps = missingSteps,
            ExtraSteps = extraSteps,
            FirstDivergenceStep = firstDivergence,
        };
    }

    private static double ComputeError(string attribute, SummaryStepRecord actual, SummaryStepRecord expected) => attribute switch
    {
        "running" => Math.Abs(actual.Running - expected.Running),
        "halting" => Math.Abs(actual.Halting - expected.Halting),
        "stopped" => Math.Abs(actual.Stopped - expected.Stopped),
        "meanSpeed" => NullableError(actual.MeanSpeed, expected.MeanSpeed),
        "meanSpeedRelative" => NullableError(actual.MeanSpeedRelative, expected.MeanSpeedRelative),
        _ => throw new ArgumentException($"Unknown comparison attribute '{attribute}'.", nameof(attribute)),
    };

    // MeanSpeed/MeanSpeedRelative are null when the SUMO "-1" not-applicable sentinel applied (no
    // on-road, non-stopped vehicles that step). null vs null is an exact match (both sentinel);
    // null vs a real value is a hard divergence (+Infinity, guaranteed outside any finite
    // tolerance) rather than silently comparing -1 against a real speed sample.
    private static double NullableError(double? actual, double? expected)
    {
        if (actual is null && expected is null)
        {
            return 0.0;
        }

        if (actual is null || expected is null)
        {
            return double.PositiveInfinity;
        }

        return Math.Abs(actual.Value - expected.Value);
    }
}
