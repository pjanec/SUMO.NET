namespace Sim.Harness;

/// <summary>
/// VB-6: one aggregate metric's reference-vs-candidate comparison (arrived count / mean duration /
/// mean speed), mirroring the reporting shape of <see cref="AttributeComparisonResult"/> so a
/// failing benchmark run prints an equally useful diagnostic.
/// </summary>
public sealed record AggregateMetricResult(
    string Metric,
    double Reference,
    double Candidate,
    double RelError,
    double Tolerance,
    bool WithinTolerance);

/// <summary>
/// VB-6: result of <see cref="AggregateComparator.Compare"/> -- statistical/aggregate agreement
/// between an engine run and a SUMO reference run, NOT vehicle-for-vehicle parity (that is
/// <see cref="ComparisonResult"/>'s job). Four checks: total arrived, mean trip duration, mean
/// network speed (each a <see cref="AggregateMetricResult"/>), and the trip-duration distribution
/// distance (a two-sample KS statistic, its own tolerance).
/// </summary>
public sealed class AggregateComparisonResult
{
    public required AggregateMetricResult Arrived { get; init; }
    public required AggregateMetricResult MeanDuration { get; init; }
    public required AggregateMetricResult MeanSpeed { get; init; }
    public required double DistributionDistance { get; init; }
    public required double DistributionDistanceTolerance { get; init; }

    public bool DistributionWithinTolerance => DistributionDistance <= DistributionDistanceTolerance;

    public bool IsMatch =>
        Arrived.WithinTolerance
        && MeanDuration.WithinTolerance
        && MeanSpeed.WithinTolerance
        && DistributionWithinTolerance;
}
