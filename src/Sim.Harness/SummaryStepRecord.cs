namespace Sim.Harness;

/// <summary>
/// VB-6: one per-step row from either a real SUMO <c>--summary-output</c> file or the engine's
/// summary ANALOG (<see cref="SummaryOutputParser"/> reads both through the same schema subset).
/// <c>MeanSpeed</c>/<c>MeanSpeedRelative</c> are <c>null</c> when the source recorded SUMO's
/// "-1.00" not-applicable sentinel (no on-road, non-stopped vehicles that step) -- callers must
/// exclude null steps from a mean-speed average rather than treat -1 as a real speed sample.
///
/// P0-D: <c>Halting</c>/<c>Stopped</c>/<c>MeanSpeedRelative</c> added for the summary/statistic
/// writer rung (see docs/HIGH-DENSITY-P0-DESIGN.md "P0-D") -- optional/defaulted so every existing
/// positional 4-arg construction (Sim.BenchCity's VB-7 analog, this file's own pre-P0-D callers)
/// stays source-compatible.
/// </summary>
public sealed record SummaryStepRecord(
    double Time,
    int Running,
    int Arrived,
    double? MeanSpeed,
    int Halting = 0,
    int Stopped = 0,
    double? MeanSpeedRelative = null);
