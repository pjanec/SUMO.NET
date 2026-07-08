using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sim.Harness;

/// <summary>
/// VB-6: per-rung tolerance for <see cref="AggregateComparator"/> -- deliberately a SEPARATE type
/// from <see cref="ToleranceConfig"/> (which governs vehicle-for-vehicle exact/statistical
/// trajectory parity). This benchmark comparator is not a parity gate (BENCHMARK_SPEC.md
/// "Success criteria"): every tolerance here is a RELATIVE fraction applied to the SUMO reference
/// value, "far looser than parity" by design, and there is no exact mode -- only the one
/// aggregate-agreement mode this file describes.
///
/// JSON shape (mirrors tolerance.json's flat style):
/// <code>
/// {
///   "arrivedRelTol": 0.3,
///   "meanDurationRelTol": 0.3,
///   "meanSpeedRelTol": 0.3,
///   "distributionDistanceTol": 0.3
/// }
/// </code>
/// </summary>
public sealed class AggregateToleranceConfig
{
    /// <summary>Relative tolerance on total-vehicles-arrived: |cand-ref|/ref &lt;= this.</summary>
    public required double ArrivedRelTol { get; init; }

    /// <summary>Relative tolerance on mean trip duration.</summary>
    public required double MeanDurationRelTol { get; init; }

    /// <summary>Relative tolerance on mean network speed.</summary>
    public required double MeanSpeedRelTol { get; init; }

    /// <summary>
    /// Absolute tolerance on the two-sample Kolmogorov-Smirnov statistic (max absolute CDF gap,
    /// range [0,1]) between the reference and candidate trip-duration distributions.
    /// </summary>
    public required double DistributionDistanceTol { get; init; }

    public static AggregateToleranceConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadFrom(stream, path);
    }

    public static AggregateToleranceConfig Parse(string json) =>
        LoadFrom(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)), sourceName: "<inline>");

    private static AggregateToleranceConfig LoadFrom(Stream stream, string sourceName)
    {
        var dto = JsonSerializer.Deserialize<Dto>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"aggregate tolerance config is empty: {sourceName}");

        return new AggregateToleranceConfig
        {
            ArrivedRelTol = dto.ArrivedRelTol ?? throw Missing(sourceName, "arrivedRelTol"),
            MeanDurationRelTol = dto.MeanDurationRelTol ?? throw Missing(sourceName, "meanDurationRelTol"),
            MeanSpeedRelTol = dto.MeanSpeedRelTol ?? throw Missing(sourceName, "meanSpeedRelTol"),
            DistributionDistanceTol = dto.DistributionDistanceTol ?? throw Missing(sourceName, "distributionDistanceTol"),
        };
    }

    private static InvalidDataException Missing(string sourceName, string field) =>
        new($"aggregate tolerance config '{sourceName}' is missing required field '{field}'.");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class Dto
    {
        [JsonPropertyName("arrivedRelTol")]
        public double? ArrivedRelTol { get; set; }

        [JsonPropertyName("meanDurationRelTol")]
        public double? MeanDurationRelTol { get; set; }

        [JsonPropertyName("meanSpeedRelTol")]
        public double? MeanSpeedRelTol { get; set; }

        [JsonPropertyName("distributionDistanceTol")]
        public double? DistributionDistanceTol { get; set; }
    }
}
