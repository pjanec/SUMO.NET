using System.Diagnostics;
using System.Globalization;
using Sim.Core;
using Sim.Core.Mixed;
using Sim.Core.Orca;
using Sim.Evac;

// PANIC-EVAC-PHASE5-TASKS.md T4.2 (design §4/§6): the Tier-1 cost-profile deliverable. Runs the
// organic-town evac demo (EvacOrganicScenario -- the same fixture EvacOrganicDemoTests and
// SceneGen.BuildEvacOrganic use: scenarios/_bench/city-organic-L2, incident at junction 415, auto-
// track working region) with EvacDirector's opt-in profiler turned on, and reports a per-phase
// wall-time breakdown -- fear update, disc feeds, pedestrian step, pusher step, engine.Step (the
// parity core, context only), and "other" -- so the Tier-2 optimization list (design §6 candidates:
// FearField grid, spatial disc feeds, OrcaCrowd.UseSpatialHash, etc.) targets the MEASURED dominant
// hotspot rather than a guessed one.
//
// NOT part of `dotnet test` -- a deliberate CLI utility (like Sim.Bench / Sim.BenchCity / Sim.Viz).
// Never touches the parity engine's committed inputs/goldens; EvacDirector's profiler is a pure
// opt-in observability seam (null unless EnableProfiling() is called), so running this tool has zero
// effect on any other demo/test's behaviour or the determinism hash.
//
// PANIC-EVAC-PHASE5-TIER2-DESIGN.md §2a/§2b/§5(1) / TASKS T2.3: `--microbench` is a SEPARATE mode
// (same exe, gated by an arg so the default T4.2 profile run is unaffected) measuring the reason for
// the Tier-2 spatial-hash work directly: for each crowd solver (MixedTrafficCrowd, OrcaCrowd) and a
// few synthetic heavy loads (N ~= 250/1000/2000 agents in a bounded region, goals mirrored across it
// so they genuinely interact), time Step() brute-force vs grid and report the speedup. Not a
// determinism gate -- a measurement (the bit-identity guarantee is proven separately by
// MixedTrafficSpatialHashTests / OrcaSpatialHashTests).
internal static class Program
{
    private const int Ticks = 300;

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        if (Array.IndexOf(args, "--microbench") >= 0)
        {
            return RunMicrobench();
        }

        if (Array.IndexOf(args, "--city") >= 0)
        {
            return RunCity(args);
        }

        var repoRoot = RepoRoot();
        var (engine, director) = EvacOrganicScenario.Build(repoRoot);
        director.EnableProfiling();

        var peakActive = 0;
        var everActive = new HashSet<VehicleHandle>();

        var sw = Stopwatch.StartNew();
        for (var step = 0; step < Ticks; step++)
        {
            director.Tick();

            var handles = engine.VehicleHandles;
            peakActive = Math.Max(peakActive, handles.Length);
            foreach (var h in handles)
            {
                everActive.Add(h);
            }
        }

        sw.Stop();

        var profile = director.Profile;
        var totalMs = sw.Elapsed.TotalMilliseconds;

        var fearMs = profile.FearUpdate.TotalMilliseconds;
        var discFeedsMs = profile.DiscFeeds.TotalMilliseconds;
        var pedStepMs = profile.PedestrianStep.TotalMilliseconds;
        var pusherStepMs = profile.PusherStep.TotalMilliseconds;
        var engineStepMs = profile.EngineStep.TotalMilliseconds;
        var accountedMs = fearMs + discFeedsMs + pedStepMs + pusherStepMs + engineStepMs;
        var otherMs = Math.Max(0.0, profile.TotalTick.TotalMilliseconds - accountedMs);

