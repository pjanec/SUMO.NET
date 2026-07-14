using System.Globalization;
using System.Numerics;
using Raylib_cs;
using rlImGui_cs;
using ImGuiNET;
using Sim.Viewer;
using Sim.Viewer.Core;

// docs/SUMOSHARP-NATIVE-VIEWER.md P0/P1: the native desktop viewer entry point.
//   dotnet run --project src/Sim.Viewer -- --mode local <scenarioDir|net.xml> [--screenshot <path>] [--frames <n>] [--drop-obstacle <wx>,<wy>]
// `--mode local` renders the authoritative SimulationSnapshot every frame (no transport, no dead
// reckoning -- EngineHost owns the Engine + SimulationRunner directly). `remote`/`loopback` are P2/P3
// scope (SUMOSHARP-NATIVE-VIEWER.md's "Modes" section) and are not implemented yet.
// `--screenshot`/`--frames` renders headless (no interactive loop) for the Xvfb verification recipe in
// the design doc: render `frames` frames then TakeScreenshot and exit.
// `--drop-obstacle <wx>,<wy>` (P1): a headless test hook -- inject one obstacle at the given WORLD point
// right after startup, so an obstacle + the resulting queue are visible in a `--screenshot` without
// needing real mouse input under Xvfb.

string? mode = null;
string? inputPath = null;
string? screenshotPath = null;
var frames = 150;
(double X, double Y)? dropObstacle = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--mode":
            mode = args[++i];
            break;
        case "--screenshot":
            screenshotPath = args[++i];
            break;
        case "--frames":
            frames = int.Parse(args[++i]);
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

if (mode != "local")
{
    Console.Error.WriteLine($"Sim.Viewer: only --mode local is implemented in P0/P1 (got '{mode ?? "(none)"}').");
    return 1;
}

if (inputPath is null)
{
    Console.Error.WriteLine("Sim.Viewer: missing <scenarioDir|net.xml> argument.");
    return 1;
}

// Accept either a scenario/sandbox directory (resolve its *.net.xml) or a direct net.xml path --
// EngineHost itself does the scenario-vs-sandbox detection from the resolved net path's directory.
string netPath;
if (Directory.Exists(inputPath))
{
    netPath = Directory.EnumerateFiles(inputPath, "*.net.xml").FirstOrDefault()
        ?? throw new FileNotFoundException($"No *.net.xml found in directory '{inputPath}'.");
}
else
{
    netPath = inputPath;
}

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
        // NOT Raylib.TakeScreenshot(path): raylib's TakeScreenshot silently drops the directory portion of
        // `path` (it saves GetFileName(path) under its internal storage/base path, i.e. the process's
        // working directory at InitWindow time) -- confirmed experimentally: an absolute path like
        // "/tmp/p0-cross.png" landed at "<cwd>/p0-cross.png" instead. LoadImageFromScreen + ExportImage
        // writes to the exact path given, honoring an absolute `--screenshot` path as the CLI promises.
        var absolutePath = Path.GetFullPath(screenshotPath!);
        var screenImage = Raylib.LoadImageFromScreen();
        Raylib.ExportImage(screenImage, absolutePath);
        Raylib.UnloadImage(screenImage);
        break;
    }
}

rlImGui.Shutdown();
Raylib.CloseWindow();
return 0;
