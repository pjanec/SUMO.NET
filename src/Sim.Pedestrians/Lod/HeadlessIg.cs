using Sim.Core.Orca;

namespace Sim.Pedestrians.Lod;

// A headless "IG" (docs/PEDESTRIAN-POC-PLAN.md POC-3): consumes a PedPublisher's event stream and
// reconstructs each ped's pose with NO access to server-internal state -- only what has been Applied.
// This is the receiving half of the "server == IG for low-power" identity (docs/PEDESTRIAN-DESIGN.md
// §8): its PathArc branch calls the exact same PathArcMotion.PositionAt the server calls, so the two
// can only ever agree, never merely "usually match".
public sealed class HeadlessIg
{
    private sealed class PedState
    {
        public PedDrModel Model = PedDrModel.PathArc;
        public IReadOnlyList<Vec2>? Path;
        public double PathStartTime;
        public double Speed;
        public Vec2 LastPos;
        public Vec2 LastVel;
        public double LastSampleTime;
    }

    private readonly Dictionary<int, PedState> _peds = new();

    // Feed one event (in wire order) into the IG's model of the world.
    public void Apply(PedEvent evt)
    {
        var state = GetOrCreate(evt.Id);
        switch (evt)
        {
            case PathArcRecord r:
                state.Path = r.Path;
                state.PathStartTime = r.StartTime;
                state.Speed = r.Speed;
                break;

            case DrSwitchEvent s:
                state.Model = s.To;
                break;

            case FreeKinematicSample f:
                state.LastPos = f.Pos;
                state.LastVel = f.Vel;
                state.LastSampleTime = f.Time;
                break;

            case HeartbeatEvent:
                break; // liveness only -- no pose information
        }
    }

    // Convenience for tests: feed a whole (ordered) event batch at once.
    public void ApplyAll(IEnumerable<PedEvent> events)
    {
        foreach (var evt in events)
        {
            Apply(evt);
        }
    }

    // Reconstructs id's world position at `now`, using ONLY what has been Applied so far.
    public Vec2 Reconstruct(int id, double now)
    {
        var state = _peds[id];
        return state.Model switch
        {
            PedDrModel.PathArc => state.Path is null
                ? Vec2.Zero
                : PathArcMotion.PositionAt(state.Path, state.PathStartTime, state.Speed, now),
            PedDrModel.FreeKinematic => state.LastPos + (state.LastVel * (now - state.LastSampleTime)),
            PedDrModel.Stationary => state.LastPos,
            _ => Vec2.Zero,
        };
    }

    // The IG's current belief about id's DR model (asserting a promotion/demotion was actually
    // *observed* on the wire, not just true on the server).
    public PedDrModel ModelOf(int id) => _peds[id].Model;

    public bool Knows(int id) => _peds.ContainsKey(id);

    private PedState GetOrCreate(int id)
    {
        if (!_peds.TryGetValue(id, out var state))
        {
            state = new PedState();
            _peds[id] = state;
        }

        return state;
    }
}
