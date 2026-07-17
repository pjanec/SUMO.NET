namespace Sim.Pedestrians.Crossing;

// POC-2 (docs/PEDESTRIAN-POC-PLAN.md POC-2; docs/PEDESTRIAN-DESIGN.md §6, decision D5): the crosswalk
// gate's walk/don't-walk source. Deliberately the smallest possible seam -- a pure function of the
// caller's own clock -- so CrossingGate never needs to know WHERE the signal comes from (a live
// junction program, a scripted demo clock, or a test double).
public interface ICrossingSignal
{
    // True when pedestrians may walk onto the gated crossing at absolute time `now`.
    bool WalkAllowed(double now);
}

// Trivial always-open / always-closed signals, useful for tests and for an unsignalized crossing
// (PedCrossing.TlLogicId == null) that should never hold pedestrians.
public sealed class AlwaysWalkSignal : ICrossingSignal
{
    public static readonly AlwaysWalkSignal Instance = new();
    public bool WalkAllowed(double now) => true;
}

public sealed class NeverWalkSignal : ICrossingSignal
{
    public static readonly NeverWalkSignal Instance = new();
    public bool WalkAllowed(double now) => false;
}
