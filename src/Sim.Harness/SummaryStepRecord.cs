namespace Sim.Harness;

/// <summary>
/// VB-6: one per-step row from either a real SUMO <c>--summary-output</c> file or the engine's
/// summary ANALOG (<see cref="SummaryOutputParser"/> reads both through the same schema subset).
/// <c>MeanSpeed</c> is <c>null</c> when the source recorded SUMO's "-1.00" not-applicable
/// sentinel (no vehicles running that step) -- callers must exclude null steps from a mean-speed
/// average rather than treat -1 as a real speed sample.
/// </summary>
public sealed record SummaryStepRecord(
    double Time,
    int Running,
    int Arrived,
    double? MeanSpeed);
