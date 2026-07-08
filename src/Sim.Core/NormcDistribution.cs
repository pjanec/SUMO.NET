namespace Sim.Core;

// C7-i (TASKS.md "speedFactor distribution (heterogeneous desired speeds)"): the "normc"
// parameterized distribution SUMO's per-vehicle speedFactor is drawn from, ported from:
//   sumo/src/utils/distribution/Distribution_Parameterized.cpp:107-120  sample()
//   sumo/src/utils/common/RandHelper.cpp:137-147                        randNorm()
//   sumo/src/utils/common/RandHelper.h:132-134                         rand(double,rng) ==
//       maxV * rand(rng) (a uniform draw in [0,1) scaled to [0,maxV))
//   sumo/src/microsim/MSVehicleType.cpp:89-91                          computeChosenSpeedDeviation
//   sumo/src/utils/common/StdDefs.cpp:52-56, :28                       roundDecimal / gPrecisionRandom
//
// OWNER DECISION (TASKS.md C7-i, mirrors C1): the ensemble/aggregate statistical bar means the
// DISTRIBUTION is ported faithfully (mean/dev/clamp semantics, the polar/Marsaglia method, the
// ceil-quantized log term, the reject-resample clamp) but the RNG STREAM feeding it is OURS
// (Sim.Core.VehicleRng / SplitMix64), never SUMO's RandHelper/MT19937 -- see VehicleRng's own
// header comment for the same argument applied to C1's dawdle draw.
//
// CLAUDE.md rule 1 correction: the C7-i briefing's "gPrecisionRandom default = 6" does not match
// the vendored source -- StdDefs.cpp:28 sets `int gPrecisionRandom = 4;`. Ported from what the
// source actually does (4), not the briefing's transcription.
public static class NormcDistribution
{
    // StdDefs.cpp:28 `int gPrecisionRandom = 4;` (never overridden anywhere in the vendored
    // source outside test/option plumbing this port doesn't touch).
    private const int GPrecisionRandom = 4;

    // RandHelper.cpp:137-147 randNorm(mean, variance, rng) -- polar (Marsaglia) method. `variance`
    // is SUMO's own parameter name for what is actually the distribution's std-dev (the caller
    // below passes `dev`, matching Distribution_Parameterized's own naming); kept as `variance`
    // here only to mirror the source's own (slightly misleading) identifier.
    //   do { u = rand(2.0)-1; v = rand(2.0)-1; q = u*u+v*v; } while (q==0 || q>=1);
    //   logRounded = ceil(log(q) * 1e14) / 1e14;
    //   return mean + variance * u * sqrt(-2 * logRounded / q);
    // `rand(2.0, rng)` (RandHelper.h:132-134) == `2.0 * rand(rng)`, a uniform draw in [0, 2.0);
    // `rand(rng)` (the zero-arg uniform-[0,1) draw) is VehicleRng.NextDouble() here (OWNER
    // DECISION above -- not RandHelper::rand's MT19937 stream).
    private static double RandNorm(double mean, double variance, ref VehicleRng rng)
    {
        double u, v, q;
        do
        {
            u = 2.0 * rng.NextDouble() - 1.0;
            v = 2.0 * rng.NextDouble() - 1.0;
            q = u * u + v * v;
        } while (q == 0.0 || q >= 1.0);

        var logRounded = Math.Ceiling(Math.Log(q) * 1e14) / 1e14;
        return mean + variance * u * Math.Sqrt(-2.0 * logRounded / q);
    }

    // Distribution_Parameterized.cpp:107-120 sample() for the "normc" id -- SUMOVTypeParameter's
    // speedFactor is always constructed with 4 params (mean, dev, min, max --
    // SUMOVTypeParameter.cpp:70 `speedFactor("normc", 1.0, 0.0, 0.2, 2.0)`), so this only needs
    // the myParameter.size()>2 (has min/max) branch of the source's sample():
    //   if (dev <= 0.) return mean;                          -- NO draw at all when dev<=0
    //   val = randNorm(mean, dev, rng);
    //   while (val < min || val > max) val = randNorm(mean, dev, rng);   -- reject-resample clamp
    //   return val;
    public static double SampleNormc(double mean, double dev, double min, double max, ref VehicleRng rng)
    {
        if (dev <= 0.0)
        {
            return mean;
        }

        var val = RandNorm(mean, dev, ref rng);
        while (val < min || val > max)
        {
            val = RandNorm(mean, dev, ref rng);
        }

        return val;
    }

    // StdDefs.cpp:52-56 roundDecimal(x, precision) -- round-half-away-from-zero to `precision`
    // decimal places (NOT round-half-to-even/banker's rounding, and NOT a simple Math.Round call
    // whose default MidpointRounding differs from this on ties):
    //   p = 10^precision; x2 = x*p; return (x2<0 ? ceil(x2-0.5) : floor(x2+0.5)) / p;
    public static double RoundDecimal(double x, int precision)
    {
        var p = Math.Pow(10, precision);
        var x2 = x * p;
        return (x2 < 0 ? Math.Ceiling(x2 - 0.5) : Math.Floor(x2 + 0.5)) / p;
    }

    // MSVehicleType.cpp:89-91 computeChosenSpeedDeviation(rng, minDev):
    //   return roundDecimal(MAX2(minDev, speedFactor.sample(rng)), gPrecisionRandom);
    // `minDev` defaults to -1. (MSVehicleType.h:174) at every call site this port exercises
    // (MSVehicleControl.cpp:113's plain vehicle-build path, no explicit minDev argument) -- since
    // the normc distribution's own clamp floor (min=0.2) is always >= -1, MAX2(minDev, sample) is
    // therefore a no-op in the normal (non-departSpeed-driven) case; kept as an explicit parameter
    // rather than hardcoded away so a future MSEdge.cpp:693-style departSpeed-anchored minDev call
    // site (out of C7-i's scope -- see TASKS.md) can reuse this without a second port.
    public static double ComputeChosenSpeedDeviation(
        double mean, double dev, double min, double max, ref VehicleRng rng, double minDev = -1.0)
    {
        var sampled = SampleNormc(mean, dev, min, max, ref rng);
        return RoundDecimal(Math.Max(minDev, sampled), GPrecisionRandom);
    }
}
