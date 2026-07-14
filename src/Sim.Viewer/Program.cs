using System.Globalization;
using System.Numerics;
using CycloneDDS.Runtime;
using Raylib_cs;
using rlImGui_cs;
using ImGuiNET;
using Sim.Core;
using Sim.Replication;
using Sim.Viewer;
using Sim.Viewer.Core;

// docs/SUMOSHARP-NATIVE-VIEWER.md P0/P1/P2b: the native desktop viewer entry point.
//   dotnet run --project src/Sim.Viewer -- --mode <local|loopback> <scenarioDir|net.xml> [opts]
// `--mode local` renders the authoritative SimulationSnapshot every frame (no transport, no dead
// reckoning -- EngineHost owns the Engine + SimulationRunner directly). `--mode loopback` (P2b) runs a
// DdsPublisher + DdsSubscriber in-process over DDS and renders the DEAD-RECKONED poses coming through DDS
// (DrClock + PoseResolver), not the local Snapshot -- SUMOSHARP-NATIVE-VIEWER.md's "Modes" section.
// `remote` is P3 scope and not implemented yet.
// `--screenshot`/`--frames` renders headless (no interactive loop) for the Xvfb verification recipe in
// the design doc: render `frames` frames then TakeScreenshot and exit.
// `--drop-obstacle <wx>,<wy>` (P1): a headless test hook -- inject one obstacle at the given WORLD point
// right after startup, so an obstacle + the resulting queue are visible in a `--screenshot` without
// needing real mouse input under Xvfb.
// `--delay <seconds>` (P2b): presets the loopback DR playout delay (0 = extrapolate) so the interactive
// slider's effect can be verified headlessly (can't be driven by mouse input under Xvfb).

string? mode = null;
string? inputPath = null;
string? screenshotPath = null;
string? selftestPath = null;
var frames = 150;
var delaySeconds = 0.0f;
(double X, double Y)? dropObstacle = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--mode":
            mode = args[++i];
            break;
        case "--selftest":
            selftestPath = args[++i];
            break;
        case "--screenshot":
            screenshotPath = args[++i];
            break;
        case "--frames":
            frames = int.Parse(args[++i]);
            break;
        case "--delay":
            delaySeconds = float.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--drop-obstacle":
            var parts = args[++i].Split(',');
            dropObstacle = (
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture));
            break;
        default:
            inputPath ??= args[i];
            break;
    }
}

// docs/SUMOSHARP-NATIVE-VIEWER.md P2 — a headless (no window, no raylib) proof that the DDS data path
// round-trips: EngineHost -> DdsPublisher -> CycloneDDS -> DdsSubscriber. Accepts either a direct net.xml
// path or a scenario/sandbox directory, resolved exactly like `--mode local` does below.
if (selftestPath is not null)
{
    return LoopbackSelfTest.Run(ResolveNetPath(selftestPath));
}

if (inputPath is null)
{
    Console.Error.WriteLine("Sim.Viewer: missing <scenarioDir|net.xml> argument.");
    return 1;
}

if (mode == "local")
{
    return RunLocal(ResolveNetPath(inputPath), screenshotPath, frames, dropObstacle);
}

if (mode == "loopback")
{
    return RunLoopback(ResolveNetPath(inputPath), screenshotPath, frames, delaySeconds, dropObstacle);
}

Console.Error.WriteLine($"Sim.Viewer: unknown --mode '{mode ?? "(none)"}' (expected local|loopback; remote is P3 scope).");
return 1;

// Accept either a scenario/sandbox directory (resolve its *.net.xml) or a direct net.xml path --
// EngineHost itself does the scenario-vs-sandbox detection from the resolved net path's directory.
static string ResolveNetPath(string path)
{
    if (Directory.Exists(path))
    {
        return Directory.EnumerateFiles(path, "*.net.xml").FirstOrDefault()
            ?? throw new FileNotFoundException($"No *.net.xml found in directory '{path}'.");
    }

    return path;
}

