using System.Numerics;
using Raylib_cs;
using rlImGui_cs;
using ImGuiNET;
using Sim.Core;
using Sim.Ingest;
using Sim.Viewer.Core;

namespace Sim.Viewer;

// docs/SUMOSHARP-NATIVE-VIEWER.md P0: draws the LOCAL-mode world (roads + oriented vehicles from the
// authoritative SimulationSnapshot -- no dead reckoning, no jitter) plus a minimal ImGui HUD.
//
// Coordinate convention: SUMO world Y is UP; raylib/canvas screen space is Y-DOWN (SUMOSHARP-NATIVE-
// VIEWER.md's "Read first" item 5). Every world point fed to a raylib draw call is passed through
// `Flip` (negates Y) before being handed to raylib; the returned Camera2D's Target/Offset are computed in
// that SAME negated-Y space, so BeginMode2D's pan/zoom (translate by -Target, scale by Zoom, translate by
// +Offset) reproduces HtmlPage.cs's `w2s(x,y) = [x*scale+ox, -y*scale+oy]` exactly, while still using a
// real Camera2D (rather than a hand-rolled screen transform) as instructed. Because thickness/size values
// passed to DrawLineEx/DrawRectanglePro are also given in this same world-unit space, the camera's Zoom
// scales road width and vehicle size for free.
public static class Renderer
{
    private static readonly Color Background = new(14, 17, 22, 255);
    private static readonly Color RoadCasing = new(10, 12, 16, 255);
    private static readonly Color RoadSurface = new(69, 78, 90, 255);
    private static readonly Color RoadInternal = new(42, 48, 56, 255);
    private static readonly Color VehicleColor = new(63, 185, 80, 255);

    public static Color BackgroundColor => Background;

    // World (x,y, SUMO Y-up) -> the Y-negated space fed to every raylib draw call under BeginMode2D.
    private static Vector2 Flip(double x, double y) => new((float)x, (float)-y);

    // Fit a Camera2D to the network bounds with a margin, exactly like HtmlPage.cs's `fit(b)`. Target and
    // Offset are expressed in the SAME Y-negated space `Flip` produces, so the Y-flip lives entirely in
    // this one conversion (negate the bounds centre's Y here; negate every drawn point's Y via `Flip`).
    public static Camera2D FitCamera(double minX, double minY, double maxX, double maxY, int screenW, int screenH)
    {
        var boundsW = (float)Math.Max(maxX - minX, 1.0);
        var boundsH = (float)Math.Max(maxY - minY, 1.0);
        var zoom = Math.Min(screenW / boundsW, screenH / boundsH) * 0.9f;
        var centerX = (float)((minX + maxX) / 2.0);
        var centerY = (float)((minY + maxY) / 2.0);

        return new Camera2D
        {
            Target = new Vector2(centerX, -centerY),
            Offset = new Vector2(screenW / 2f, screenH / 2f),
            Rotation = 0f,
            Zoom = zoom,
        };
    }

    public static void DrawWorld(Camera2D camera, NetworkModel network, SimulationSnapshot snapshot)
    {
        Raylib.BeginMode2D(camera);

        // Roads: a dark casing under a lighter lane fill, each lane drawn as a stroked polyline along its
        // Shape (HtmlPage.cs draw()'s two-pass casing/surface loop). Thickness is in WORLD units (metres)
        // so the camera's Zoom scales it automatically; the 1.5/2.5 px floors are converted to world units
        // by dividing by Zoom, mirroring the browser's pixel-space clamp (`Math.max(1.5, lane.w*cam.scale)`).
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var lane in network.LanesByHandle)
            {
                var shape = lane.Shape;
                if (shape.Count < 2)
                {
                    continue;
                }

                var surfaceThick = Math.Max(1.5f / camera.Zoom, (float)lane.Width);
                var thick = pass == 0 ? surfaceThick + 2.5f / camera.Zoom : surfaceThick;
                var color = pass == 0 ? RoadCasing : (lane.Id.StartsWith(':') ? RoadInternal : RoadSurface);

                for (var i = 0; i < shape.Count - 1; i++)
                {
                    var (x1, y1) = shape[i];
                    var (x2, y2) = shape[i + 1];
                    Raylib.DrawLineEx(Flip(x1, y1), Flip(x2, y2), thick, color);
                }
            }
        }

        // Vehicles: oriented rectangles sized Length x Width (world metres), positioned at the SUMO FRONT
        // reference point (SimulationSnapshot.PosX/PosY -- PoseResolver.cs: "Position is the vehicle's
        // front reference, matching SUMO getPosition"), rotated to match Angle (navi-deg, 0=N clockwise).
        for (var i = 0; i < snapshot.Count; i++)
        {
            var front = Flip(snapshot.PosX[i], snapshot.PosY[i]);
            var length = Math.Max(0.5f / camera.Zoom, snapshot.Length[i]);
            var width = Math.Max(0.3f / camera.Zoom, snapshot.Width[i]);

            // navi-deg (0=N, cw) -> world dir (sin,cos) -> screen dir (x,-y flip) -> screen rotation angle,
            // matching HtmlPage.cs draw()'s `nr`/`sa` computation exactly:
            //   nr = deg*PI/180; sa = atan2(-cos(nr), sin(nr))
            var nr = snapshot.Angle[i] * MathF.PI / 180f;
            var sa = MathF.Atan2(-MathF.Cos(nr), MathF.Sin(nr));
            var rotationDeg = sa * 180f / MathF.PI;

            // The front sits at the rectangle's local (width=length, height/2) point -- i.e. the body
            // trails BEHIND the front along local -x, matching the browser's `ctx.rect(-L,-W/2,L,W)` drawn
            // at a translate-origin of the front point (HtmlPage.cs draw()). raylib's DrawRectanglePro
            // subtracts `origin` from the (width,height) box before rotating/translating to `rec.x,rec.y`,
            // so origin=(length,width/2) reproduces that same occupied range [-length,0] x [-width/2,width/2].
            var rec = new Rectangle(front.X, front.Y, length, width);
            var origin = new Vector2(length, width / 2f);
            Raylib.DrawRectanglePro(rec, origin, rotationDeg, VehicleColor);
        }

        Raylib.EndMode2D();
    }

    public static void DrawHud(EngineHost host, SimulationSnapshot snapshot)
    {
        rlImGui.Begin();
        ImGui.Begin("SumoSharp - native viewer (local)");
        ImGui.Text(host.ScenarioMode ? "mode: SCENARIO" : "mode: SANDBOX");
        ImGui.Separator();
        ImGui.Text($"vehicles: {snapshot.Count}");
        ImGui.Text($"sim time: {snapshot.Time:F1}s   step: {snapshot.StepCount}");
        ImGui.Text($"fps: {Raylib.GetFPS()}");
        ImGui.End();
        rlImGui.End();
    }
}
