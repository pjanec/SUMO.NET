namespace Sim.Harness;

/// <summary>
/// VB-6 (VIZ_BENCH_TASKS.md Phase 2 / BENCHMARK_SPEC.md "Statistical agreement with SUMO"): a NEW
/// comparator for AGGREGATE engine-vs-SUMO agreement on a scaled-city benchmark rung. Deliberately
/// NOT <see cref="TrajectoryComparator"/> -- that one matches individual (vehicle, time) points
/// to 1e-3 and is the wrong tool here: a 15k-vehicle synthetic city with its own RNG stream will
/// never produce the same trajectories as SUMO, nor should it need to. This comparator instead
/// asks "does the engine produce the same KIND of traffic": total vehicles arrived, mean trip
/// duration, mean network speed, and the shape of the trip-duration distribution (a two-sample
/// Kolmogorov-Smirnov statistic -- the largest gap between the two empirical CDFs, 0 = identical
/// distributions, 1 = maximally different).
///
/// Pure/offline: takes already-parsed <see cref="TripInfoRecord"/>/<see cref="SummaryStepRecord"/>
/// lists (from <see cref="TripInfoParser"/>/<see cref="SummaryOutputParser"/>, which read the SAME
/// schema subset from either a real SUMO output or the engine's VB-7 analog) -- no SUMO, no
/// process spawning, no network. That is what makes VB-6's own tests safe to run inside
/// `dotnet test` even though the wider Phase-2 benchmark pipeline is not.
/// </summary>
public static class AggregateComparator
{
    public static AggregateComparisonResult Compare(
        IReadOnlyList<TripInfoRecord> referenceTrips,
        IReadOnlyList<SummaryStepRecord> referenceSummary,
        IReadOnlyList<TripInfoRecord> candidateTrips,
        IReadOnlyList<SummaryStepRecord> candidateSummary,
        AggregateToleranceConfig tolerance)
    {
        var arrived = MetricResult(
            "arrived",
            referenceTrips.Count,
            candidateTrips.Count,
            tolerance.ArrivedRelTol);

        var meanDuration = MetricResult(
            "meanDuration",
            MeanDuration(referenceTrips),
            MeanDuration(candidateTrips),
            tolerance.MeanDurationRelTol);

        var meanSpeed = MetricResult(
            "meanSpeed",
            MeanSpeed(referenceSummary),
            MeanSpeed(candidateSummary),
            tolerance.MeanSpeedRelTol);

        var distributionDistance = KsStatistic(
            referenceTrips.Select(t => t.Duration).ToList(),
            candidateTrips.Select(t => t.Duration).ToList());

        return new AggregateComparisonResult
        {
            Arrived = arrived,
            MeanDuration = meanDuration,
            MeanSpeed = meanSpeed,
            DistributionDistance = distributionDistance,
            DistributionDistanceTolerance = tolerance.DistributionDistanceTol,
        };
    }

    private static AggregateMetricResult MetricResult(string name, double reference, double candidate, double tol)
    {
        var relError = RelError(reference, candidate);
        return new AggregateMetricResult(name, reference, candidate, relError, tol, relError <= tol);
    }

    private static double RelError(double reference, double candidate)
    {
        if (reference == 0.0)
        {
            return candidate == 0.0 ? 0.0 : double.PositiveInfinity;
        }

        return Math.Abs(candidate - reference) / Math.Abs(reference);
    }

    private static double MeanDuration(IReadOnlyList<TripInfoRecord> trips) =>
        trips.Count > 0 ? trips.Average(t => t.Duration) : 0.0;

    private static double MeanSpeed(IReadOnlyList<SummaryStepRecord> steps)
    {
        var speeds = steps.Where(s => s.MeanSpeed.HasValue).Select(s => s.MeanSpeed!.Value).ToList();
        return speeds.Count > 0 ? speeds.Average() : 0.0;
    }

    /// <summary>
    /// Two-sample Kolmogorov-Smirnov statistic: sup_x |F_ref(x) - F_cand(x)| over the pooled,
    /// sorted sample values of both sets, where F is each set's empirical CDF. 0 when both sets
    /// are empty (vacuously equal); 1 when exactly one set is empty and the other is not (maximal
    /// divergence -- one distribution has no support where the other has all of it).
    /// </summary>
    private static double KsStatistic(IReadOnlyList<double> reference, IReadOnlyList<double> candidate)
    {
        if (reference.Count == 0 && candidate.Count == 0)
        {
            return 0.0;
        }

        if (reference.Count == 0 || candidate.Count == 0)
        {
            return 1.0;
        }

        var refSorted = reference.OrderBy(v => v).ToArray();
        var candSorted = candidate.OrderBy(v => v).ToArray();

        var allValues = new double[refSorted.Length + candSorted.Length];
        Array.Copy(refSorted, allValues, refSorted.Length);
        Array.Copy(candSorted, 0, allValues, refSorted.Length, candSorted.Length);
        Array.Sort(allValues);

        var maxDiff = 0.0;
        foreach (var x in allValues)
        {
            var refCdf = CountLessOrEqual(refSorted, x) / (double)refSorted.Length;
            var candCdf = CountLessOrEqual(candSorted, x) / (double)candSorted.Length;
            var diff = Math.Abs(refCdf - candCdf);
            if (diff > maxDiff)
            {
                maxDiff = diff;
            }
        }

        return maxDiff;
    }

    // refSorted/candSorted are sorted ascending -- binary search for the upper bound of x.
    private static int CountLessOrEqual(double[] sortedValues, double x)
    {
        var lo = 0;
        var hi = sortedValues.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sortedValues[mid] <= x)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
