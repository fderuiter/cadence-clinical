using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Observations;

/// <summary>
/// Dynamic JSONB property bag for laboratory and custom observation properties (ADR-2329).
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public sealed class ObservationAdditionalProperties
{
    [JsonPropertyName("lab_source")]
    public string? LabSource { get; set; }

    [JsonPropertyName("lab_site_id")]
    public string? LabSiteId { get; set; }

    [JsonPropertyName("lab_indicator")]
    public string? LabIndicator { get; set; }

    [JsonPropertyName("lab_out_of_range")]
    public bool? LabOutOfRange { get; set; }

    [JsonPropertyName("matched_normal_bounds")]
    public string? MatchedNormalBounds { get; set; }

    [JsonPropertyName("range_indicator")]
    public string? RangeIndicator { get; set; }

    [JsonPropertyName("is_out_of_range")]
    public bool? IsOutOfRange { get; set; }

    [JsonPropertyName("reference_range_low")]
    public double? ReferenceRangeLow { get; set; }

    [JsonPropertyName("reference_range_high")]
    public double? ReferenceRangeHigh { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?> CustomProperties { get; set; } = new();

    public ObservationAdditionalProperties Clone()
    {
        var clone = new ObservationAdditionalProperties
        {
            LabSource = LabSource,
            LabSiteId = LabSiteId,
            LabIndicator = LabIndicator,
            LabOutOfRange = LabOutOfRange,
            MatchedNormalBounds = MatchedNormalBounds,
            RangeIndicator = RangeIndicator,
            IsOutOfRange = IsOutOfRange,
            ReferenceRangeLow = ReferenceRangeLow,
            ReferenceRangeHigh = ReferenceRangeHigh,
        };

        foreach (var (k, v) in CustomProperties)
        {
            clone.CustomProperties[k] = v;
        }

        return clone;
    }
}
