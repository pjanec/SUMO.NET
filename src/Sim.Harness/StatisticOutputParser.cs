using System.Globalization;
using System.Xml.Linq;

namespace Sim.Harness;

/// <summary>
/// P0-D: reads a SUMO-schema <c>--statistic-output</c> file's <c>&lt;teleports .../&gt;</c> child
/// into a <see cref="StatisticRecord"/> -- the same "read only the subset the comparator needs"
/// pattern as <see cref="TripInfoParser"/>/<see cref="SummaryOutputParser"/>. A real SUMO
/// statistic file carries several sibling elements (<c>performance</c>, <c>vehicles</c>,
/// <c>safety</c>, <c>persons</c>, <c>personTeleports</c>) this parser deliberately ignores, since
/// docs/HIGH-DENSITY-P0-DESIGN.md "P0-D" only requires <c>teleports</c> to be graded.
/// </summary>
public static class StatisticOutputParser
{
    public static StatisticRecord Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static StatisticRecord Parse(Stream stream) => ParseDocument(XDocument.Load(stream));

    public static StatisticRecord ParseXml(string xml) => ParseDocument(XDocument.Parse(xml));

    private static StatisticRecord ParseDocument(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("statistic document has no root element.");
        var teleports = root.Element("teleports")
            ?? throw new InvalidDataException("<statistics> is missing required child <teleports>.");

        return new StatisticRecord(
            TeleportsTotal: ParseInt(teleports, "total"),
            TeleportsJam: ParseInt(teleports, "jam"),
            TeleportsYield: ParseInt(teleports, "yield"),
            TeleportsWrongLane: ParseInt(teleports, "wrongLane"));
    }

    private static int ParseInt(XElement element, string name) =>
        int.Parse(
            element.Attribute(name)?.Value
                ?? throw new InvalidDataException($"<{element.Name}> is missing required attribute '{name}'."),
            CultureInfo.InvariantCulture);
}