static int RunLocal(string netPath, string? screenshotPath, int frames, (double X, double Y)? dropObstacle)
{
    using var host = new EngineHost(netPath);

    if (dropObstacle is { } drop)
    {
        host.InjectObstacleAtWorld(drop.X, drop.Y);
    }

    const int screenW = 1280;
    const int screenH = 800;

    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    Raylib.InitWindow(screenW, screenH, "SumoSharp - native viewer (local)");
    if (!Raylib.IsWindowReady())
    {
        Console.Error.WriteLine("Sim.Viewer: window not ready (no display?).");
        return 1;
    }

    // Cap the draw loop at 60 fps. EngineHost's SimulationRunner ticks on its own real-time-paced background
    // thread (targetHz 10) and the random-traffic spawner fires on a real wall-clock Timer (dueTime 500ms,
    // period 900ms) -- an unthrottled headless draw loop can blast through `--frames` in well under a second
    // under Xvfb/software GL, finishing before either has had a chance to run even once. Pacing the render
    // loop to a real frame rate gives both wall-clock-driven systems time to actually produce traffic before
    // the screenshot is taken.
    Raylib.SetTargetFPS(60);

    rlImGui.Setup(darkTheme: true, enableDocking: false);
    var io = ImGui.GetIO();
    io.Fonts.Clear();
    var fontPath = Path.Combine(AppContext.BaseDirectory, "assets", "DejaVuSans.ttf");
    io.Fonts.AddFontFromFileTTF(fontPath, 18f);
    rlImGui.ReloadFonts();

    var camera = Renderer.FitCamera(host.MinX, host.MinY, host.MaxX, host.MaxY, screenW, screenH);

    var headless = screenshotPath is not null;
    var frameCount = 0;
    var frameStats = new FrameStats();
    var showDiagnostics = true; // P1: diagnostics panel default ON, toggled with 'd'

    // P1 drag-vs-click bookkeeping for the world camera (Camera2D pan/zoom/pick -- see Renderer.Flip's
    // doc comment for the world<->screen convention this camera operates in).
    var dragging = false;
    var dragMoved = false;
    var dragStartMouse = Vector2.Zero;
    var dragStartTarget = Vector2.Zero;
    const float DragMoveThreshold = 3f; // px: below this, mouseup is a CLICK (pick), not a pan.

    while (!Raylib.WindowShouldClose())
    {
        frameStats.Add(Raylib.GetFrameTime());

        if (Raylib.IsKeyPressed(KeyboardKey.D))
        {
            showDiagnostics = !showDiagnostics;
        }

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Renderer.BackgroundColor);

        // rlImGui.Begin() (ImGui NewFrame) must run before reading io.WantCaptureMouse for this frame's
        // world-input gate (an ImGui window/button under the cursor should eat clicks/drags/wheel rather
        // than also panning/picking the world underneath it).
        rlImGui.Begin();
        var wantMouse = ImGui.GetIO().WantCaptureMouse;

        if (!wantMouse)
        {
            var mouse = Raylib.GetMousePosition();

            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                dragging = true;
                dragMoved = false;
                dragStartMouse = mouse;
                dragStartTarget = camera.Target;
            }

            if (dragging && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                var delta = mouse - dragStartMouse;
                if (delta.Length() > DragMoveThreshold)
                {
                    dragMoved = true;
                }

                // Pan: drag the WORLD with the cursor (moving the mouse right reveals content to the
                // left), so Target moves opposite the screen-space drag delta, scaled back to world units.
                camera.Target = dragStartTarget - delta / camera.Zoom;
            }

            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
            {
                if (dragging && !dragMoved)
                {
                    // A click, not a pan -> invert the camera (then Flip) to get the WORLD point under the
                    // cursor and inject an obstacle there.
                    var flipSpace = Raylib.GetScreenToWorld2D(mouse, camera);
                    host.InjectObstacleAtWorld(flipSpace.X, -flipSpace.Y);
                }

                dragging = false;
            }

            var wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0f)
            {
                // Zoom about the cursor: the world point under the cursor (in Flip-space) must land back
                // under the cursor after the zoom, so re-derive Target from the pre-zoom world point.
                var beforeZoom = Raylib.GetScreenToWorld2D(mouse, camera);
                var zoomFactor = wheel > 0 ? 1.1f : 1f / 1.1f;
                camera.Zoom *= zoomFactor;
                var afterZoom = Raylib.GetScreenToWorld2D(mouse, camera);
                camera.Target += beforeZoom - afterZoom;
            }
        }

        var snapshot = host.Snapshot;
        Renderer.DrawWorld(camera, host.Network, snapshot, host);

        Renderer.DrawControlsPanel(host);
        if (showDiagnostics)
        {
            Renderer.DrawDiagnosticsPanel(snapshot, frameStats);
        }

        rlImGui.End();

        Raylib.EndDrawing();

        frameCount++;
        if (headless && frameCount >= frames)
        {
            ExportScreenshot(screenshotPath!);
            break;
        }
    }

    rlImGui.Shutdown();
    Raylib.CloseWindow();
    return 0;
}

