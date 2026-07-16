using System;
using System.IO;
using CityLib;
using Godot;

namespace Viewer;

// docs/DEMO-CITY3D-DESIGN.md "Code structure" / task T1.1 — the Godot glue skeleton. This node proves the
// packaged-consumer chain works INSIDE Godot: it builds a CityLib.SimSource over a real scenario, ticks
// the sim on a fixed sim-cadence accumulator, reconstructs per-frame vehicle poses via CityLib.Reconstructor,
// and logs a heartbeat — all headless, no rendering/meshes yet (that's T1.3+). No SumoSharp.* type is
// referenced directly here; everything computable comes through CityLib (repo rule: the Godot layer is
// thin glue only).
public partial class Main : Node3D
{
    // Matches scenarios/09-traffic-light/config.sumocfg's step-length (1s) -- the sim advances one Engine
    // step per simulated second; the Reconstructor still runs every rendered frame, which is the whole
    // point of the DR (dead-reckoning) motion story (design "Data path").
    private const double SimStepSeconds = 1.0;

    // Playout delay per design "Playout delay": a stable, small manual knob (~0.3-0.5s), not auto-driven.
    private const double PlayoutDelaySeconds = 0.4;

    // Quit after this many rendered frames -- enough to observe several sim ticks and a non-zero vehicle
    // count on scenarios/09-traffic-light, while keeping the headless smoke run fast. At a fixed 60 FPS
    // (see run-smoke.sh's `--fixed-fps 60`, needed because headless dummy-renderer wall-clock deltas are
    // far smaller than real-time and would otherwise take hundreds of real frames to accumulate one
    // simulated second) this covers ~3 sim ticks (SimStepSeconds=1).
    private const int QuitAfterFrames = 200;

    private SimSource? _sim;
    private Reconstructor? _reconstructor;
    private double _accumulator;
    private int _frame;

    public override void _Ready()
    {
        string repoRoot;
        try
        {
            repoRoot = FindRepoRoot();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Main: could not locate repo root (searched upward for Traffic.sln): {ex.Message}");
            GetTree().Quit(1);
            return;
        }

        var scenarioDir = Path.Combine(repoRoot, "scenarios", "09-traffic-light");
        var netPath = Path.Combine(scenarioDir, "net.net.xml");
        var rouPath = Path.Combine(scenarioDir, "rou.rou.xml");
        var cfgPath = Path.Combine(scenarioDir, "config.sumocfg");

        if (!File.Exists(netPath) || !File.Exists(rouPath) || !File.Exists(cfgPath))
        {
            GD.PrintErr(
                $"Main: scenario 'scenarios/09-traffic-light' not found under repo root '{repoRoot}' " +
                $"(expected {netPath}, {rouPath}, {cfgPath}).");
            GetTree().Quit(1);
            return;
        }

        try
        {
            _sim = new SimSource(netPath, rouPath, cfgPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Main: failed to construct SimSource: {ex}");
            GetTree().Quit(1);
            return;
        }

        _reconstructor = new Reconstructor();
        GD.Print($"Main: loaded scenario '09-traffic-light' from '{scenarioDir}'.");
    }

    public override void _Process(double delta)
    {
        if (_sim is null || _reconstructor is null)
        {
            // _Ready already reported the error and requested quit; nothing more to do this frame.
            return;
        }

        // Fixed sim-cadence accumulator: advance the sim in whole SimStepSeconds increments regardless of
        // the (headless-dummy-renderer) frame rate, while reconstruction still runs every _Process call.
        _accumulator += delta;
        while (_accumulator >= SimStepSeconds)
        {
            _sim.Tick();
            _accumulator -= SimStepSeconds;
        }

        var vehicles = _reconstructor.Reconstruct(_sim.Source, _sim.LocalLanes, PlayoutDelaySeconds);

        if (vehicles.Count > 0)
        {
            var v = vehicles[0];
            GD.Print(
                $"Main: frame={_frame} simTime={_sim.Time:F2} vehicles={vehicles.Count} " +
                $"v0=(x={v.X:F2}, z={v.Z:F2}, yaw={v.YawRad:F3})");
        }
        else
        {
            GD.Print($"Main: frame={_frame} simTime={_sim.Time:F2} vehicles=0");
        }

        _frame++;
        if (_frame >= QuitAfterFrames)
        {
            GD.Print($"Main: reached {QuitAfterFrames} frames, quitting.");
            _sim.Dispose();
            _sim = null;
            GetTree().Quit();
        }
    }

    // Resolve the repo root by searching upward from this project's own directory for Traffic.sln --
    // never a hardcoded absolute path (CLAUDE.md prime directive: the VM mount path is not stable).
    // ProjectSettings.GlobalizePath("res://") gives the absolute path Godot loaded this project from,
    // which is demos/City3D/Viewer in a normal checkout.
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Traffic.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "walked up from '" + ProjectSettings.GlobalizePath("res://") + "' without finding Traffic.sln");
    }
}
