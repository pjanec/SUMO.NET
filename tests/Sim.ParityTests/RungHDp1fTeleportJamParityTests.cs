using System.Globalization;
using Sim.Core;
using Sim.Harness;
using Xunit;

namespace Sim.ParityTests;

// P1-F (docs/HIGH-DENSITY-P1F-DESIGN.md): the bounded teleport valve (SUMO's `time-to-teleport`
// jam mechanism). Acceptance scenario scenarios/47-teleport-jam -- a single-lane edge eA->eB where
// a parked <stop>-ped blocker near the end of eA queues a follower that cannot pass. With
// time-to-teleport=120 the follower accumulates WaitingTime and, at 121s stuck (strict `>`),
// teleports onto eB; the parked blocker (isStopped) never accumulates and never teleports. Golden
// from vanilla SUMO 1.20.0: the follower's position jumps discontinuously eA(pos 982.499, speed 0)
// at t=199 to eB(pos 5.0, speed 13.89) at t=200; golden.statistic.xml shows teleports total=1,
// jam=1.
public class RungHDp1fTeleportJamParityTests
{
    private static readonly string ScenarioDir = Path.Combine(RepoRoot(), "scenarios", "47-teleport-jam");
    private static readonly string NetPath = Path.Combine(ScenarioDir, "net.net.xml");
    private static readonly string RouPath = Path.Combine(ScenarioDir, "rou.rou.xml");

    // The primary parity gate: the full FCD trajectory (including the follower's discontinuous
    // eA->eB jump at t=200 to pos 5.0 / speed 13.89) must match the golden within tolerance.json.
    [Fact]
    public void Run300Steps_MatchesGoldenFcdWithinTolerance()
    {
        var engine = new Engine();
        engine.LoadScenario(NetPath, RouPath, Path.Combine(ScenarioDir, "config.sumocfg"));

        var actual = engine.Run(300);
        var golden = FcdParser.Parse(Path.Combine(ScenarioDir, "golden.fcd.xml"));
        var tolerance = ToleranceConfig.Load(Path.Combine(ScenarioDir, "tolerance.json"));

        var result = TrajectoryComparator.Compare(actual, golden, tolerance);

        Assert.True(result.IsMatch,
            "scenario 47 teleport-jam FCD parity FAILED. " +
            $"FirstDivergenceStep={result.FirstDivergenceStep?.ToString() ?? "none"}; " +
            string.Join(" ", result.Attributes.Select(a =>
                $"{a.Attribute}(maxAbs={a.MaxAbsError:G6},rmse={a.Rmse:G6},ok={a.WithinTolerance})")));
    }

    // The teleport count gate: exactly one jam-teleport over the run, matching golden.statistic.xml.
    [Fact]
    public void Run300Steps_TeleportCount_MatchesGoldenStatistic()
    {
        var engine = new Engine();
        engine.LoadScenario(NetPath, RouPath, Path.Combine(ScenarioDir, "config.sumocfg"));

        engine.Run(300);

        var golden = StatisticOutputParser.Parse(Path.Combine(ScenarioDir, "golden.statistic.xml"));

        Assert.Equal(1, golden.TeleportsTotal);
        Assert.Equal(1, golden.TeleportsJam);
        Assert.Equal(golden.TeleportsTotal, engine.TeleportCount);
        Assert.Equal(golden.TeleportsJam, engine.TeleportCountJam);
    }

    // The teleport lands the follower on eB at pos 5.0 / speed 13.89 at t=200, and it is still on
    // eA at t=199 -- i.e. the follower did NOT teleport when its WaitingTime was exactly 120 (frame
    // 199 reflects that step's move), and DID once it exceeded 120. The parked blocker never moves.
    [Fact]
    public void Follower_JumpsToEBAtStep200_BlockerNeverTeleports()
    {
        var engine = new Engine();
        engine.LoadScenario(NetPath, RouPath, Path.Combine(ScenarioDir, "config.sumocfg"));
        var actual = engine.Run(300);

        Assert.True(actual.TryGet("follower", 199.0, out var before));
        Assert.Equal("eA_0", before.Lane);
        Assert.Equal(982.499, before.Pos, 3);
        Assert.Equal(0.0, before.Speed, 3);

        Assert.True(actual.TryGet("follower", 200.0, out var after));
        Assert.Equal("eB_0", after.Lane);
        Assert.Equal(5.0, after.Pos, 3);
        Assert.Equal(13.89, after.Speed, 3);

        // The blocker (a scheduled <stop>) never accumulates WaitingTime, so it never teleports --
        // it sits at eA pos 990 for the whole run.
        Assert.True(actual.TryGet("blocker", 200.0, out var blocker));
        Assert.Equal("eA_0", blocker.Lane);
        Assert.Equal(990.0, blocker.Pos, 3);
    }

