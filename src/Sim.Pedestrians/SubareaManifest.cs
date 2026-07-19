using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sim.Pedestrians;

// P8 (docs/COORDINATION-pedestrian-x-subarea.md §3; docs/PEDESTRIAN-P8-2-APPEARANCE-LEGITIMACY-DESIGN.md §4):
// reads the DURABLE half of the SumoData sub-area data contract from the box's `manifest.json` -- the inputs
// a host needs once at load to configure the pedestrian side against a crop:
//
//  - WalkableFringeEdges: manifest.subarea.fringe_edges with ped=true -- the no-cheating appearance boundary
//    that seeds PedSpawnPolicy's fringe set (the walkable-edge analogue of RealismMask).
//  - BoxBounds:           manifest.subarea.box_bounds -- the shared SUMO-metre coordinate frame.
//  - KneeVehLkm:          manifest.density.knee_veh_lkm -- the calibrated vehicle-density knee the P8-4 ped
//    density knob / crossing-throughput guard anchors against (so crowds don't gridlock the calibrated cars).
//
// Pure input parsing (System.Text.Json), no SUMO, no engine -- hermetically testable against the committed
// box. The PER-TICK half of the contract (the camera visible walkable-edge set) is host-provided at runtime,
// not from the manifest.
public sealed record SubareaManifest(
    IReadOnlyList<string> WalkableFringeEdges,
    SubareaManifest.Bounds BoxBounds,
    double KneeVehLkm)
{
    public readonly record struct Bounds(double XMin, double YMin, double XMax, double YMax);

    public static SubareaManifest Load(string manifestPath)
    {
        if (manifestPath is null)
        {
            throw new ArgumentNullException(nameof(manifestPath));
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;

        var subarea = root.GetProperty("subarea");

        var fringe = new List<string>();
        foreach (var e in subarea.GetProperty("fringe_edges").EnumerateArray())
        {
            // The walkable fringe is exactly the ped=true stubs (audit_nocheat's ped fringe), per §3.
            if (e.TryGetProperty("ped", out var ped) && ped.GetBoolean())
            {
                fringe.Add(e.GetProperty("id").GetString()!);
            }
        }

        var bb = subarea.GetProperty("box_bounds");
        var bounds = new Bounds(
            bb.GetProperty("xmin").GetDouble(),
            bb.GetProperty("ymin").GetDouble(),
            bb.GetProperty("xmax").GetDouble(),
            bb.GetProperty("ymax").GetDouble());

        // density.knee_veh_lkm is the calibrated knee; tolerate its absence (older manifests) with NaN.
        var knee = double.NaN;
        if (root.TryGetProperty("density", out var density) &&
            density.TryGetProperty("knee_veh_lkm", out var kneeEl))
        {
            knee = kneeEl.GetDouble();
        }

        return new SubareaManifest(fringe, bounds, knee);
    }
}
