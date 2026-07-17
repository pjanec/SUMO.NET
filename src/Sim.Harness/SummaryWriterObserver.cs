using System.Globalization;
using Sim.Core;

namespace Sim.Harness;

/// <summary>
/// P0-D (docs/HIGH-DENSITY-P0-DESIGN.md "P0-D", verified against sumo/src/microsim/MSNet.cpp:
/// 607-647 + MSVehicleControl.cpp:516-543 + StdDefs.h:58): the <c>--summary-output</c> analog of
/// <see cref="FcdWriterObserver"/> -- registers on the SAME D9 export seam (<see
/// cref="Engine.AddExportObserver"/>), so it aggregates over EXACTLY the per-frame,
/// per-active-vehicle state the committed FCD trajectory does (registering ANY export observer
/// forces Engine.EmitTrajectory's serial per-vehicle loop, see that method's own comment), never a
/// separately-recomputed view.
///
/// Per sim step this accumulates, over every vehicle <see cref="ISimExportObserver.OnVehicleExported"/>
/// reports that frame (i.e. every ON-ROAD vehicle -- inserted, not yet arrived, <see
/// cref="Engine.ActiveVehicles"/>'s own definition):
///   - `running`   = the frame's vehicle count (getRunningVehicleNo() analog).
///   - `halting`   = count with `speed &lt; SUMO_const_haltingSpeed (0.1)`, over ALL on-road
///                   vehicles (stopped ones included -- a vehicle held at a &lt;stop&gt; has
///                   speed 0 and is also "halting").
///   - `stopped`   = count with <see cref="VehicleExportSnapshot.IsStoppedAtStop"/> true
///                   (currently held at the front of its own stop queue, reached).
///   - `meanSpeed`/`meanSpeedRelative` = mean speed / mean(speed / edge-speed-limit) over on-road
///     AND non-stopped vehicles ONLY -- both sentinel `null` (SUMO's `-1`) when that subset is
///     empty. `meanSpeedRelative` divides by <see cref="VehicleExportSnapshot.EdgeSpeedLimit"/>
///     (the CURRENT edge's own speed limit), never the vehicle's own vType maxSpeed.
///
/// `arrived` is tracked too (a per-frame diff of the active-vehicle-id set: any id present last
/// frame and absent this frame has arrived -- phase 1 has no teleport/despawn other than genuine
/// arrival) for schema completeness/console parity with <see cref="SummaryStepRecord"/>'s
/// pre-existing field, but is NOT one of the P0-D graded attributes (<see cref="SummaryComparator"/>
/// deliberately does not compare it) since the design doc's P0-D subset is time/running/halting/
/// stopped/meanSpeed/meanSpeedRelative only.
///
/// Optionally writes a SUMO-schema <c>--summary-output</c> file as it goes (same "write AND
/// remember" shape as <see cref="FcdWriterObserver"/> would if it also exposed an in-memory
/// buffer) -- the parameterless constructor skips the file entirely and only fills <see
/// cref="Records"/>, which is what the parity test reads (mirroring how <see cref="Engine.Run"/>
/// hands the FCD parity test an in-memory <c>TrajectorySet</c> with no file round-trip needed).
/// </summary>
public sealed class SummaryWriterObserver : ISimExportObserver, IDisposable
{
    private readonly TextWriter? _writer;
    private readonly bool _ownsWriter;
    private bool _rootOpen;
    private bool _closed;

    private readonly List<SummaryStepRecord> _records = new();

    private int _running;
    private int _halting;
    private int _stopped;
    private double _speedSum;
    private double _speedRelativeSum;
    private int _meanCount;

    private HashSet<string> _prevActiveIds = new();
    private HashSet<string> _currentActiveIds = new();
    private int _cumulativeArrived;

    /// <summary>Every <c>&lt;step&gt;</c> accumulated so far, in frame order -- the parity test's read surface.</summary>
    public IReadOnlyList<SummaryStepRecord> Records => _records;

    /// <summary>In-memory only -- no file written. Used by the parity test (no round-trip needed).</summary>
    public SummaryWriterObserver()
        : this(writer: null, ownsWriter: false)
    {
    }