    // Strict `>` boundary (design §6 risk 1): with time-to-teleport=121 the follower needs ONE MORE
    // second of waiting than with 120, so it has NOT yet teleported at t=200 (still on eA) whereas
    // the ttt=120 run has (proved above). This isolates the strict-`>` semantics: WaitingTime==ttt
    // does not fire; WaitingTime>ttt does.
    [Fact]
    public void StrictGreaterThanBoundary_HigherThresholdDelaysTeleportByOneStep()
    {
        var cfg120 = WriteConfig(timeToTeleport: 120);
        var cfg121 = WriteConfig(timeToTeleport: 121);

        var e120 = new Engine();
        e120.LoadScenario(NetPath, RouPath, cfg120);
        var t120 = e120.Run(300);

        var e121 = new Engine();
        e121.LoadScenario(NetPath, RouPath, cfg121);
        var t121 = e121.Run(300);

        // ttt=120: teleported by frame 200 (on eB).
        Assert.True(t120.TryGet("follower", 200.0, out var f120));
        Assert.Equal("eB_0", f120.Lane);

        // ttt=121: one more second of waiting required, so still on eA at frame 200.
        Assert.True(t121.TryGet("follower", 200.0, out var f121));
        Assert.Equal("eA_0", f121.Lane);
        Assert.Equal(982.499, f121.Pos, 3);

        // ...and it teleports one step later (frame 201 on eB).
        Assert.True(t121.TryGet("follower", 201.0, out var f121Next));
        Assert.Equal("eB_0", f121Next.Lane);

        Assert.Equal(1, e120.TeleportCount);
        Assert.Equal(1, e121.TeleportCount);
    }

    // time-to-teleport.remove variant (design §1D, §2): the vehicle is REMOVED instead of
    // re-inserted -- it must not reappear on eB, but it still counts as a jam-teleport.
    [Fact]
    public void RemoveVariant_RemovesFollowerInsteadOfReinserting_StillCountsJam()
    {
        var cfg = WriteConfig(timeToTeleport: 120, remove: true);

        var engine = new Engine();
        engine.LoadScenario(NetPath, RouPath, cfg);
        var actual = engine.Run(300);

        // Present + stuck on eA just before the teleport step.
        Assert.True(actual.TryGet("follower", 199.0, out var before));
        Assert.Equal("eA_0", before.Lane);

        // Removed at teleport: never re-inserted anywhere, at t=200 or any later frame.
        var follower = actual.PointsFor("follower");
        Assert.DoesNotContain(follower.Keys, t => t >= 200.0);

        // Still counted as exactly one jam-teleport.
        Assert.Equal(1, engine.TeleportCount);
        Assert.Equal(1, engine.TeleportCountJam);
    }

    // Inert when the valve is off (design §2, the byte-identical guarantee): with
    // time-to-teleport=-1 (every phase-1 scenario's setting) no teleport ever fires -- the follower
    // stays jammed on eA for the whole run and TeleportCount stays 0.
    [Fact]
    public void InertWhenOff_NoTeleport_FollowerStaysJammedOnEA()
    {
        var cfg = WriteConfig(timeToTeleport: -1);

        var engine = new Engine();
        engine.LoadScenario(NetPath, RouPath, cfg);
        var actual = engine.Run(300);

        Assert.Equal(0, engine.TeleportCount);
        Assert.Equal(0, engine.TeleportCountJam);

        // Still on eA, still stuck behind the blocker, at the last frame.
        Assert.True(actual.TryGet("follower", 299.0, out var last));
        Assert.Equal("eA_0", last.Lane);
        Assert.Equal(982.499, last.Pos, 3);
    }

    // Writes a minimal sumocfg (the net/rou come from the 3-arg LoadScenario overload's explicit
    // paths, so only <time>/<processing> matter here) into the session scratch dir.
    private static string WriteConfig(double timeToTeleport, bool remove = false)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"p1f-ttt{timeToTeleport.ToString(CultureInfo.InvariantCulture)}-rm{remove}-{Guid.NewGuid():N}.sumocfg");
        var removeLine = remove ? "        <time-to-teleport.remove value=\"true\"/>\n" : string.Empty;
        File.WriteAllText(path,
            "<configuration>\n" +
            "    <time><begin value=\"0\"/><end value=\"300\"/><step-length value=\"1\"/></time>\n" +
            "    <processing>\n" +
            "        <step-method.ballistic value=\"false\"/>\n" +
            $"        <time-to-teleport value=\"{timeToTeleport.ToString(CultureInfo.InvariantCulture)}\"/>\n" +
            removeLine +
            "        <default.action-step-length value=\"1\"/>\n" +
            "        <default.speeddev value=\"0\"/>\n" +
            "        <collision.action value=\"none\"/>\n" +
            "    </processing>\n" +
            "    <random_number><seed value=\"42\"/></random_number>\n" +
            "</configuration>\n");
        return path;
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
