using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Sim.Core;
using Sim.LiveCity;
using Sim.Viewer.Raylib;

namespace Sim.Viewer;

// docs/LIVE-CITY-VIEWERS-DESIGN.md §1/§5, docs/LIVE-CITY-VIEWERS-TASKS.md Stage B (B1/B2): the ONLY place
// in the demo tool that references Sim.LiveCity -- mirrors EvacOverlay/PedOverlay's shape (a generic
// IRenderOverlay client, Sim.Viewer.Core/Sim.Viewer.Raylib stay domain-agnostic). Unlike those two
// overlays, this one does NOT build/step an Engine itself (RunLiveCity in Program.cs owns the LiveCitySim
// and calls UpdateSnapshot after each Step()); it is a pure "draw the latest coupled-sim snapshot"
// presenter plus the click-to-identify hit-test, so PickNearest is trivially unit-testable without a
// window (B2's success condition).
public sealed class LiveCityOverlay : IRenderOverlay
{
    // Regime -> colour, mirrored from PedOverlay/EvacOverlay's palette conventions and the task's own
    // mapping: low-power weave = grey (calm/ambient), high-power = orange (promoted/reactive), paused =
    // yellow (dwell).
    private static readonly Color LowPowerColor = new(148, 163, 184, 255); // grey
    private static readonly Color HighPowerColor = new(251, 146, 60, 255); // orange
    private static readonly Color PausedColor = new(250, 204, 21, 255); // yellow
    private static readonly Color SelectionRingColor = new(56, 189, 248, 255); // cyan
    private static readonly Color SelectionLabelColor = new(226, 232, 240, 255); // near-white

    public const float PedRadius = 0.3f; // metres, per the task spec.
    public const double DefaultPickRadius = 4.0; // metres, per the task spec.

    // Published by RunLiveCity's PumpFrame (the engine-stepping side) right after each sim.Step(), read by
    // the draw methods (the render side) -- same immutable-snapshot-per-frame discipline EvacOverlay's
    // `volatile EvacRenderSnapshot? _snap` and PedOverlay's `volatile PedRenderPoint[] _snapshot` use, so a
    // future background-stepping variant of RunLiveCity stays safe without any change here.
    private volatile LiveCitySnapshot _snapshot = EmptySnapshot;

    private static readonly LiveCitySnapshot EmptySnapshot =
        new(Array.Empty<LiveCityCar>(), Array.Empty<LiveCityPed>(), 0);

    private VehicleHandle? _selectedHandle;
    private string? _selectedName;

    // The latest sampled frame -- exposed for a headless caller (mirrors EvacOverlay.Snapshot/
    // PedOverlay.Snapshot) and for RunLiveCity's own HUD/diagnostics logging.
    public LiveCitySnapshot Snapshot => _snapshot;

    // The currently click-selected vehicle's identity, or null if nothing is selected yet.
    public string? SelectedName => _selectedName;

    public void UpdateSnapshot(LiveCitySnapshot snapshot) => _snapshot = snapshot;

    // docs/LIVE-CITY-VIEWERS-DESIGN.md §5: this overlay wants world clicks (nearest-vehicle pick) instead
    // of the generic viewer's default "drop an obstacle" behaviour -- moot here anyway (RunLiveCity has no
    // EngineHost to inject an obstacle into), but keeps the same explicit-opt-in contract every other
    // overlay uses.
    public bool HandlesWorldClick => true;

    // Nearest-vehicle hit-test (design §5's "mirrors template.js's hit-test"): finds the sampled car whose
    // (X, Y) is nearest the click point, within `maxDist`, and latches it. A miss (nothing within
    // `maxDist`) leaves the previous selection untouched -- clicking empty road should not blank out an
    // already-identified vehicle.
    public void OnWorldClick(double worldX, double worldY)
    {
        var snap = _snapshot;
        var idx = PickNearest(snap.Cars, worldX, worldY, DefaultPickRadius);
        if (idx < 0)
        {
            return;
        }

        var car = snap.Cars[idx];
        _selectedHandle = car.Handle;
        _selectedName = car.Name;
    }

