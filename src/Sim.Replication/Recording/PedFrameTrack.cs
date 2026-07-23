using System;
using System.Collections.Generic;

namespace Sim.Replication.Recording;

// docs/LIVE-CITY-VIEWERS-DESIGN.md §2.1, docs/LIVE-CITY-VIEWERS-TASKS.md Stage C (C2) — the ped side of a
// `.simrec` replay. Peds render as plain regime-coloured discs (LiveCityOverlay), never through the
// KinematicReconstructor, so a per-frame position+regime snapshot is all replay needs -- no dead-reckoning,
// no history buffering, just "which frame is nearest <= t". A NEUTRAL tuple (no Sim.LiveCity dependency,
// per the design's explicit "no dependency on Sim.LiveCity" instruction) -- the viewer maps the `Regime`
// byte to its own `Sim.LiveCity.PedRegime` enum (the numeric values already agree: LowPowerWalking=0,
// HighPower=1, Paused=2).
//
// Eagerly loads every PEDFRAME record into memory up front (one linear pass over the file, ignoring every
// other record type) and answers PedsAt via binary search -- simple and robust at the minute-scale
// recordings this feature targets; a multi-hour recording would want a streaming/windowed version instead.
public sealed class PedFrameTrack
{
    private readonly List<(double Time, (int Id, float X, float Y, float Z, byte Regime, string AnimTag)[] Peds)> _frames = new();

    public PedFrameTrack(string path)
    {
        using var reader = new SimRecReader(path);
        while (reader.TryReadNext(out var entry))
        {
            if (entry.Kind == SimRecFormat.RecordType.PedFrame)
            {
                _frames.Add((entry.Time, entry.Peds!));
            }
        }
    }

    public int FrameCount => _frames.Count;

    // The PEDFRAME nearest <= t, or the earliest frame if t is before every recorded frame, or an empty
    // list if the recording has no ped frames at all.
    public IReadOnlyList<(int Id, float X, float Y, float Z, byte Regime, string AnimTag)> PedsAt(double t)
    {
        if (_frames.Count == 0)
        {
            return Array.Empty<(int, float, float, float, byte, string)>();
        }

        var lo = 0;
        var hi = _frames.Count - 1;
        var best = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_frames[mid].Time <= t)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return _frames[best].Peds;
    }
}
