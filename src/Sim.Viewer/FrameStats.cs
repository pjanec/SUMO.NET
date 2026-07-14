namespace Sim.Viewer;

// P1 diagnostics panel: a ring buffer of the last ~120 Raylib.GetFrameTime() samples (roughly 2s at
// 60fps), from which the diagnostics panel derives fps/min/avg/p99 frame time -- SUMOSHARP-NATIVE-
// VIEWER.md P1's "perf-diagnostics panel".
public sealed class FrameStats
{
    private readonly float[] _buf;
    private int _count;
    private int _head;

    public FrameStats(int capacity = 120)
    {
        _buf = new float[capacity];
    }

    public void Add(float frameSeconds)
    {
        _buf[_head] = frameSeconds;
        _head = (_head + 1) % _buf.Length;
        if (_count < _buf.Length)
        {
            _count++;
        }
    }

    // (minSeconds, avgSeconds, p99Seconds) over the samples currently in the buffer. All zero if empty.
    public (float Min, float Avg, float P99) Compute()
    {
        if (_count == 0)
        {
            return (0f, 0f, 0f);
        }

        var samples = new float[_count];
        Array.Copy(_buf, samples, _count);
        Array.Sort(samples);

        var min = samples[0];
        var sum = 0f;
        foreach (var s in samples)
        {
            sum += s;
        }

        var avg = sum / _count;
        var p99Index = Math.Min(_count - 1, (int)Math.Ceiling(_count * 0.99) - 1);
        var p99 = samples[Math.Max(0, p99Index)];
        return (min, avg, p99);
    }
}
