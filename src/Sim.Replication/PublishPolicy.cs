using Sim.Core;

namespace Sim.Replication;

// SUMOSHARP-DEADRECKONING.md §7 — the adaptive publish rate is a PLUGGABLE policy (owner decision), not a
// fixed threshold. The publisher calls ShouldPublish per vehicle per candidate frame with the cheap frozen
// signals below; a highly-predictable steady follower is deferred (down to ~1 Hz), an uncertain one
// (braking/accelerating hard, near a leader, mid-manoeuvre, model just switched) is sent at full rate. A
// host can supply its own policy (e.g. weight by camera distance or a bandwidth governor).
public readonly struct PublishSignals
{
    public PublishSignals(
        VehicleHandle handle, DrModel model, double speed, double accel,
        double secondsSinceLastSent, bool laneChangingOrManoeuvring,
        double posError = 0.0, double latError = 0.0, bool laneChanged = false)
    {
        Handle = handle; Model = model; Speed = speed; Accel = accel;
        SecondsSinceLastSent = secondsSinceLastSent; LaneChangingOrManoeuvring = laneChangingOrManoeuvring;
        PosError = posError; LatError = latError; LaneChanged = laneChanged;
    }

    public VehicleHandle Handle { get; }
    public DrModel Model { get; }
    public double Speed { get; }
    public double Accel { get; }
    public double SecondsSinceLastSent { get; }
    public bool LaneChangingOrManoeuvring { get; }
    public double PosError { get; }
    public double LatError { get; }
    public bool LaneChanged { get; }
}

public interface IPublishPolicy
{
    // True to include this vehicle in the current frame; false to keep dead-reckoning it on the client.
    bool ShouldPublish(in PublishSignals s);
}

// The default policy: send at the full rate when the mover is not steady-state predictable, otherwise stretch
// toward a slow keep-alive interval. "Predictable" = a lane-bound mover (LaneArc OR Stationary) with small
// acceleration and not manoeuvring; such a vehicle is re-sent only every SlowInterval. Stationary counts
// because a stopped vehicle is the MOST dead-reckonable regime (zero motion) -- exactly the queue-at-lights
// case where the bandwidth saving matters most; the only cost is a bounded, self-correcting launch-from-stop
// latency (accel-limited, fixed on the next publish). Everything else (FreeKinematic, |accel| over the
// threshold, manoeuvring, or a stale keep-alive) is sent whenever it is at least FastInterval since the last
// send. (Confirmed with the laneless branch, issue #4.)
public sealed class DefaultPublishPolicy : IPublishPolicy
{
    public double FastInterval { get; init; } = 0.1;   // 10 Hz for uncertain movers
    public double SlowInterval { get; init; } = 1.0;   // 1 Hz keep-alive for predictable ones
    public double AccelThreshold { get; init; } = 0.3; // m/s^2 below which motion is "steady"

    public bool ShouldPublish(in PublishSignals s)
    {
        var predictable = (s.Model == DrModel.LaneArc || s.Model == DrModel.Stationary)
            && !s.LaneChangingOrManoeuvring
            && Math.Abs(s.Accel) < AccelThreshold;

        var interval = predictable ? SlowInterval : FastInterval;
        return s.SecondsSinceLastSent >= interval;
    }
}

// Dead-reckoning-error policy (SUMOSHARP-DR-ERROR-PUBLISHING-DESIGN.md): publish only when the receiver's
// dead-reckoning would be wrong -- i.e. the true state has diverged from the DR prediction (computed by the
// scheduler via DrExtrapolation.Arc from the last-PUBLISHED state) beyond a small tolerance, or the lane
// changed, or a liveliness heartbeat elapsed. A genuinely steady vehicle diverges by ~0 -> not sent
// (bandwidth saved); a drifting/maneuvering one is sent promptly -> the viewer's extrapolation stays within
// tolerance -> smooth at low playout delay. First sighting: SecondsSinceLastSent is +inf >= MaxInterval.
public sealed class DrErrorPublishPolicy : IPublishPolicy
{
    public double PosTol { get; init; } = 0.3;      // m of longitudinal prediction error
    public double LatTol { get; init; } = 0.2;      // m of lateral (posLat) prediction error
    public double MaxInterval { get; init; } = 3.0; // s liveliness heartbeat

    public bool ShouldPublish(in PublishSignals s) =>
        s.LaneChanged
        || s.PosError > PosTol
        || s.LatError > LatTol
        || s.SecondsSinceLastSent >= MaxInterval;
}
