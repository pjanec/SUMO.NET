namespace Sim.Ingest;

// Resolved subset of a .sumocfg needed to drive the engine loop. Integration method is a
// config flag, not a baked-in choice (DESIGN.md), so Ballistic/Euler is carried explicitly.
// C1-i: Seed is the sumocfg's <random_number><seed value="..."/></random_number> (SUMO's global
// RNG seed, e.g. RandHelper::initRandGlobal); parsed here for completeness/future ensemble-
// harness use (TASKS.md C1-ii/C1-iii). Not auto-applied to Engine.Seed by LoadScenario -- see
// Engine.Seed's own header comment for why that stays the single, caller-controlled source of
// truth for the per-entity dawdle RNG instead.
public sealed record ScenarioConfig(
    double Begin,
    double End,
    double StepLength,
    bool Ballistic,
    double TimeToTeleport,
    double ActionStepLength,
    double SpeedDev,
    int Seed);
