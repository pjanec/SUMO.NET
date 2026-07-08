using System.Globalization;
using System.Xml.Linq;

namespace Sim.Harness;

/// <summary>
/// VB-6: reads a SUMO-schema tripinfo file into <see cref="TripInfoRecord"/>s. Deliberately reads
/// ONLY the subset of attributes the aggregate comparator needs (id, depart, duration,
/// arrivalSpeed) and ignores everything else -- which is exactly why this same parser can read
/// BOTH a real SUMO <c>--tripinfo-output</c> file (root <c>&lt;tripinfos&gt;</c>, many more
/// attributes: routeLength, waitingTime, rerouteNo, devices, vType, speedFactor, vaporized, ...)
/// AND the engine's tripinfo ANALOG emitted by the VB-7 benchmark runner (same root/element/
/// attribute names, only the subset below populated). One schema, one loader, two producers --
/// see VIZ_BENCH_TASKS.md VB-6's "clear input schema/loader for both" requirement.
///
/// <c>duration</c> is read directly if present (both SUMO and the engine analog always write it);
/// if a producer ever omits it, it is derived from <c>arrival - depart</c> as a fallback.
/// </summary>
public static class TripInfoParser
{
    public static IReadOnlyList<TripInfoRecord> Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static IReadOnlyList<TripInfoRecord> Parse(Stream stream) => ParseDocument(XDocument.Load(stream));

    public static IReadOnlyList<TripInfoRecord> ParseXml(string xml) => ParseDocument(XDocument.Parse(xml));

    private static IReadOnlyList<TripInfoRecord> ParseDocument(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("tripinfo document has no root element.");

        var records = new List<TripInfoRecord>();
        foreach (var el in root.Elements("tripinfo"))
        {
            var id = RequireAttribute(el, "id");
            var depart = ParseDouble(el, "depart");
            var duration = TryParseDouble(el, "duration")
                ?? (TryParseDouble(el, "arrival") is { } arrival ? arrival - depart : throw new InvalidDataException(
                    $"<tripinfo id='{id}'> has neither 'duration' nor 'arrival' -- cannot derive trip duration."));
            var arrivalSpeed = TryParseDouble(el, "arrivalSpeed");

            records.Add(new TripInfoRecord(id, depart, duration, arrivalSpeed));
        }

        return records;
    }

    private static string RequireAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value
        ?? throw new InvalidDataException($"<{element.Name}> is missing required attribute '{name}'.");

    private static double ParseDouble(XElement element, string name) =>
        double.Parse(RequireAttribute(element, name), CultureInfo.InvariantCulture);

    private static double? TryParseDouble(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return value is null ? null : double.Parse(value, CultureInfo.InvariantCulture);
    }
}