    // docs/LIVE-CITY-VIEWERS-TASKS.md B2 success condition: a PURE static helper (no Raylib/window
    // dependency) so the hit-test is unit-testable headlessly. Returns the index of the nearest car within
    // `maxDist` of (wx, wy), or -1 if the list is empty or every car is farther than `maxDist`. Ties (equal
    // distance) keep the FIRST candidate found (stable, deterministic -- no System.Random involved).
    public static int PickNearest(IReadOnlyList<LiveCityCar> cars, double wx, double wy, double maxDist)
    {
        var maxD2 = maxDist * maxDist;
        var best = -1;
        var bestD2 = double.PositiveInfinity;

        for (var i = 0; i < cars.Count; i++)
        {
            var dx = cars[i].X - wx;
            var dy = cars[i].Y - wy;
            var d2 = (dx * dx) + (dy * dy);
            // Strictly-less (not <=) so an exact tie keeps the FIRST candidate found, matching the comment
            // above -- a later equal-distance car must not silently steal the pick from an earlier one.
            if (d2 <= maxD2 && d2 < bestD2)
            {
                bestD2 = d2;
                best = i;
            }
        }

        return best;
    }

    // Pedestrians (regime-coloured discs) + the selected-vehicle highlight ring, drawn OVER the vehicles --
    // mirrors EvacOverlay/PedOverlay's own "peds drawn over" layering.
    public void DrawWorldOver(Camera2D camera, SimulationSnapshot snapshot, IReadOnlyList<Renderer.DrVehicleDraw> vehicles)
    {
        var snap = _snapshot;

        global::Raylib_cs.Raylib.BeginMode2D(camera);

        foreach (var p in snap.Peds)
        {
            // Sim.LiveCity.PedRegime -- explicitly qualified: Sim.Viewer already has its own PedRegime enum
            // (PedOverlay.cs's LowPower/HighPower/Escaped), and this file's namespace IS Sim.Viewer, so an
            // unqualified `PedRegime` would resolve to the WRONG (sibling) enum.
            var color = p.Regime switch
            {
                Sim.LiveCity.PedRegime.HighPower => HighPowerColor,
                Sim.LiveCity.PedRegime.Paused => PausedColor,
                _ => LowPowerColor,
            };
            global::Raylib_cs.Raylib.DrawCircleV(Renderer.Flip(p.X, p.Y), PedRadius, color);
        }

        var ringCenterScreen = default(Vector2?);
        if (_selectedHandle is { } handle && TryFindCar(snap.Cars, handle, out var car))
        {
            var center = Renderer.Flip(car.X, car.Y);
            var ringRadius = Math.Max((float)Math.Max(car.Length, car.Width) * 0.9f, 1.2f / camera.Zoom);
            global::Raylib_cs.Raylib.DrawCircleLinesV(center, ringRadius, SelectionRingColor);
            ringCenterScreen = global::Raylib_cs.Raylib.GetWorldToScreen2D(center, camera);
        }

        global::Raylib_cs.Raylib.EndMode2D();

        // The identity label is drawn in SCREEN space (fixed pixel font size), after EndMode2D, so it stays
        // legible at any zoom instead of scaling (and blurring/vanishing) with the world-space ring.
        if (ringCenterScreen is { } screenPos && _selectedName is { } name)
        {
            global::Raylib_cs.Raylib.DrawText(name, (int)screenPos.X + 10, (int)screenPos.Y - 10, 16, SelectionLabelColor);
        }
    }

    private static bool TryFindCar(IReadOnlyList<LiveCityCar> cars, VehicleHandle handle, out LiveCityCar car)
    {
        for (var i = 0; i < cars.Count; i++)
        {
            if (cars[i].Handle == handle)
            {
                car = cars[i];
                return true;
            }
        }

        car = default;
        return false;
    }

    // The live-city legend + live HUD counters (B1's "small HUD line") -- mirrors EvacOverlay/PedOverlay's
    // panel placement/sizing convention (left column, below the generic controls/diagnostics panels).
    public void DrawUi()
    {
        var snap = _snapshot;

        ImGui.SetNextWindowPos(new Vector2(10, 610), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(360, 150), ImGuiCond.FirstUseEver);
        ImGui.Begin("SumoSharp - live city");

        ImGui.Text("pedestrian legend:");
        LegendLine(LowPowerColor, "low-power (calm / weaving)");
        LegendLine(HighPowerColor, "high-power (promoted / reactive)");
        LegendLine(PausedColor, "paused (dwell)");

        ImGui.Separator();
        ImGui.Text($"cars: {snap.Cars.Count}   peds: {snap.Peds.Count}   occupied crossings: {snap.OccupiedCrossings}");
        ImGui.Text(_selectedName is { } name ? $"selected: {name}" : "click a car to identify it");

        ImGui.End();
    }

    private static void LegendLine(Color c, string label)
    {
        ImGui.TextColored(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f), $"■ {label}");
    }
}
