using System.Linq;
using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// P2-H acceptance (docs/HIGH-DENSITY-P2H-DESIGN.md §5): SUMO's `--max-depart-delay` insertion-backlog
// eviction anchor. scenarios/50-max-depart-delay is a single-lane edge AB (500 m, speed 13.89,
// sigma=0) with a `blocker` vehicle that departs at t=0, departPos=0, and immediately stops
// (`<stop lane="AB_0" startPos="0" endPos="10" duration="10"/>`) right in the insertion zone, plus
// 12 follower vehicles v1..v12 departing every 1 s (depart 1..12), all requesting departPos=0 on the
// same lane. `config.sumocfg` sets `<processing><max-depart-delay value="5"/></processing>`.
//
// Vanilla SUMO 1.20.0 on this scenario inserts only 4 of the 13 demand vehicles -- blocker, v1, v10,
// v12 -- and DELETES the other 9 (v2..v9, v11) because each waited more than 5 s past its own depart
// time without finding an insertion gap (MSInsertionControl.cpp:168, `deleteVehicle(veh, true)`).
// v10 and v12 are the load-bearing edge case: each is inserted on the EXACT simulation step it would
// otherwise have crossed the eviction threshold, because SUMO (and the ported engine) attempt
// insertion BEFORE checking the delay -- a vehicle that can insert on the step it crosses the
// threshold still departs (verified in golden.fcd.xml: v10 first appears at t=16 with pos=0, and
// 10 + 6 = 16 is exactly its eviction step; v12 first appears at t=18, and 12 + 6 = 18 is exactly its
// eviction step).
//
// Before the P2-H fix, `Engine.InsertDepartingVehicles` had no delay-based eviction at all -- a
// pending vehicle that could not find a gap was retried forever, so v2..v9 and v11 would still be
// sitting in the un-departed backlog rather than being dropped, and `Engine.DiscardedDepartureCount`
// would stay 0. With the fix (`EvictOverdueDeparture`, gated on `_config.MaxDepartDelay >= 0.0` and
// `time - v.Def.Depart > _config.MaxDepartDelay`), the engine matches SUMO bit-exactly.
public class RungHDp2hMaxDepartDelayParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "50-max-depart-delay");
    private const int End = 80;

    private static readonly string[] DroppedVehicleIds = { "v2", "v3", "v4", "v5", "v6", "v7", "v8", "v9", "v11" };
    private static readonly string[] PresentVehicleIds = { "blocker", "v1", "v10", "v12" };

    [Fact]
    public void MaxDepartDelay_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(End);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch, BuildFailureMessage(result));
    }

    // Non-vacuous presence assertion (docs/HIGH-DENSITY-P2H-DESIGN.md §5): the 9 vehicles that SUMO
    // deleted for waiting past max-depart-delay must be ABSENT from the engine's trajectory, and the
    // 4 vehicles SUMO actually inserted (including the two exact-threshold-step inserts, v10 and v12)
    // must be PRESENT. Without the P2-H fix this fails: an unbounded-retry engine would eventually
    // insert every one of v2..v9 and v11 once the road cleared, so they would appear in
    // actual.VehicleIds (or the trajectory would diverge trying to reconcile positions), and this
    // assertion would fail against the pre-fix engine.
    [Fact]
    public void MaxDepartDelay_DropsOverdueVehiclesAndKeepsInsertedOnes()
    {
        var engine = new Engine();
        engine.LoadScenario(Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(End);
        var vehicleIds = actual.VehicleIds;

        foreach (var droppedId in DroppedVehicleIds)
        {
            Assert.False(
                vehicleIds.Contains(droppedId),
                $"vehicle {droppedId} should have been evicted by max-depart-delay but appears in the engine trajectory.");
        }

        foreach (var presentId in PresentVehicleIds)
        {
            Assert.True(
                vehicleIds.Contains(presentId),
                $"vehicle {presentId} should have inserted normally but is missing from the engine trajectory.");
        }
    }

    // Load-bearing max-depart-delay assertion (docs/HIGH-DENSITY-P2H-DESIGN.md §2/§3/§5): this is the
    // direct observability check on the eviction tally itself, independent of the FCD trajectory.
    // `Engine.DiscardedDepartureCount` must equal exactly 9 -- the number of demand vehicles
    // (v2,v3,v4,v5,v6,v7,v8,v9,v11) that waited longer than the scenario's max-depart-delay=5 without
    // finding an insertion gap. Without the P2-H fix, `InsertDepartingVehicles` never evicts anyone:
    // every candidate that fails to insert is simply retried again next step forever, so
    // `_discardedDepartures` never increments and this count would stay 0 -- both this assertion and
    // the presence assertion above fail against the pre-fix engine (confirmed during authoring: the
    // fix is what makes the backlog vehicles disappear instead of lingering as an un-departed queue).
    [Fact]
    public void MaxDepartDelay_DiscardedDepartureCountMatchesDroppedVehicleCount()
    {
        var engine = new Engine();
        engine.LoadScenario(Path.Combine(ScenarioDir, "config.sumocfg"));

        engine.Run(End);

        Assert.Equal(DroppedVehicleIds.Length, engine.DiscardedDepartureCount);
    }

    private static string BuildFailureMessage(ComparisonResult result)
    {
        var lines = new List<string>
        {
            $"P2-H max-depart-delay parity FAILED. FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}",
        };
        foreach (var attribute in result.Attributes)
        {
            lines.Add($"  attribute={attribute.Attribute} maxAbsError={attribute.MaxAbsError} rmse={attribute.Rmse} withinTolerance={attribute.WithinTolerance}");
        }
        if (result.PresenceMismatches.Count > 0)
        {
            lines.Add("  presence mismatches (first 10):");
            foreach (var mismatch in result.PresenceMismatches.Take(10))
            {
                lines.Add($"    {mismatch.Kind} vehicle={mismatch.VehicleId} time={mismatch.Time?.ToString() ?? "n/a"}");
            }
        }
        return string.Join(Environment.NewLine, lines);
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