        Console.WriteLine("=== T4.2 evac cost profile: organic town (EvacOrganicScenario) ===");
        Console.WriteLine($"scenario           : scenarios/_bench/city-organic-L2 (274 junctions, 1186 edges, 618 trips)");
        Console.WriteLine($"ticks              : {Ticks}  (stepLength=1.0s)");
        Console.WriteLine($"peak concurrent    : {peakActive}  (vehicles active in the SAME tick, parity-engine-wide)");
        Console.WriteLine($"ever active        : {everActive.Count}  (distinct vehicles seen over the whole run)");
        Console.WriteLine($"panicked           : {director.PanickedCount}");
        Console.WriteLine($"converted          : {director.ConvertedCount}");
        Console.WriteLine($"pedestrians        : {director.PedestrianCount}");
        Console.WriteLine();
        Console.WriteLine($"total generation wall time : {totalMs:F1} ms  ({sw.Elapsed.TotalSeconds:F3} s)");
        Console.WriteLine();
        Console.WriteLine("per-phase breakdown (of EvacDirector.Tick() wall time):");

        void PrintPhase(string name, double ms) =>
            Console.WriteLine($"  {name,-18} {ms,9:F1} ms   {(totalMs > 0 ? ms / totalMs : 0.0),6:P1}");

        PrintPhase("fear update", fearMs);
        PrintPhase("disc feeds", discFeedsMs);
        PrintPhase("pedestrian step", pedStepMs);
        PrintPhase("pusher step", pusherStepMs);
        PrintPhase("engine.Step", engineStepMs);
        PrintPhase("other", otherMs);

        // The dominant EVAC hotspot -- deliberately excludes engine.Step (the parity core; it is
        // reported as context, not a Tier-2 optimization candidate per design §6) and "other" (not
        // one specific named phase to optimize). This is the input that scopes the Tier-2 task list.
        var evacPhases = new (string Name, double Ms)[]
        {
            ("fear update", fearMs),
            ("disc feeds", discFeedsMs),
            ("pedestrian step", pedStepMs),
            ("pusher step", pusherStepMs),
        };
        var dominant = evacPhases.OrderByDescending(p => p.Ms).First();

        Console.WriteLine();
        Console.WriteLine(
            $"DOMINANT EVAC HOTSPOT: {dominant.Name}  ({dominant.Ms:F1} ms, " +
            $"{(totalMs > 0 ? dominant.Ms / totalMs : 0.0):P1} of total tick time) " +
            "-- this is what Tier 2 should optimize first.");