// docs/SUMOSHARP-NATIVE-VIEWER.md P2b — one process runs BOTH the publisher (EngineHost -> DdsPublisher)
// and the subscriber/renderer, over DDS intra-host. Renders the DEAD-RECKONED poses coming through DDS
// (DrClock + PoseResolver against the SUBSCRIBER's decoded geometry/history), not the local Snapshot --
// the single-app DR test the design doc's "loopback" mode exists for.
static int RunLoopback(string netPath, string? screenshotPath, int frames, float initialDelaySeconds, (double X, double Y)? dropObstacle)
{
    using var host = new EngineHost(netPath);
    using var participant = new DdsParticipant();
    using var publisher = new DdsPublisher(host, participant);
    using var subscriber = new DdsSubscriber(participant);

    if (dropObstacle is { } drop)
    {
        host.InjectObstacleAtWorld(drop.X, drop.Y);
    }

    // DDS discovery is async -- give the intra-process writer/reader pairs time to match before anything
    // is published (LoopbackSelfTest's proven pattern).
    Thread.Sleep(500);
    publisher.PublishGeometryOnce();

    // Drain until the whole network's geometry has arrived (or a short timeout), so the very first
    // rendered frames already have roads to draw instead of a blank world for a few frames.
    for (var i = 0; i < 50 && !subscriber.GeometryComplete; i++)
    {
        subscriber.Pump();
        Thread.Sleep(20);
    }

    const int screenW = 1280;
    const int screenH = 800;

    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    Raylib.InitWindow(screenW, screenH, "SumoSharp - native viewer (loopback DR)");
    if (!Raylib.IsWindowReady())
    {
        Console.Error.WriteLine("Sim.Viewer: window not ready (no display?).");
        return 1;
    }

    Raylib.SetTargetFPS(60);

    rlImGui.Setup(darkTheme: true, enableDocking: false);
    var io = ImGui.GetIO();
    io.Fonts.Clear();
    var fontPath = Path.Combine(AppContext.BaseDirectory, "assets", "DejaVuSans.ttf");
    io.Fonts.AddFontFromFileTTF(fontPath, 18f);
    rlImGui.ReloadFonts();

    // Same net -> same bounds whether read locally (EngineHost.Network) or over DDS; local bounds are
    // already available without waiting on the subscriber, so the camera fit doesn't need to block further.
    var camera = Renderer.FitCamera(host.MinX, host.MinY, host.MaxX, host.MaxY, screenW, screenH);

    var headless = screenshotPath is not null;
    var frameCount = 0;
    var frameStats = new FrameStats();
    var showDiagnostics = true;

    var dragging = false;
    var dragMoved = false;
    var dragStartMouse = Vector2.Zero;
    var dragStartTarget = Vector2.Zero;
    const float DragMoveThreshold = 3f;

    var drClock = new DrClock();
    var delaySlider = initialDelaySeconds;
    var smooth = false;
    var smoothed = new Dictionary<VehicleHandle, (float X, float Y, float Deg)>();
    var lastPublishedSimTime = double.NaN;
    var startWall = System.Diagnostics.Stopwatch.StartNew();

    Span<int> upcomingScratch = stackalloc int[UpcomingLanes.Count];
    var vehicleDraws = new List<Renderer.DrVehicleDraw>();

    while (!Raylib.WindowShouldClose())
    {
        frameStats.Add(Raylib.GetFrameTime());

        if (Raylib.IsKeyPressed(KeyboardKey.D))
        {
            showDiagnostics = !showDiagnostics;
        }

        // Publish at the SIM cadence (gated on the snapshot's own Time advancing), not the 60 Hz render
        // cadence -- EngineHost's SimulationRunner ticks in the background at its own targetHz, so most
        // render frames see an unchanged snapshot and would otherwise re-publish identical state.
        var snapTimeNow = host.Snapshot.Time;
        if (double.IsNaN(lastPublishedSimTime) || snapTimeNow > lastPublishedSimTime)
        {
            publisher.PublishStep();
            lastPublishedSimTime = snapTimeNow;
        }

        subscriber.Pump();
        drClock.Pump(subscriber.LatestVehicleSampleTime);

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Renderer.BackgroundColor);

        rlImGui.Begin();
        var wantMouse = ImGui.GetIO().WantCaptureMouse;

        if (!wantMouse)
        {
            var mouse = Raylib.GetMousePosition();

            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                dragging = true;
                dragMoved = false;
                dragStartMouse = mouse;
                dragStartTarget = camera.Target;
            }

            if (dragging && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                var delta = mouse - dragStartMouse;
                if (delta.Length() > DragMoveThreshold)
                {
                    dragMoved = true;
                }

                camera.Target = dragStartTarget - delta / camera.Zoom;
            }

            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
            {
                if (dragging && !dragMoved)
                {
                    var flipSpace = Raylib.GetScreenToWorld2D(mouse, camera);
                    host.InjectObstacleAtWorld(flipSpace.X, -flipSpace.Y);
                }

                dragging = false;
            }

            var wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0f)
            {
                var beforeZoom = Raylib.GetScreenToWorld2D(mouse, camera);
                var zoomFactor = wheel > 0 ? 1.1f : 1f / 1.1f;
                camera.Zoom *= zoomFactor;
                var afterZoom = Raylib.GetScreenToWorld2D(mouse, camera);
                camera.Target += beforeZoom - afterZoom;
            }
        }

        // Build each tracked vehicle's dead-reckoned draw pose: DrClock resolves the render-time DrState
        // from its buffered history (dt already applied), PoseResolver walks the DDS-received geometry
        // (dt=0, since DrClock already advanced pos/posLat to render time) to get the world pose.
        vehicleDraws.Clear();
        var geoSource = new DdsGeometryLaneSource(subscriber.Geometry);
        foreach (var (handle, history) in subscriber.History)
        {
            if (history.Count == 0)
            {
                continue;
            }

            var resolved = drClock.Resolve(history, delaySlider);
            var (length, width) = subscriber.Dims.TryGetValue(handle, out var dims) ? dims : (5.0f, 1.8f);
            var state = resolved.State with { Length = length, Width = width };

            var upCount = resolved.Upcoming.CopyTo(upcomingScratch);
            var pose = PoseResolver.Resolve(
                geoSource, state, upcomingScratch[..upCount], default, 0.0, RenderRealism.CornerCutCorrected);

            var px = pose.X;
            var py = pose.Y;
            var pdeg = pose.HeadingDeg;

            // Optional low-pass, extrapolation-only (HtmlPage.cs's `smooth`): interpolated poses are
            // already smooth/exact, so only extrapolated ones are filtered.
            if (smooth && resolved.Extrapolated)
            {
                var (min, avg, _) = frameStats.Compute();
                var frameDt = avg > 0f ? avg : 1f / 60f;
                var aPos = 1f - MathF.Exp(-frameDt / 0.07f);
                var aDeg = 1f - MathF.Exp(-frameDt / 0.06f);

                if (smoothed.TryGetValue(handle, out var prev))
                {
                    var ex = (float)px - prev.X;
                    var ey = (float)py - prev.Y;
                    if (ex * ex + ey * ey > 49f)
                    {
                        smoothed[handle] = ((float)px, (float)py, pdeg);
                    }
                    else
                    {
                        var nx = prev.X + ex * aPos;
                        var ny = prev.Y + ey * aPos;
                        var dd = ((pdeg - prev.Deg + 540f) % 360f) - 180f;
                        var nd = Math.Abs(dd) > 50f ? pdeg : (prev.Deg + dd * aDeg + 360f) % 360f;
                        smoothed[handle] = (nx, ny, nd);
                    }
                }
                else
                {
                    smoothed[handle] = ((float)px, (float)py, pdeg);
                }

                (px, py, pdeg) = smoothed[handle];
            }
            else
            {
                smoothed[handle] = ((float)px, (float)py, pdeg);
            }

            vehicleDraws.Add(new Renderer.DrVehicleDraw(px, py, pdeg, length, width, state.Speed));
        }

        Renderer.DrawWorldDds(camera, subscriber.Geometry, subscriber.TlStateByLane, vehicleDraws);

        Renderer.DrawLoopbackControlsPanel(host, ref delaySlider, ref smooth);
        if (showDiagnostics)
        {
            var wallElapsed = startWall.Elapsed.TotalSeconds;
            var ddsSamplesPerSecond = wallElapsed > 0 ? subscriber.TotalVehicleSamplesReceived / wallElapsed : 0.0;
            Renderer.DrawDdsDiagnosticsPanel(frameStats, drClock, ddsSamplesPerSecond, vehicleDraws.Count);
        }

        rlImGui.End();

        Raylib.EndDrawing();

        frameCount++;
        if (headless && frameCount >= frames)
        {
            ExportScreenshot(screenshotPath!);
            Console.WriteLine($"DRCLOCK: renderSim={drClock.RenderSim:F3} simRate={drClock.SimRate:F3} backSteps={drClock.BackSteps}");
            break;
        }
    }

    rlImGui.Shutdown();
    Raylib.CloseWindow();
    return 0;
}

// NOT Raylib.TakeScreenshot(path): raylib's TakeScreenshot silently drops the directory portion of `path`
// (it saves GetFileName(path) under its internal storage/base path, i.e. the process's working directory
// at InitWindow time) -- confirmed experimentally: an absolute path like "/tmp/p0-cross.png" landed at
// "<cwd>/p0-cross.png" instead. LoadImageFromScreen + ExportImage writes to the exact path given, honoring
// an absolute `--screenshot` path as the CLI promises.
static void ExportScreenshot(string screenshotPath)
{
    var absolutePath = Path.GetFullPath(screenshotPath);
    var screenImage = Raylib.LoadImageFromScreen();
    Raylib.ExportImage(screenImage, absolutePath);
    Raylib.UnloadImage(screenImage);
}
