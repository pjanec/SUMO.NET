using System.Linq;
using Sim.Core;
using Sim.Core.Orca;
using Sim.Evac;
using Xunit;
using Xunit.Abstractions;

namespace Sim.ParityTests;

// PANIC-EVAC-PHASE5-DESIGN.md / PANIC-EVAC-PHASE5-TASKS.md Tier 1, Stage S2/S3: behavioural validation
// of the organic-town demo (EvacOrganicScenario) -- loaded demand inserting under the director's own
// Tick() loop (T2.2), a real cascade emerging on a realistic mesh (T3.1), the working-region locality
// property that is the core of Phase 5 (T3.2), and containment + determinism (T3.3).
public class EvacOrganicDemoTests
{
    private readonly ITestOutputHelper _out;
    public EvacOrganicDemoTests(ITestOutputHelper output) => _out = output;

    private static readonly string TestRepoRoot = RepoRoot();

    // ----- T2.2: demand-under-director confirmation -----

    [Fact]
    public void DemandInsertsUnderDirector()
    {
        var (engine, director) = EvacOrganicScenario.Build(TestRepoRoot);

        var peakActive = 0;
        var everActive = new HashSet<VehicleHandle>();
        for (var step = 0; step < 200; step++)
        {
            director.Tick();

            var handles = engine.VehicleHandles;
            peakActive = Math.Max(peakActive, handles.Length);
            foreach (var h in handles)
            {
                everActive.Add(h);
            }
        }

        _out.WriteLine($"peakActive={peakActive} everActiveDistinctCount={everActive.Count}");
        Assert.True(peakActive > 0,
            "expected the scenario's loaded demand to insert vehicles under the director's Tick() loop");
    }

    // ----- T3.1: cascade on the organic net -----

    [Fact]
    public void CascadeEmerges()
    {
        var config = EvacOrganicScenario.DefaultConfig();
        var (_, director) = EvacOrganicScenario.Build(TestRepoRoot, config: config);

        var peakOrcaPush = 0;
        var maxPedDist = 0.0;
        for (var step = 0; step < 300; step++)
        {
            director.Tick();
            peakOrcaPush = Math.Max(peakOrcaPush, director.OrcaPushCount);

            for (var i = 0; i < director.PedestrianCount; i++)
            {
                var p = director.PedestrianPosition(i);
                maxPedDist = Math.Max(maxPedDist, director.Incident.DistanceTo(p.X, p.Y));
            }
        }

        _out.WriteLine(
            $"panicked={director.PanickedCount} peakOrcaPush={peakOrcaPush} " +
            $"pedestrians={director.PedestrianCount} maxPedDist={maxPedDist:F2} safeRadius={config.SafeRadius:F2}");

        Assert.True(director.PanickedCount > 0, "expected some cars near the incident to panic");
        Assert.True(peakOrcaPush > 0, "expected some cars to enter the Orca-push stage");
        Assert.True(director.PedestrianCount > 0, "expected some panicked+blocked cars to convert to pedestrians");
        Assert.True(maxPedDist >= 0.8 * config.SafeRadius,
            $"expected at least one pedestrian to flee to >= 0.8*SafeRadius ({0.8 * config.SafeRadius:F2}); " +
            $"observed max {maxPedDist:F2}");
    }

    // ----- T3.2: locality (the core Phase-5 property) -----

    [Fact]
    public void EvacStaysLocal()
    {
        var config = EvacOrganicScenario.DefaultConfig();
        var (engine, director) = EvacOrganicScenario.Build(TestRepoRoot, config: config);

        var everActive = new HashSet<VehicleHandle>();
        var trackedEntryDist = new Dictionary<VehicleHandle, double>();

        for (var step = 0; step < 300; step++)
        {
            // Snapshot the read surface BEFORE Tick() -- this is exactly what AutoTrackWorkingRegion
            // consults at the start of this tick's PreStep, so "first tracked" position == this snapshot.
            var handles = engine.VehicleHandles;
            var px = engine.PosX;
            var py = engine.PosY;
            var preTick = new (VehicleHandle Handle, double X, double Y)[handles.Length];
            for (var i = 0; i < handles.Length; i++)
            {
                preTick[i] = (handles[i], px[i], py[i]);
                everActive.Add(handles[i]);
            }

            director.Tick();

            foreach (var (h, x, y) in preTick)
            {
                if (director.IsTracked(h) && !trackedEntryDist.ContainsKey(h))
                {
                    trackedEntryDist[h] = director.Incident.DistanceTo(x, y);
                }
            }
        }

        var neverTracked = everActive.Count(h => !director.IsTracked(h));
        _out.WriteLine(
            $"everActiveDistinctCount={everActive.Count} trackedCount={trackedEntryDist.Count} " +
            $"neverTrackedCount={neverTracked} workingRadius={config.WorkingRadius:F2}");

        Assert.True(trackedEntryDist.Count > 0, "expected at least one vehicle to be auto-tracked");

        foreach (var (h, dist) in trackedEntryDist)
        {
            Assert.True(dist <= config.WorkingRadius + 1e-3,
                $"vehicle {h} was tracked at distance {dist:F2} > WorkingRadius {config.WorkingRadius:F2}");
        }

        Assert.True(neverTracked > 0,
            "expected at least one active vehicle to stay outside the working region and never be tracked " +
            "(proves the evac layer stays local, not city-wide)");
        Assert.True(trackedEntryDist.Count < everActive.Count,
            "expected the tracked set to be a STRICT subset of the ever-active set");
    }

    // ----- T3.3: containment + determinism -----

    [Fact]
    public void ContainmentAndDeterminism()
    {
        string RunAndSign()
        {
            var (_, director) = EvacOrganicScenario.Build(TestRepoRoot);

            for (var step = 0; step < 300; step++)
            {
                director.Tick();

                for (var i = 0; i < director.PedestrianCount; i++)
                {
                    var p = director.PedestrianPosition(i);
                    Assert.True(director.NavMesh.Contains(p),
                        $"pedestrian {i} left the navmesh at step {step}: ({p.X:F2},{p.Y:F2})");
                }

                foreach (var (x, y, _) in director.ActivePushers())
                {
                    Assert.True(director.NavMesh.Contains(new Vec2(x, y)),
                        $"pusher left the navmesh at step {step}: ({x:F2},{y:F2})");
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(director.PanickedCount).Append('|')
              .Append(director.ConvertedCount).Append('|')
              .Append(director.OrcaPushCount).Append('|')
              .Append(director.PedestrianCount).Append(';');

            for (var i = 0; i < director.PedestrianCount; i++)
            {
                var p = director.PedestrianPosition(i);
                sb.Append(p.X.ToString("R")).Append(',').Append(p.Y.ToString("R")).Append(';');
            }

            return sb.ToString();
        }

        var sigA = RunAndSign();
        var sigB = RunAndSign();

        _out.WriteLine($"sigA={sigA}");
        _out.WriteLine($"sigB={sigB}");
        Assert.Equal(sigA, sigB);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Traffic.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (Traffic.sln not found above test assembly).");
    }
}
