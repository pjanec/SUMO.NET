using System;

namespace Sim.Replication.Recording;

// docs/LIVE-CITY-VIEWERS-DESIGN.md §4, docs/LIVE-CITY-VIEWERS-TASKS.md Stage C (C2/C3) — the replay
// timeline's authority: `Now` is the sim-time cursor `ReplicationFileSource`/`PedFrameTrack` read from
// every frame. Deliberately dumb (no wall-clock of its own beyond what `Tick` is handed) so it is
// trivially unit-testable and reusable headlessly (the replay smoke test drives it via `SeekTo` directly,
// with no real wall time involved at all).
public sealed class PlaybackClock
{
    public double Now { get; private set; }

    public bool Playing { get; private set; } = true;

    public double Speed { get; set; } = 1.0;

    // The recording's total length (sim seconds). 0 until the caller sets it from
    // ReplicationFileSource.Duration; Now clamps into [0, Duration] once it is positive.
    public double Duration { get; set; }

    // Frame-step granularity (sim seconds) -- normally set to the recording's own Dt (ReplicationFileSource.Dt)
    // so StepFrame moves exactly one recorded VFRAME at a time.
    public double Dt { get; set; } = 0.5;

    public void Play() => Playing = true;

    public void Pause() => Playing = false;

    public void Restart() => Now = 0.0;

    public void SeekTo(double t) => Now = Duration > 0.0 ? Math.Clamp(t, 0.0, Duration) : Math.Max(0.0, t);

    // direction: +1 steps forward one Dt, -1 steps backward one Dt.
    public void StepFrame(int direction) => SeekTo(Now + direction * Dt);

    // Advance Now by wallDelta*Speed while Playing (a no-op while paused), clamped to [0, Duration].
    public void Tick(double wallDelta)
    {
        if (!Playing)
        {
            return;
        }

        SeekTo(Now + wallDelta * Speed);
    }
}
