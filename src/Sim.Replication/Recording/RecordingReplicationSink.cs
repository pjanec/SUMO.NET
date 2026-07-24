using System;
using System.Collections.Generic;

namespace Sim.Replication.Recording;

// docs/LIVE-CITY-VIEWERS-DESIGN.md §2.2, docs/LIVE-CITY-VIEWERS-TASKS.md Stage C (C1) — an IReplicationSink
// DECORATOR: every Publish* call writes the framed record to the owned `.simrec` file AND forwards to an
// OPTIONAL inner sink, unchanged (zero behaviour change to the inner sink -- it sees exactly the calls it
// would have seen without a recorder in front of it). `inner` is null in the common case (RunLiveCity's
// `--record` tee: LiveCitySim publishes to a SEPARATE ReplicationPublisher/sink pair purely for recording,
// so there is nothing to forward to here); it exists so the decorator shape matches the design's stated
// contract and is usable in front of a live sink too (e.g. a future "record what's being published live"
// wiring) without changing this class.
//
// LifecycleRecord carries no timestamp of its own (IReplicationSink.PublishLifecycle takes no time param),
// and ReplicationPublisher.PublishStep always calls PublishLifecycle BEFORE PublishFrame within the same
// step -- so pending lifecycle records are buffered here and flushed (stamped with that frame's time) the
// moment the next PublishFrame/PublishTrafficLights arrives, which is normally the very next call (a
// newly-spawned vehicle's first sighting always passes PublishScheduler's adaptive gate, so a step with a
// lifecycle event essentially always publishes a frame too). Any records still pending at Dispose (e.g. a
// despawn on the very last recorded step) are flushed stamped with the last time seen.
//
// The ped track has no wire codec/interface of its own (docs/LIVE-CITY-VIEWERS-DESIGN.md §2.1: peds render
// as plain discs, so a per-frame position+regime snapshot is all replay needs) -- WritePedFrame is an
// extra method beyond IReplicationSink, called directly by RunLiveCity's record loop each step, sharing
// THIS SAME writer/file so both tracks interleave by time in one `.simrec`.
public sealed class RecordingReplicationSink : IReplicationSink
{
    private readonly SimRecWriter _writer;
    private readonly IReplicationSink? _inner;
    private readonly List<LifecycleRecord> _pendingLifecycle = new();
    private double _lastKnownTime;

    public RecordingReplicationSink(string path, double dt, string datasetId, IReplicationSink? inner = null)
    {
        _writer = new SimRecWriter(path, dt, datasetId);
        _inner = inner;
    }

    public void PublishGeometry(IReadOnlyList<GeometryCodec.LaneGeo> lanes)
    {
        _writer.WriteGeometry(lanes);
        _inner?.PublishGeometry(lanes);
    }

    public void PublishLifecycle(in LifecycleRecord record)
    {
        _pendingLifecycle.Add(record);
        _inner?.PublishLifecycle(record);
    }

    public void PublishFrame(uint step, double time, ReadOnlySpan<VehicleRecord> movers)
    {
        FlushPendingLifecycle(time);
        _writer.WriteVehicleFrame(step, time, movers);
        _lastKnownTime = time;
        _inner?.PublishFrame(step, time, movers);
    }

    public void PublishTrafficLights(uint step, double time, IReadOnlyList<TlCodec.TlEntry> lights)
    {
        FlushPendingLifecycle(time);
        _writer.WriteTrafficLights(step, time, lights);
        _lastKnownTime = time;
        _inner?.PublishTrafficLights(step, time, lights);
    }

    // Not part of IReplicationSink -- see the class remark. Shares this sink's own writer/file so ped
    // frames interleave with the car track by time instead of living in a second file.
    public void WritePedFrame(double time, IReadOnlyList<(int Id, float X, float Y, float Z, byte Regime, string AnimTag)> peds)
    {
        FlushPendingLifecycle(time);
        _writer.WritePedFrame(time, peds);
        _lastKnownTime = time;
    }

    public void Flush() => _writer.Flush();

    private void FlushPendingLifecycle(double time)
    {
        if (_pendingLifecycle.Count == 0)
        {
            return;
        }

        foreach (var rec in _pendingLifecycle)
        {
            _writer.WriteLifecycle(rec, time);
        }

        _pendingLifecycle.Clear();
    }

    public void Dispose()
    {
        FlushPendingLifecycle(_lastKnownTime);
        _writer.Dispose();
        _inner?.Dispose();
    }
}
