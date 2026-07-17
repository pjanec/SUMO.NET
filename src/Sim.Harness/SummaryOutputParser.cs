using System.Globalization;
using System.Xml.Linq;

namespace Sim.Harness;

/// <summary>
/// VB-6: reads a SUMO-schema summary file into <see cref="SummaryStepRecord"/>s. Like
/// <see cref="TripInfoParser"/>, this reads only the subset of attributes the aggregate
/// comparator needs (time, running, arrived, meanSpeed -- P0-D adds halting, stopped,
/// meanSpeedRelative) so the SAME loader reads both a real SUMO <c>--summary-output</c> file
/// (root <c>&lt;summary&gt;</c>, many more per-step attributes: loaded, inserted, waiting, ended,
/// collisions, teleports, meanWaitingTime, meanTravelTime, duration) and the engine's summary
/// ANALOG (VB-7 benchmark runner / P0-D <c>SummaryWriterObserver</c>).
///
/// SUMO writes <c>meanSpeed="-1.00"</c>/<c>meanSpeedRelative="-1.00"</c> as a sentinel for "no
/// on-road, non-stopped vehicles this step" -- preserved here as <see
/// cref="SummaryStepRecord.MeanSpeed"/>/<see cref="SummaryStepRecord.MeanSpeedRelative"/> == null
/// (any negative value is treated as the sentinel, not just exactly -1, since floating formatting
/// could in principle differ) rather than a spurious -1 sample the comparator could accidentally
/// average in.
///
/// P0-D: <c>halting</c>/<c>stopped</c>/<c>meanSpeedRelative</c> are read as OPTIONAL attributes
/// (default 0 / sentinel-null) rather than required -- older/synthetic summary XML (this file's
/// own pre-P0-D unit tests, hand-written inline fixtures) that omits them still parses unchanged.
/// </summary>
public static class SummaryOutputParser
{
    public static IReadOnlyList<SummaryStepRecord> Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static IReadOnlyList<SummaryStepRecord> Parse(Stream stream) => ParseDocument(XDocument.Load(stream));

    public static IReadOnlyList<SummaryStepRecord> ParseXml(string xml) => ParseDocument(XDocument.Parse(xml));

    private static IReadOnlyList<SummaryStepRecord> ParseDocument(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("summary document has no root element.");

        var records = new List<SummaryStepRecord>();
        foreach (var el in root.Elements("step"))
        {
            var time = ParseDouble(el, "time");
            var running = ParseInt(el, "running");
            var arrived = ParseInt(el, "arrived");
            var meanSpeedRaw = TryParseDouble(el, "meanSpeed");
            var meanSpeed = meanSpeedRaw is { } v && v >= 0.0 ? v : (double?)null;
            var halting = TryParseInt(el, "halting");
            var stopped = TryParseInt(el, "stopped");
            var meanSpeedRelativeRaw = TryParseDouble(el, "meanSpeedRelative");
            var meanSpeedRelative = meanSpeedRelativeRaw is { } vr && vr >= 0.0 ? vr : (double?)null;

            records.Add(new SummaryStepRecord(time, running, arrived, meanSpeed, halting, stopped, meanSpeedRelative));
        }

        return records;
    }

    private static string RequireAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value
        ?? throw new InvalidDataException($"<{element.Name}> is missing required attribute '{name}'.");

    private static double ParseDouble(XElement element, string name) =>
        double.Parse(RequireAttribute(element, name), CultureInfo.InvariantCulture);

    private static int ParseInt(XElement element, string name) =>
        int.Parse(RequireAttribute(element, name), CultureInfo.InvariantCulture);

    private static double? TryParseDouble(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return value is null ? null : double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static int TryParseInt(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return value is null ? 0 : int.Parse(value, CultureInfo.InvariantCulture);
    }
}
