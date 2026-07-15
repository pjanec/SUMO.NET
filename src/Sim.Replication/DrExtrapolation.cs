namespace Sim.Replication;

// Shared dead-reckoning arc-length extrapolation -- the SINGLE source of truth for the constant-accel DR
// curve, used by BOTH the viewer (DrClock, so its render prediction matches) and the publisher's DR-error
// policy (so it publishes exactly when the viewer's prediction would drift). Clamped so a decelerating
// vehicle freezes at its stopping point instead of reversing past it. Ported verbatim from
// DrClock.ExtrapolateArc (Stage B makes DrClock delegate here).
public static class DrExtrapolation
{
    public static double Arc(double pos, double speed, double accel, double dt)
    {
        if (dt > 0.0)
        {
            if (speed <= 0.0)
            {
                return pos; // already stopped -> stay put (no drift, no reverse)
            }

            if (accel < 0.0)
            {
                var timeToStop = speed / -accel;
                if (dt > timeToStop)
                {
                    dt = timeToStop; // freeze at the stopping point instead of reversing past it
                }
            }
        }

        return pos + speed * dt + 0.5 * accel * dt * dt;
    }
}
