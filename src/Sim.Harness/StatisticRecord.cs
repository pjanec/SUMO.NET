namespace Sim.Harness;

/// <summary>
/// P0-D: the <c>&lt;teleports total= jam= yield= wrongLane=/&gt;</c> subset of a SUMO
/// <c>--statistic-output</c> file's <c>&lt;statistics&gt;</c> root (docs/HIGH-DENSITY-P0-DESIGN.md
/// "P0-D"). Phase 1 runs teleport-off (CLAUDE.md "Determinism (phase 1)"), so every committed
/// golden has all four at 0; P1-F is the only future rung expected to make <c>Total</c> nonzero.
/// </summary>
public sealed record StatisticRecord(int TeleportsTotal, int TeleportsJam, int TeleportsYield, int TeleportsWrongLane);
