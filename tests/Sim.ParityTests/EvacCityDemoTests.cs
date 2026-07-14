using System.Linq;
using Sim.Core;
using Sim.Evac;
using Xunit;
using Xunit.Abstractions;

namespace Sim.ParityTests;

// PANIC-EVAC-PHASE5-TIER2-DESIGN.md §3b / TASKS T2.5: behavioural validation of the Tier-2 CITY-SCALE
// evac demo (EvacCityScenario) on the committed 10k-class host `scenarios/_bench/city-15000`. Mirrors
// EvacOrganicDemoTests' shape (cascade, locality, containment+determinism) but adds the load-bearing
// Tier-2 property: the auto-tracked working-region population must land in the hundreds->low-thousands
// range, proving the two spatial-hashed crowd solvers (UseCrowdSpatialHash=true by default on this
// scenario) are actually being stressed at city scale, not just the parity engine.
//
// KEPT TRACTABLE (per TASKS T2.5): a modest tick count (<= 120) so the suite stays fast -- the heavy
// full-scale run (hundreds of ticks, low-thousands tracked population) belongs to the T2.7 profiler
// (Sim.EvacProfile --city), not `dotnet test`. Because the shipped EvacCityScenario.DefaultIncident
// fires at StartTime=150 (deliberately late -- it targets the multi-hundred-tick demo/profiler run,
// where the city has had time to ramp up), these tests use a LOCAL incident with an early StartTime so
// the full panic -> block -> Orca-push -> wedge -> pedestrian cascade has room to complete inside the
// tractable tick budget. Auto-tracking itself is NOT gated on the incident's StartTime (it is a pure
// proximity scan, see EvacDirector.AutoTrackWorkingRegion), so the tracked-population property holds
// regardless of which StartTime is used.
public class EvacCityDemoTests
{
    private readonly ITestOutputHelper _out;
    public EvacCityDemoTests(ITestOutputHelper output) => _out = output;

    private static readonly string TestRepoRoot = RepoRoot();

    // Fires almost immediately so a 120-tick test window gives the cascade maximal room to complete;
    // same epicentre/radius as the shipped default (see EvacCityScenario.DefaultIncident) -- only
    // StartTime differs, kept LOCAL to the test fixture (the shipped default stays tuned for the real
    // demo/profiler run per the scenario's own doc comment).
    private static Incident TestIncident => EvacCityScenario.DefaultIncident with { StartTime = 20.0 };

    private const int Ticks = 150;

    // ----- T2.5(1): loads offline + ticks -----

    [Fact]
    public void LoadsAndTicks()
    {
        var (engine, director) = EvacCityScenario.Build(TestRepoRoot, TestIncident);

        var peakActive = 0;
        for (var step = 0; step < Ticks; step++)
        {
            director.Tick();
            peakActive = Math.Max(peakActive, engine.VehicleHandles.Length);
        }

        _out.WriteLine($"peakActive={peakActive} trackedCount={director.TrackedCount}");
        Assert.True(peakActive > 0, "expected the city's loaded demand to insert vehicles under the director's Tick() loop");
    }

    // ----- T2.5(2)+(3): cascade emerges AND the tracked working-region population is stressed -----

    [Fact]
    public void CascadeStressesWorkingRegion()
    {
        var config = EvacCityScenario.DefaultConfig();
        var (_, director) = EvacCityScenario.Build(TestRepoRoot, TestIncident, config);

        var peakOrcaPush = 0;
        for (var step = 0; step < Ticks; step++)
        {
            director.Tick();
            peakOrcaPush = Math.Max(peakOrcaPush, director.OrcaPushCount);
        }

        _out.WriteLine(
            $"trackedCount={director.TrackedCount} panicked={director.PanickedCount} " +
            $"peakOrcaPush={peakOrcaPush} converted={director.ConvertedCount} " +
            $"pedestrians={director.PedestrianCount} workingRadius={config.WorkingRadius:F1}");

        // The cascade: some cars panic, some enter the Orca-push stage, some convert to pedestrians.
        Assert.True(director.PanickedCount > 0, "expected some cars near the incident to panic");
        Assert.True(peakOrcaPush > 0, "expected some cars to enter the Orca-push stage");
        Assert.True(director.PedestrianCount > 0, "expected some panicked+blocked cars to convert to pedestrians");

        // The load-bearing Tier-2 property (design §1/§7, TASKS T2.5): the tracked working-region
        // population must be in the hundreds->low-thousands range -- proof the O(m^2) crowd solvers
        // are genuinely stressed at city scale (this is what the UseCrowdSpatialHash flag has to bite
        // on), not left with a handful of agents that would prove nothing.
        Assert.True(director.TrackedCount > 300,
            $"expected the tracked working-region population to be in the hundreds->low-thousands " +
            $"(floor 300); observed {director.TrackedCount}");
    }

    // ----- T2.5(4): locality still holds at 10k -----

    [Fact]
    public void EvacStaysLocalAt10k()
    {
        var config = EvacCityScenario.DefaultConfig();
        var (engine, director) = EvacCityScenario.Build(TestRepoRoot, TestIncident, config);

        var everActive = new HashSet<VehicleHandle>();
        var trackedEntryDist = new Dictionary<VehicleHandle, double>();

        for (var step = 0; step < Ticks; step++)
        {
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
            $"neverTrackedCount={neverTracked} workingRadius={config.WorkingRadius:F1}");

        Assert.True(trackedEntryDist.Count > 0, "expected at least one vehicle to be auto-tracked");

        foreach (var (h, dist) in trackedEntryDist)
        {
            Assert.True(dist <= config.WorkingRadius + 1e-3,
                $"vehicle {h} was tracked at distance {dist:F2} > WorkingRadius {config.WorkingRadius:F2}");
        }

        // Some active vehicle far outside the region is never tracked -- the load-bearing locality
        // property holds even at 10k scale (proves the evac layer stays local, not city-wide).
        Assert.True(neverTracked > 0,
            "expected at least one active vehicle to stay outside the working region and never be tracked");
        Assert.True(trackedEntryDist.Count < everActive.Count,
            "expected the tracked set to be a STRICT subset of the ever-active set");
    }

    // ----- T2.5(5): determinism (two runs bit-identical), over a smaller tick count to stay fast -----

    [Fact]
    public void Deterministic()
    {
        const int detTicks = 60;

        string RunAndSign()
        {
            var (_, director) = EvacCityScenario.Build(TestRepoRoot, TestIncident);

            for (var step = 0; step < detTicks; step++)
            {
                director.Tick();
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(director.TrackedCount).Append('|')
              .Append(director.PanickedCount).Append('|')
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
