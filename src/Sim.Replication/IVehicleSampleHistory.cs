using System.Collections;
using System.Collections.Generic;

namespace Sim.Replication;

// docs/SUMOSHARP-PACKAGING-DESIGN.md §5 / D9 — the transport-neutral per-vehicle sample history: a
// capacity-bounded, NEWEST-LAST buffer of TimestampedSample. Ordering is explicit and load-bearing:
// index 0 is the OLDEST retained sample, index Count-1 is the NEWEST. This mirrors
// DdsSubscriber's per-vehicle List<VehicleSample> (also newest-last) and the DrClock consumer
// pattern of reading history[0] as oldest and history[^1] (i.e. Count-1) as newest, so a later
// swap of the transport-coupled history for this neutral one is a pure type substitution.
public interface IVehicleSampleHistory : IReadOnlyList<TimestampedSample>
{
}

// A simple capacity-bounded newest-last ring of TimestampedSample. Appending past capacity drops
// the OLDEST sample (index 0) so Count never exceeds the configured capacity. Backed by a plain
// List<T> with RemoveAt(0) on overflow -- O(n) per drop, but n is capped at a small history depth
// (DrClock uses 8), so the shift is negligible; this keeps the index-0-is-oldest invariant obvious
// and easy to verify rather than chasing a ring-buffer offset.
public sealed class VehicleSampleHistory : IVehicleSampleHistory
{
    private readonly int _capacity;
    private readonly List<TimestampedSample> _samples;

    public VehicleSampleHistory(int capacity = 8)
    {
        _capacity = capacity;
        _samples = new List<TimestampedSample>(capacity);
    }

    // Appends `sample` as the newest entry. Once Count == capacity, the OLDEST sample (index 0) is
    // dropped first so Count never exceeds capacity.
    public void Append(TimestampedSample sample)
    {
        if (_samples.Count >= _capacity)
        {
            _samples.RemoveAt(0);
        }

        _samples.Add(sample);
    }

    public int Count => _samples.Count;

    // index 0 == oldest retained sample; index Count-1 == newest.
    public TimestampedSample this[int index] => _samples[index];

    public IEnumerator<TimestampedSample> GetEnumerator() => _samples.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
