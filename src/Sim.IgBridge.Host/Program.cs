using Sim.IgBridge;

// IgBridge verification host (docs/IGBRIDGE-TASKS.md Stage 1+). Scaffold entry point: the run / replay
// / render / metrics subcommands are wired in T1.2-T1.5. For now it only proves the reference graph
// (Sim.IgBridge + Sim.Viz) resolves and links on net8.0.
Console.WriteLine("IgBridge verification host (scaffold).");
Console.WriteLine($"IgSample schema: {nameof(IgRecordKind.New)}/{nameof(IgRecordKind.Upd)}/{nameof(IgRecordKind.Del)} "
    + "-- planar (x, y, headingDeg) @ sim-time t.");
Console.WriteLine("Subcommands (run / replay / render / metrics) land in Stage 1.");
return 0;
