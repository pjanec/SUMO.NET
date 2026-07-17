using System.Globalization;

namespace Sim.Harness;

/// <summary>
/// P0-D (docs/HIGH-DENSITY-P0-DESIGN.md "P0-D"): writes a SUMO-schema <c>--statistic-output</c>
/// file's <c>&lt;teleports total= jam= yield= wrongLane=/&gt;</c> subset. Unlike <see
/// cref="SummaryWriterObserver"/> this is a plain static writer, not an <see
/// cref="ISimExportObserver"/> -- a real SUMO statistic file is written ONCE at the end of the run
/// (it is not a per-step series), and every field here (<see cref="Engine.TeleportCount"/>) is
/// already a whole-run total the engine tracks directly, so there is no per-frame state to
/// aggregate through the export seam.
///
/// Deliberately does NOT emit the golden's other top-level elements (<c>performance</c>,
/// <c>vehicles</c>, <c>safety</c>, <c>persons</c>, <c>personTeleports</c>) -- those carry
/// wall-clock/process metrics or subsystems (persons) this phase-1 engine has no faithful source
/// for, and docs/HIGH-DENSITY-P0-DESIGN.md's P0-D scope only requires <c>teleports</c> to be
/// graded. Emitting a fabricated number for an unimplemented metric would violate CLAUDE.md's "a
/// faster wrong answer is still wrong" spirit applied to reporting.
/// </summary>
public static class StatisticWriter
{
    public static void Write(string path, int teleportsTotal, int teleportsJam = 0, int teleportsYield = 0, int teleportsWrongLane = 0)
    {
        using var writer = new StreamWriter(path, append: false);
        Write(writer, teleportsTotal, teleportsJam, teleportsYield, teleportsWrongLane);
    }

    public static void Write(TextWriter writer, int teleportsTotal, int teleportsJam = 0, int teleportsYield = 0, int teleportsWrongLane = 0)
    {
        writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        writer.WriteLine("<statistics>");
        writer.Write("    <teleports total=\"");
        writer.Write(teleportsTotal.ToString(CultureInfo.InvariantCulture));
        writer.Write("\" jam=\"");
        writer.Write(teleportsJam.ToString(CultureInfo.InvariantCulture));
        writer.Write("\" yield=\"");
        writer.Write(teleportsYield.ToString(CultureInfo.InvariantCulture));
        writer.Write("\" wrongLane=\"");
        writer.Write(teleportsWrongLane.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("\"/>");
        writer.WriteLine("</statistics>");
    }
}
