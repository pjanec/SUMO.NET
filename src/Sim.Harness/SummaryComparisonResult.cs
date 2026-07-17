namespace Sim.Harness;

/// <summary>
/// P0-D: result of <see cref="SummaryComparator.Compare"/>, mirroring <see
/// cref="ComparisonResult"/>'s shape (per-attribute max-abs/RMSE + first-divergence step) but over
/// a <see cref="SummaryStepRecord"/> step-series rather than a per-(vehicle,time) trajectory --
/// there is no vehicle identity here, so "presence" mismatches are just step TIMES present in one
/// series but not the other (<see cref="MissingSteps"/>/<see cref="ExtraSteps"/>), not per-vehicle
/// entries.
/// </summary>
public sealed class SummaryComparisonResult
{
    public required IReadOnlyList<AttributeComparisonResult> Attributes { get; init; }
    public required IReadOnlyList<double> MissingSteps { get; init; }
    public required IReadOnlyList<double> ExtraSteps { get; init; }
    public required double? FirstDivergenceStep { get; init; }

    public bool IsMatch =>
        MissingSteps.Count == 0
        && ExtraSteps.Count == 0
        && Attributes.All(a => a.WithinTolerance);
}
