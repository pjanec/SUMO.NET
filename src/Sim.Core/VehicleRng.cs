namespace Sim.Core;

// C1-i (TASKS.md "Statistical parity / driver imperfection"): a deterministic, per-entity
// PRNG for Krauss dawdling (KraussModel's dawdle2 port -- see FinalizeSpeed in KraussModel.cs).
//
// OWNER DECISION (TASKS.md C1): the statistical parity bar is ENSEMBLE/AGGREGATE, not
// RNG-exact. We deliberately do NOT reproduce SUMO's RandHelper/MT19937 per-vehicle stream
// (brittle, version-dependent, and it fights the ECS parallelism). This struct is OUR RNG --
// the dawdle ALGORITHM is ported faithfully from MSCFModel_Krauss.cpp, but the random STREAM
// feeding it is not required (and not attempted) to match SUMO bit-for-bit. What IS required
// (CLAUDE.md "no System.Random"; D8's UseParallelPlan parallel-safety argument) is that:
//   (a) results are fully deterministic given (globalSeed, entityIndex) and the draw count, and
//   (b) each entity's stream is independent of every other entity's and of thread/scheduling
//       order -- i.e. no shared/global RNG instance, ever.
//
// Algorithm: SplitMix64 (Sebastiano Vigna, public domain -- https://prng.di.unimi.it/splitmix64.c),
// a well-known, fast, good-quality 64-bit PRNG. Chosen because it is a single unmanaged `ulong`
// of state (fits VehicleRuntime's D3 "unmanaged scalars/structs only" posture -- see that file's
// header comment -- with no managed `System.Random` object per vehicle) and needs no separate
// seeding routine beyond mixing the seed into the state word itself.
public struct VehicleRng
{
    private ulong _state;

    public VehicleRng(ulong state)
    {
        _state = state;
    }

    // Per-entity seeding: mixes the engine's global seed with this vehicle's stable EntityIndex
    // (Engine.LoadScenario assigns EntityIndex once, in vehicle-definition order -- see
    // VehicleRuntime.EntityIndex's own comment) through ONE SplitMix64 step, rather than a bare
    // XOR (globalSeed ^ entityIndex would give adjacent vehicles near-identical initial states,
    // differing by a single low bit, which is bad for a PRNG's early output). The multiplier
    // reused for the pre-mix is SplitMix64's own golden-ratio increment constant
    // (0x9E3779B97F4A7C15, an odd 64-bit constant with good avalanche properties under XOR-
    // multiply mixing) -- convenient and well-known, not itself load-bearing.
    public static VehicleRng SeedFor(ulong globalSeed, int entityIndex)
    {
        unchecked
        {
            var mixedSeed = globalSeed ^ ((ulong)(uint)entityIndex * 0x9E3779B97F4A7C15UL);
            return new VehicleRng(SplitMix64Next(ref mixedSeed));
        }
    }

    // C7-i (TASKS.md "speedFactor distribution"): a SALTED variant of SeedFor, used to derive a
    // SECOND, fully independent per-entity stream from the same (globalSeed, entityIndex) pair --
    // e.g. the once-at-creation speedFactor draw (Engine.LoadScenario / NormcDistribution) must
    // NEVER share state with (or advance) VehicleRuntime.RngState, C1's per-step dawdle stream,
    // even though both derive from the same Engine.Seed. XOR-ing a distinct, caller-supplied
    // `salt` into globalSeed BEFORE the entityIndex mix below guarantees the two streams start
    // from different SplitMix64 states for every entityIndex (the salt shifts every subsequent
    // output), so a bug that accidentally called the wrong overload would be caught by
    // RungC7SpeedFactorTests' independence test (divergent dawdle sequence) rather than silently
    // aliasing streams.
    public static VehicleRng SeedFor(ulong globalSeed, int entityIndex, ulong salt)
    {
        unchecked
        {
            var mixedSeed = (globalSeed ^ salt) ^ ((ulong)(uint)entityIndex * 0x9E3779B97F4A7C15UL);
            return new VehicleRng(SplitMix64Next(ref mixedSeed));
        }
    }

    // Draws the next uniform double in [0,1) -- matches dawdle2's own comment ("generate random
    // number out of [0,1)") for MSCFModel_Krauss.cpp's `RandHelper::rand(rng)` call. Advances
    // this instance's private state by exactly one SplitMix64 step per call (`ref this` field
    // mutation) -- callers must hold the RNG `by ref` (see VehicleRuntime.RngState / Engine's
    // FinalizeSpeed call site) for the draw to persist across steps.
    public double NextDouble()
    {
        var z = SplitMix64Next(ref _state);
        // Standard technique for a uniform double from a 64-bit int: take the top 53 bits
        // (a double's mantissa width) and scale into [0,1).
        return (z >> 11) * (1.0 / (1UL << 53));
    }

    private static ulong SplitMix64Next(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
