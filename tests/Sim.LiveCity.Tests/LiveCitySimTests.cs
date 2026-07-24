using System;
using System.Diagnostics;
using System.IO;
using Sim.LiveCity;
using Xunit;

namespace Sim.LiveCity.Tests;

public class LiveCitySimTests
{
    // Resolve the repo root the same way CLAUDE.md prescribes ("git rev-parse --show-toplevel"), with a
    // walk-up-from-AppContext.BaseDirectory fallback for an environment without git on PATH.
    private static string RepoRoot()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --show-toplevel")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            using var proc = Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (proc.ExitCode == 0 && Directory.Exists(Path.Combine(output, "scenarios")))
            {
                return output;
            }
        }
        catch
        {
            // fall through to the walk-up fallback
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "scenarios")) && File.Exists(Path.Combine(dir.FullName, "Traffic.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not resolve the SumoSharp repo root.");
    }

    private static LiveCityConfig MakeConfig(bool yield = true, double? dt = null)
    {
        var cfg = LiveCityConfig.ForRepoRoot(RepoRoot());
        cfg.YieldEnabled = yield;
        if (dt is { } d)
        {
            cfg.Dt = d;
        }

        return cfg;
    }

    [Fact]
    public void CoupledSim_OverAFewMinutes_ProducesCarsPedsAndYieldEvents()
    {
        using var sim = new LiveCitySim(MakeConfig(yield: true));

        for (var i = 0; i < 120; i++)
        {
            sim.Step();
        }

        Assert.True(sim.PeakCars > 0, $"expected PeakCars > 0, got {sim.PeakCars}");
        Assert.True(sim.PeakPeds > 0, $"expected PeakPeds > 0, got {sim.PeakPeds}");
        Assert.True(sim.PeakOccupiedCrossings > 0, $"expected PeakOccupiedCrossings > 0, got {sim.PeakOccupiedCrossings}");
        Assert.True(sim.CarYieldObservations > 0, $"expected CarYieldObservations > 0, got {sim.CarYieldObservations}");

        // Wire non-vacuousness: pump both sources and assert something real arrived.
        sim.VehicleSource.Pump();
        Assert.True(sim.VehicleSource.History.Count > 0, "expected >=1 vehicle in the replicated History");

        sim.PedSource.Pump();
        Assert.True(sim.PedSource.LatestCrowdFrame.Count > 0, "expected >=1 ped in the latest crowd frame");
    }

    [Fact]
    public void TwoRuns_SameConfig_AreByteExactDeterministic()
    {
        using var simA = new LiveCitySim(MakeConfig(yield: true));
        using var simB = new LiveCitySim(MakeConfig(yield: true));

        for (var step = 0; step < 120; step++)
        {
            simA.Step();
            simB.Step();

            var snapA = simA.Sample();
            var snapB = simB.Sample();

            Assert.Equal(snapA.Cars.Count, snapB.Cars.Count);
            for (var i = 0; i < snapA.Cars.Count; i++)
            {
                var a = snapA.Cars[i];
                var b = snapB.Cars[i];
                Assert.Equal(a.Handle, b.Handle);
                Assert.Equal(a.X, b.X);
                Assert.Equal(a.Y, b.Y);
                Assert.Equal(a.Z, b.Z);
                Assert.Equal(a.AngleDeg, b.AngleDeg);
            }

            Assert.Equal(snapA.Peds.Count, snapB.Peds.Count);
            for (var i = 0; i < snapA.Peds.Count; i++)
            {
                var a = snapA.Peds[i];
                var b = snapB.Peds[i];
                Assert.Equal(a.Id, b.Id);
                Assert.Equal(a.X, b.X);
                Assert.Equal(a.Y, b.Y);
                Assert.Equal(a.Regime, b.Regime);
            }
        }
    }

    // docs/LIVE-CITY-VISUALS-NOTES.md (tick-rate task), deliverable 1: the SAME determinism proof as
    // TwoRuns_SameConfig_AreByteExactDeterministic above, but at Dt=0.1 (10 Hz, cfg.SimHz's non-default
    // side) instead of the 0.5 (2 Hz) default -- proves LiveCityConfig.Dt/SimHz plumbs all the way through
    // to LiveCitySim's engine step-length (via the InvariantCulture-formatted config XML) and the ped
    // demand's stepDt without breaking either the coupled sim's liveness (cars>0 && peds>0) or its
    // byte-exact determinism (same seed+Dt => identical run).
    [Fact]
    public void TwoRuns_AtTenHz_AreByteExactDeterministic_AndProduceCarsAndPeds()
    {
        using var simA = new LiveCitySim(MakeConfig(yield: true, dt: 0.1));
        using var simB = new LiveCitySim(MakeConfig(yield: true, dt: 0.1));

        for (var step = 0; step < 120; step++)
        {
            simA.Step();
            simB.Step();

            var snapA = simA.Sample();
            var snapB = simB.Sample();

            Assert.Equal(snapA.Cars.Count, snapB.Cars.Count);
            for (var i = 0; i < snapA.Cars.Count; i++)
            {
                var a = snapA.Cars[i];
                var b = snapB.Cars[i];
                Assert.Equal(a.Handle, b.Handle);
                Assert.Equal(a.X, b.X);
                Assert.Equal(a.Y, b.Y);
                Assert.Equal(a.Z, b.Z);
                Assert.Equal(a.AngleDeg, b.AngleDeg);
            }

            Assert.Equal(snapA.Peds.Count, snapB.Peds.Count);
            for (var i = 0; i < snapA.Peds.Count; i++)
            {
                var a = snapA.Peds[i];
                var b = snapB.Peds[i];
                Assert.Equal(a.Id, b.Id);
                Assert.Equal(a.X, b.X);
                Assert.Equal(a.Y, b.Y);
                Assert.Equal(a.Regime, b.Regime);
            }
        }

        Assert.True(simA.PeakCars > 0, $"expected PeakCars > 0 at Dt=0.1, got {simA.PeakCars}");
        Assert.True(simA.PeakPeds > 0, $"expected PeakPeds > 0 at Dt=0.1, got {simA.PeakPeds}");
    }

    // #15 LIVENESS / THROUGHPUT regression guard (docs/LIVE-CITY-15-RESUME.md §2 item 3).
    // The parity gate structurally CANNOT catch the #15 junction-gridlock class of bug: every #15 fix is
    // demo-gated and INERT on every golden, so a change that silently reforms the dense-flow gridlock passes
    // parity byte-for-byte. This test is the missing guard: it runs the coupled live-city sim ~1000 s
    // headless (no SUMO, committed demo inputs) with the shipped dense-flow config PINNED (immune to stray
    // LIVECITY_* env vars) and asserts the sim keeps DISCHARGING -- arrivals keep climbing to the end and the
    // late stopped fraction never pins near 1.0. It is deterministic (same seed+config => identical run, see
    // TwoRuns_SameConfig_AreByteExactDeterministic), so the thresholds are stable, not statistical.
    //
    // Measured separation (this branch, first-hand) at 2000 steps = 1000 s:
    //   healthy (shipped)      : final arrivals ~736, last-400-step growth ~+145, late stoppedFrac avg ~0.35
    //   a #15 gridlock regress : final arrivals ~361 (flatlined by t~900), last-window growth ~+2, frac ~1.0
    // The thresholds below sit with wide margin on BOTH sides of that gap, so healthy flow never flakes red
    // while any gridlock regression (the arrivals flatline is the sharpest signal) trips it.
    [Fact]
    public void DenseFlow_OverAThousandSeconds_KeepsDischarging_NoGridlock()
    {
        var cfg = LiveCityConfig.ForRepoRoot(RepoRoot());
        // Pin the scenario so the assertion is about ENGINE behaviour, not config/env drift: dense-flow
        // demand (160 cars), teleport OFF (a teleport would mask a jam by removing stuck cars), 2 Hz, and the
        // shipped #15 dense-flow knobs explicitly ON (cooperative LC + the into-occupied vetoes).
        cfg.CarTargetConcurrent = 160;
        cfg.TimeToTeleportSeconds = 0.0;
        cfg.Dt = 0.5;
        cfg.YieldEnabled = true;
        cfg.CooperativeLaneChange = true;
        cfg.MergeStoppedMinGap = 5.0;
        cfg.MergeStoppedStrategicDeferDist = 15.0;

        using var sim = new LiveCitySim(cfg);

        const int totalSteps = 2000;       // 1000 s at dt=0.5 -- long enough that a gridlock has fully set in
        const int lateWindow = 400;        // final 200 s used for the anti-flatline + stopped-fraction checks
        var arrivalsAtLateWindowStart = 0L;
        var lastPos = new System.Collections.Generic.Dictionary<Sim.Core.VehicleHandle, (double X, double Y)>();
        var lateMatched = 0;
        var lateStopped = 0;

        for (var i = 0; i < totalSteps; i++)
        {
            sim.Step();
            var snap = sim.Sample();

            var inLateWindow = i >= totalSteps - lateWindow;
            if (i == totalSteps - lateWindow)
            {
                arrivalsAtLateWindowStart = sim.ArrivedTotal;
            }

            // Displacement-based stopped fraction, computed EXACTLY as the LIVECITY-GRIDLOCK smoke probe does
            // (per-handle frame-to-frame move < 0.05 m => "stopped"), accumulated over the late window only.
            var cur = new System.Collections.Generic.Dictionary<Sim.Core.VehicleHandle, (double X, double Y)>(snap.Cars.Count);
            foreach (var c in snap.Cars)
            {
                cur[c.Handle] = (c.X, c.Y);
                if (inLateWindow && lastPos.TryGetValue(c.Handle, out var prev))
                {
                    var d = Math.Sqrt(((c.X - prev.X) * (c.X - prev.X)) + ((c.Y - prev.Y) * (c.Y - prev.Y)));
                    lateMatched++;
                    if (d < 0.05) lateStopped++;
                }
            }

            lastPos = cur;
        }

        var finalArrivals = sim.ArrivedTotal;
        var lateGrowth = finalArrivals - arrivalsAtLateWindowStart;
        var lateStoppedFrac = lateMatched > 0 ? (double)lateStopped / lateMatched : 1.0;

        Assert.True(sim.PeakCars > 0, $"expected PeakCars > 0, got {sim.PeakCars}");
        // (1) Total throughput: healthy ~736; a gridlock flatlines ~361. 450 sits between with margin.
        Assert.True(finalArrivals >= 450,
            $"THROUGHPUT regression: expected >= 450 arrivals over 1000 s, got {finalArrivals} (gridlock reforms at ~360)");
        // (2) Anti-flatline (sharpest gridlock signal): arrivals must KEEP climbing to the end. Healthy
        //     grows ~+145 in the last 200 s; a gridlock grows ~+2. 40 firmly separates them.
        Assert.True(lateGrowth >= 40,
            $"GRIDLOCK: arrivals flatlined -- only {lateGrowth} arrivals in the last {lateWindow} steps (healthy ~145, gridlock ~2)");
        // (3) The sim is not frozen: late stopped fraction must not pin near 1.0. Healthy avg ~0.35;
        //     a terminal gridlock ~1.0. 0.85 catches the freeze while tolerating heavy-but-flowing jams.
        Assert.True(lateStoppedFrac <= 0.85,
            $"GRIDLOCK: late stopped fraction {lateStoppedFrac:F2} pinned high (healthy ~0.35, frozen ~1.0)");
    }

    [Fact]
    public void YieldOnVsOff_ProduceDifferentCoupling_AndYieldOnIsPositive()
    {
        using var simOn = new LiveCitySim(MakeConfig(yield: true));
        using var simOff = new LiveCitySim(MakeConfig(yield: false));

        var trajectoryDiffers = false;

        for (var step = 0; step < 120; step++)
        {
            simOn.Step();
            simOff.Step();

            var onSnap = simOn.Sample();
            var offSnap = simOff.Sample();

            if (onSnap.Cars.Count != offSnap.Cars.Count)
            {
                trajectoryDiffers = true;
                continue;
            }

            for (var i = 0; i < onSnap.Cars.Count; i++)
            {
                if (Math.Abs(onSnap.Cars[i].X - offSnap.Cars[i].X) > 1e-9
                    || Math.Abs(onSnap.Cars[i].Y - offSnap.Cars[i].Y) > 1e-9)
                {
                    trajectoryDiffers = true;
                    break;
                }
            }
        }

        Assert.True(simOn.CarYieldObservations > 0, $"expected yield-ON CarYieldObservations > 0, got {simOn.CarYieldObservations}");
        Assert.True(
            trajectoryDiffers || simOn.CarYieldObservations != simOff.CarYieldObservations,
            $"expected yield ON/OFF to differ: onObs={simOn.CarYieldObservations} offObs={simOff.CarYieldObservations} trajectoryDiffers={trajectoryDiffers}");
    }
}
