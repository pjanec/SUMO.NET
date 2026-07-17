namespace Sim.Pedestrians.Crossing;

// A deterministic phase-clock ICrossingSignal whose period and walk/don't-walk windows mirror the
// REAL <tlLogic> program driving the junction (read via CrossingTlReader), evaluated at `linkIndex`
// -- this IS the "fallback deterministic phase clock ICrossingSignal ... whose period mirrors the
// tlLogic's phase durations (read from the net's <tlLogic>)" POC-2 calls for
// (docs/PEDESTRIAN-POC-PLAN.md POC-2), because Engine.TlLaneHandles/TlStates does not cover
// pedestrian crossing links at all (see CrossingSignalFactory's remarks for why) -- but built from
// the ACTUAL phase durations and state string rather than a guessed/simplified period, so the
// walk/don't-walk timing it produces is exactly what the live Engine computes for this (or any other)
// 'static' tlLogic program.
//
// SUMO's pedestrian-crossing convention uses only 'G' (walk) / 'r' (don't walk) at a crossing's entry
// link -- no transitional 'g'/'y' the way vehicle links get (verified against POC-0's net.net.xml: the
// crossing link indices 20-23 are 'G' or 'r' in every phase, never 'g' or 'y') -- so WalkAllowed is
// exactly `StateAt(now) == 'G'`.
//
// The phase walk mirrors Sim.Core.TrafficLightState.GetLinkState's algorithm (a static tlLogic
// program cycles through phases in declared order, holding each for its duration, restarting at
// offset + cycle) but is reimplemented here, independently, rather than called -- Sim.Pedestrians must
// not reference Sim.Ingest / Sim.Core's parity TrafficLightState (CrossingTlReader's remarks). See
// CrossingTlReaderTests for a cross-check against the net's hand-verified phase table.
public sealed class TlProgramCrossingSignal : ICrossingSignal
{
    private readonly TlProgramSpec _program;
    private readonly int _linkIndex;

    public TlProgramCrossingSignal(TlProgramSpec program, int linkIndex)
    {
        if (program.Phases.Count == 0)
        {
            throw new ArgumentException("tlLogic program has no phases.", nameof(program));
        }

        _program = program;
        _linkIndex = linkIndex;
    }

    public bool WalkAllowed(double now) => StateAt(now) == 'G';

    // The raw signal character at `now` (exposed for tests / observability -- e.g. distinguishing
    // 'r' don't-walk from a would-be 'y' clearance phase, even though this scenario never emits 'y'
    // at a crossing link).
    public char StateAt(double now)
    {
        var cycleLength = _program.CycleLength;
        var position = (now - _program.Offset) % cycleLength;
        if (position < 0)
        {
            // Defensive only (mirrors Sim.Core.TrafficLightState.GetLinkState's own note): C#'s %
            // can return a negative remainder where SUMOTime's integer modulo would not.
            position += cycleLength;
        }

        var cumulative = 0.0;
        foreach (var phase in _program.Phases)
        {
            cumulative += phase.Duration;
            if (position < cumulative)
            {
                return phase.State[_linkIndex];
            }
        }

        // Only reachable via floating-point rounding right at the cycle boundary.
        return _program.Phases[^1].State[_linkIndex];
    }
}