    public SummaryWriterObserver(string path)
        : this(new StreamWriter(path, append: false), ownsWriter: true)
    {
    }

    public SummaryWriterObserver(TextWriter? writer, bool ownsWriter = false)
    {
        _writer = writer;
        _ownsWriter = ownsWriter;
        if (_writer is not null)
        {
            _writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            _writer.WriteLine("<summary>");
            _rootOpen = true;
        }
    }

    public void OnFrameBegin(double time)
    {
        _running = 0;
        _halting = 0;
        _stopped = 0;
        _speedSum = 0.0;
        _speedRelativeSum = 0.0;
        _meanCount = 0;
        _currentActiveIds.Clear();
    }

    public void OnVehicleExported(in VehicleExportSnapshot snapshot)
    {
        _running++;
        _currentActiveIds.Add(snapshot.VehicleId);

        // MSNet.cpp / StdDefs.h SUMO_const_haltingSpeed: ALL on-road vehicles, stopped ones
        // included (a vehicle held at a <stop> has speed 0, so it is also "halting").
        if (snapshot.Speed < KraussModel.HaltingSpeed)
        {
            _halting++;
        }

        if (snapshot.IsStoppedAtStop)
        {
            _stopped++;
        }
        else
        {
            // meanSpeed/meanSpeedRelative: on-road AND non-stopped only -- a stopped vehicle
            // contributes to neither the numerator nor the count (design doc P0-D).
            _meanCount++;
            _speedSum += snapshot.Speed;
            _speedRelativeSum += snapshot.EdgeSpeedLimit > 0.0 ? snapshot.Speed / snapshot.EdgeSpeedLimit : 0.0;
        }
    }

    public void OnFrameEnd(double time)
    {
        foreach (var id in _prevActiveIds)
        {
            if (!_currentActiveIds.Contains(id))
            {
                _cumulativeArrived++;
            }
        }

        (_prevActiveIds, _currentActiveIds) = (_currentActiveIds, _prevActiveIds);

        double? meanSpeed = _meanCount > 0 ? _speedSum / _meanCount : (double?)null;
        double? meanSpeedRelative = _meanCount > 0 ? _speedRelativeSum / _meanCount : (double?)null;

        var record = new SummaryStepRecord(
            Time: time,
            Running: _running,
            Arrived: _cumulativeArrived,
            MeanSpeed: meanSpeed,
            Halting: _halting,
            Stopped: _stopped,
            MeanSpeedRelative: meanSpeedRelative);

        _records.Add(record);

        if (_writer is not null)
        {
            WriteStep(record);
        }
    }

    private void WriteStep(SummaryStepRecord s)
    {
        _writer!.Write("    <step time=\"");
        _writer.Write(s.Time.ToString("F3", CultureInfo.InvariantCulture));
        _writer.Write("\" running=\"");
        _writer.Write(s.Running.ToString(CultureInfo.InvariantCulture));
        _writer.Write("\" arrived=\"");
        _writer.Write(s.Arrived.ToString(CultureInfo.InvariantCulture));
        _writer.Write("\" halting=\"");
        _writer.Write(s.Halting.ToString(CultureInfo.InvariantCulture));
        _writer.Write("\" stopped=\"");
        _writer.Write(s.Stopped.ToString(CultureInfo.InvariantCulture));
        _writer.Write("\" meanSpeed=\"");
        _writer.Write((s.MeanSpeed ?? -1.0).ToString("F6", CultureInfo.InvariantCulture));
        _writer.Write("\" meanSpeedRelative=\"");
        _writer.Write((s.MeanSpeedRelative ?? -1.0).ToString("F6", CultureInfo.InvariantCulture));
        _writer.WriteLine("\"/>");
    }

    private void CloseRoot()
    {
        if (_closed)
        {
            return;
        }

        if (_rootOpen && _writer is not null)
        {
            _writer.WriteLine("</summary>");
            _writer.Flush();
            _rootOpen = false;
        }

        _closed = true;
    }

    public void Dispose()
    {
        CloseRoot();
        if (_ownsWriter)
        {
            _writer?.Dispose();
        }
    }
}
