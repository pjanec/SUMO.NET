// HelloTraffic -- a tutorial-style walkthrough of the SumoSharp.Core "quick start" (see
// src/Sim.Core/README.md). Run it with:
//   dotnet run --project samples/HelloTraffic
// or point it at any SUMO network:
//   dotnet run --project samples/HelloTraffic -- path/to/net.net.xml
using Sim.Core;

internal static class Program
{
    private static int Main(string[] args)
    {
        // 1) Resolve a network to load. Default to the committed scenarios/15-reroute network (the
        //    same one samples/SumoSharp.GameHostSample defaults to); accept an override as argv[0].
        var netPath = args.Length > 0 ? args[0] : DefaultNet();
        if (netPath is null || !File.Exists(netPath))
        {
            Console.Error.WriteLine("error: could not find a .net.xml (pass one as the first argument).");
            return 1;
        }

        Console.WriteLine($"HelloTraffic — network: {netPath}");

        // 2) Create the engine and load a NETWORK ONLY (no demand file). This is the "start empty and
        //    build traffic at runtime" entry point -- exactly what a game or digital twin wants.
        var engine = new Engine();
        engine.LoadNetwork(netPath);

        // 3) Define a vehicle type. Sigma=0 keeps the car-following model deterministic (no driver
        //    imperfection noise) -- the same phase-1 determinism rule the whole engine relies on.
        var car = engine.DefineVType(new VTypeParams { Sigma = 0.0, MaxSpeed = 13.89 }, id: "car");

        // 4) Spawn two vehicles, routed by the engine's shortest-path router between edge ids that
        //    exist in the default network (S -> A -> B -> D -> E and A -> B -> D -> E). SpawnVehicle
        //    returns a handle immediately in the Pending state; SUMO-parity queued insertion places it
        //    on the road at the next Step() when a safe gap exists. The second vehicle uses the
        //    engine's built-in default passenger vType instead of the one we just defined, to show
        //    both ways of picking a vType.
        var v1 = engine.SpawnVehicle(car, fromEdge: "SA", toEdge: "DE");
        var v2 = engine.SpawnVehicle(engine.DefaultVType, fromEdge: "AB", toEdge: "DE");
        Console.WriteLine($"spawned {v1} and {v2}");

        // 5) Step the simulation ~20 times. Each step, read the live vehicle state back with
        //    zero-allocation columnar spans: VehicleHandles is the set of currently-live vehicles, and
        //    TryGetVehicle projects one of them into a random-access record (position, speed, lane).
        for (var step = 1; step <= 20; step++)
        {
            engine.Step();

            foreach (var h in engine.VehicleHandles)
            {
                if (engine.TryGetVehicle(h, out var s))
                {
                    Console.WriteLine(
                        $"  step {step,2}  t={engine.CurrentTime,5:F1}s  {s.VehicleId,-6} " +
                        $"lane={s.LaneId,-6} x={s.X,7:F2} y={s.Y,7:F2} speed={s.Speed,5:F2} m/s");
                }
            }
        }

        Console.WriteLine("done.");
        return 0;
    }

    // scenarios/15-reroute/net.net.xml, found by walking up to the repo root (Traffic.sln) -- the
    // same routable network samples/SumoSharp.GameHostSample defaults to.
    private static string? DefaultNet()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Traffic.sln")))
        {
            dir = dir.Parent;
        }

        return dir is null ? null : Path.Combine(dir.FullName, "scenarios", "15-reroute", "net.net.xml");
    }
}