        return 0;
    }

    // PANIC-EVAC-PHASE5-TIER2-DESIGN.md §3d/§5(3) / TASKS T2.7: the Tier-2 closing deliverable. Runs
    // EvacCityScenario (the committed 10k-class city-15000 host, design §3a option A) TWICE -- Tier-2
    // spatial hashes OFF then ON -- and prints the same per-phase breakdown T4.2 does for each run,
    // plus the speedup on the two measured hotspots (pusher/pedestrian step) and a separate auto-track
    // scan verdict (is its O(city) cost material at this scale?). `--city [ticks]` -- ticks defaults
    // to 300 (mirrors the organic profile's run length); pass a smaller number for a quick check.
    private static int RunCity(string[] args)
    {
        var ticks = DefaultCityTicks;
        var cityArgIdx = Array.IndexOf(args, "--city");
        if (cityArgIdx + 1 < args.Length && int.TryParse(args[cityArgIdx + 1], out var parsedTicks))
        {
            ticks = parsedTicks;
        }

        var repoRoot = RepoRoot();

        CityRun RunOnce(bool useSpatialHash)
        {
            var config = EvacCityScenario.DefaultConfig() with { UseCrowdSpatialHash = useSpatialHash };
            var (engine, director) = EvacCityScenario.Build(repoRoot, config: config);
            director.EnableProfiling();

            var peakActive = 0;
            var sw = Stopwatch.StartNew();
            for (var step = 0; step < ticks; step++)
            {
                director.Tick();
                peakActive = Math.Max(peakActive, engine.VehicleHandles.Length);
            }

            sw.Stop();

            return new CityRun(
                useSpatialHash, sw.Elapsed.TotalMilliseconds, director.Profile, peakActive,
                director.TrackedCount, director.PanickedCount, director.ConvertedCount,
                director.PusherCount, director.PedestrianCount);
        }

        Console.WriteLine("=== T2.7 evac cost profile: city-15000 (EvacCityScenario), hashes OFF vs ON ===");
        Console.WriteLine($"scenario           : scenarios/_bench/city-15000 (24x24 grid, 1 lane, ~13-17k peak concurrent)");
        Console.WriteLine($"ticks              : {ticks}  (stepLength=1.0s)");
        Console.WriteLine();

        var off = RunOnce(useSpatialHash: false);
        var on = RunOnce(useSpatialHash: true);

        void PrintRun(string label, CityRun r)
        {
            Console.WriteLine($"--- {label} (UseCrowdSpatialHash={r.UseSpatialHash}) ---");
            Console.WriteLine($"peak concurrent (city-wide) : {r.PeakActive}");
            Console.WriteLine($"tracked working-region pop. : {r.TrackedCount}");
            Console.WriteLine($"panicked / converted        : {r.Panicked} / {r.Converted}");
            Console.WriteLine($"pushers (ever) / pedestrians : {r.PusherCount} / {r.Pedestrians}");
            Console.WriteLine($"total generation wall time  : {r.TotalMs:F1} ms  ({r.TotalMs / 1000.0:F3} s)");
            Console.WriteLine("per-phase breakdown:");
            PrintPhaseRow("fear update", r.Profile.FearUpdate.TotalMilliseconds, r.TotalMs);
            PrintPhaseRow("disc feeds", r.Profile.DiscFeeds.TotalMilliseconds, r.TotalMs);
            PrintPhaseRow("pedestrian step", r.Profile.PedestrianStep.TotalMilliseconds, r.TotalMs);
            PrintPhaseRow("pusher step", r.Profile.PusherStep.TotalMilliseconds, r.TotalMs);
            PrintPhaseRow("engine.Step", r.Profile.EngineStep.TotalMilliseconds, r.TotalMs);
            PrintPhaseRow("auto-track scan", r.Profile.AutoTrackScan.TotalMilliseconds, r.TotalMs);
            Console.WriteLine();
        }

        PrintRun("OFF (brute force)", off);
        PrintRun("ON  (spatial hash)", on);

        double Speedup(double offMs, double onMs) => onMs > 0 ? offMs / onMs : double.PositiveInfinity;

        var pusherSpeedup = Speedup(off.Profile.PusherStep.TotalMilliseconds, on.Profile.PusherStep.TotalMilliseconds);
        var pedSpeedup = Speedup(off.Profile.PedestrianStep.TotalMilliseconds, on.Profile.PedestrianStep.TotalMilliseconds);

        Console.WriteLine("=== before/after summary ===");
        Console.WriteLine(
            $"pusher step      : {off.Profile.PusherStep.TotalMilliseconds,9:F1} ms -> " +
            $"{on.Profile.PusherStep.TotalMilliseconds,9:F1} ms   ({pusherSpeedup,6:F2}x)");
        Console.WriteLine(
            $"pedestrian step  : {off.Profile.PedestrianStep.TotalMilliseconds,9:F1} ms -> " +
            $"{on.Profile.PedestrianStep.TotalMilliseconds,9:F1} ms   ({pedSpeedup,6:F2}x)");
        Console.WriteLine(
            $"total generation : {off.TotalMs,9:F1} ms -> {on.TotalMs,9:F1} ms   ({Speedup(off.TotalMs, on.TotalMs),6:F2}x)");
        Console.WriteLine();

        // Auto-track scan verdict: report the ON run's number (the run that matters -- the ON run is
        // the one the demo actually ships with) as a % of that run's total tick time. design §3d: only
        // optimize the O(city) scan if the measurement shows it material; no speculative work.
        var autoTrackMs = on.Profile.AutoTrackScan.TotalMilliseconds;
        var autoTrackPct = on.TotalMs > 0 ? autoTrackMs / on.TotalMs : 0.0;
        const double materialThresholdPct = 0.05;   // 5% of tick time -- design's "material" bar
        var verdict = autoTrackPct >= materialThresholdPct
            ? "MATERIAL -- the O(city) auto-track scan should get its own optimization (T2.8 candidate: " +
              "a coarse world-grid pre-filter over the read buffer, or an incremental entrant set)."
            : "NOT material at this scale -- no optimization warranted (design §3d: measurement-gated, no speculative work).";
        Console.WriteLine(
            $"AUTO-TRACK SCAN VERDICT: {autoTrackMs:F1} ms total ({autoTrackPct:P1} of ON run's tick time, " +
            $"peak concurrent {on.PeakActive}). {verdict}");

        return 0;
    }

    private const int DefaultCityTicks = 300;

    private readonly record struct CityRun(
        bool UseSpatialHash, double TotalMs, ProfileSnapshot Profile, int PeakActive, int TrackedCount,
        int Panicked, int Converted, int PusherCount, int Pedestrians);

    private static void PrintPhaseRow(string name, double ms, double totalMs) =>
        Console.WriteLine($"  {name,-18} {ms,9:F1} ms   {(totalMs > 0 ? ms / totalMs : 0.0),6:P1}");

    // T2.3: synthetic heavy-load microbenchmark for the two Tier-2 spatial-hash solvers. For each
    // solver and each N, builds two crowds -- brute-force and grid (UseSpatialHash) -- placed
    // identically (a square grid layout at a FIXED per-agent spacing, so the region spans sqrt(N),
    // keeping local density -- and hence each agent's REAL neighbour count -- roughly constant across
    // N; only the brute-force scan's "how many agents do I have to LOOK AT before rejecting them as
    // out of range" grows with N, which is exactly the cost the spatial hash targets). Every agent's
    // goal is mirrored to the opposite side of the region so they walk toward and past each other for
    // the whole run (genuine sustained interaction, not a one-off pass). Each configuration is JIT-
    // warmed on a small throwaway crowd first, then timed over `repeats` independent builds and the
    // MEDIAN wall time is reported, to keep the numbers honest against JIT tiering / GC noise.
    private static int RunMicrobench()
    {
        Console.WriteLine("=== T2.3 heavy-load micro-benchmark: MixedTrafficCrowd / OrcaCrowd, brute vs grid ===");
        Console.WriteLine();

        var counts = new[] { 250, 1000, 2000 };
        const int steps = 60;
        const int repeats = 5;
        const double dt = 0.1;

        Console.WriteLine($"{"solver",-18} {"N",6} {"brute ms",10} {"grid ms",10} {"speedup",9}");

        foreach (var n in counts)
        {
            var (bruteMs, gridMs) = BenchmarkOrca(n, steps, repeats, dt);
            PrintRow("OrcaCrowd", n, bruteMs, gridMs);
        }

        Console.WriteLine();

        foreach (var n in counts)
        {
            var (bruteMs, gridMs) = BenchmarkMixed(n, steps, repeats, dt);
            PrintRow("MixedTrafficCrowd", n, bruteMs, gridMs);
        }

        return 0;
    }

    private static void PrintRow(string solver, int n, double bruteMs, double gridMs)
    {
        var speedup = gridMs > 0 ? bruteMs / gridMs : double.PositiveInfinity;
        Console.WriteLine($"{solver,-18} {n,6} {bruteMs,10:F1} {gridMs,10:F1} {speedup,8:F2}x");
    }

    // Square grid layout at a FIXED per-agent spacing: the region side grows as spacing*sqrt(N), so
    // local density (and each agent's real in-range neighbour count) stays roughly constant as N grows
    // -- isolating the ONE thing that genuinely scales with N: the brute-force scan's per-agent "look
    // at every other agent" cost. Goal is the point mirrored through the region's centre, so every
    // agent walks toward (and past) roughly-opposite agents for the whole run.
    private static (Vec2 Pos, Vec2 Goal)[] GridLayout(int n, double spacing)
    {
        var side = (int)Math.Ceiling(Math.Sqrt(n));
        var centre = (side - 1) * spacing * 0.5;
        var layout = new (Vec2 Pos, Vec2 Goal)[n];
        var k = 0;
        for (var gy = 0; gy < side && k < n; gy++)
        {
            for (var gx = 0; gx < side && k < n; gx++)
            {
                var p = new Vec2(gx * spacing - centre, gy * spacing - centre);
                layout[k] = (p, -p);
                k++;
            }
        }

        return layout;
    }

    // Median wall time (ms) of `repeats` independent timed runs of `steps` Step() calls, each run on a
    // FRESH crowd from `build` (so no cross-run state leaks in), after a JIT/tiering warmup pass on a
    // small throwaway crowd built the same way. Median (not mean/min) damps outliers from GC/OS jitter
    // without hand-picking the most favourable sample.
    private static double MedianTimedMs<TCrowd>(
        Func<int, TCrowd> build, Action<TCrowd, double> step, int n, int steps, int repeats, double dt)
    {
        // Warmup: run the SAME code path (same build closure, small N) enough times to clear JIT
        // tiering before any timed sample, on a crowd that is discarded afterwards.
        var warm = build(Math.Min(n, 24));
        for (var s = 0; s < 20; s++)
        {
            step(warm, dt);
        }

        var samples = new double[repeats];
        for (var r = 0; r < repeats; r++)
        {
            var crowd = build(n);
            var sw = Stopwatch.StartNew();
            for (var s = 0; s < steps; s++)
            {
                step(crowd, dt);
            }

            sw.Stop();
            samples[r] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static (double BruteMs, double GridMs) BenchmarkOrca(int n, int steps, int repeats, double dt)
    {
        OrcaCrowd Build(int count, bool useGrid)
        {
            var crowd = new OrcaCrowd(count) { MaxNeighbours = 8, SymmetryBreak = 0.05, UseSpatialHash = useGrid };
            foreach (var (pos, goal) in GridLayout(count, spacing: 6.0))
            {
                crowd.Add(pos, radius: 0.3, maxSpeed: 1.4, goal);
            }

            return crowd;
        }

        var bruteMs = MedianTimedMs(c => Build(c, useGrid: false), (c, dt) => c.Step(dt), n, steps, repeats, dt);
        var gridMs = MedianTimedMs(c => Build(c, useGrid: true), (c, dt) => c.Step(dt), n, steps, repeats, dt);
        return (bruteMs, gridMs);
    }

    private static (double BruteMs, double GridMs) BenchmarkMixed(int n, int steps, int repeats, double dt)
    {
        MixedTrafficCrowd Build(int count, bool useGrid)
        {
            var crowd = new MixedTrafficCrowd(count)
            {
                MaxNeighbours = 12,
                SymmetryBreak = 0.05,
                UseSpatialHash = useGrid,
            };
            foreach (var (pos, goal) in GridLayout(count, spacing: 25.0))
            {
                crowd.Add(VehicleClass.Car, pos, goal);
            }

            return crowd;
        }

        var bruteMs = MedianTimedMs(c => Build(c, useGrid: false), (c, dt) => c.Step(dt), n, steps, repeats, dt);
        var gridMs = MedianTimedMs(c => Build(c, useGrid: true), (c, dt) => c.Step(dt), n, steps, repeats, dt);
        return (bruteMs, gridMs);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Traffic.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (Traffic.sln not found above the exe).");
    }
}
