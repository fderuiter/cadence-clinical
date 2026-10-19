using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Cadence.Apps.Interop.Domain;

namespace Cadence.Apps.Interop.Application.Services;

/// <summary>
/// Maps the Observation resources of a posted FHIR Bundle to eSource reconciliation candidates. Only what the
/// bundle carries becomes a candidate: an Observation with a supported LOINC code, an id and a numeric
/// <c>valueQuantity</c>. Nothing is compared with the eCRF here, so every candidate is <c>NOT_COMPARED</c> (#5432).
/// @req:PRD-ESR-001
/// </summary>
public static class FhirObservationMapper
{
    /// <summary>The discrepancy type of a candidate that has not been compared with an eCRF value.</summary>
    public const string NotCompared = "NOT_COMPARED";

    private const string LoincSystem = "http://loinc.org";

    private sealed record CdashTarget(string Domain, string Testcd, string Test);

    private static readonly FrozenDictionary<string, CdashTarget> LoincToCdash = new Dictionary<string, CdashTarget>(StringComparer.Ordinal)
    {
        ["8480-6"] = new("VS", "SYSBP", "Systolic Blood Pressure"),
        ["8462-4"] = new("VS", "DIABP", "Diastolic Blood Pressure"),
        ["8867-4"] = new("VS", "PULSE", "Pulse Rate"),
        ["8310-5"] = new("VS", "TEMP", "Body Temperature"),
        ["29463-7"] = new("VS", "WEIGHT", "Weight"),
        ["8302-2"] = new("VS", "HEIGHT", "Height"),
        ["2339-0"] = new("LB", "GLUC", "Glucose"),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The source and subject the mapped candidates are filed under.</summary>
    public sealed record Target(string StudyId, string SiteId, string SubjectId, string SubjectPseudonym, string EhrSystemId);

    /// <summary>The candidates mapped from a bundle, and the Observations that were skipped with the reason.</summary>
    public sealed record Result(IReadOnlyList<EhrReconciliationCandidateRecord> Candidates, IReadOnlyList<string> Skipped);

    /// <summary>
    /// Maps <paramref name="bundleJson"/>. Throws <see cref="FormatException"/> when it is not a FHIR Bundle.
    /// </summary>
    public static Result Map(string bundleJson, Target target, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(target);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bundleJson);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The FHIR bundle is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("resourceType", out var type)
                || type.GetString() != "Bundle")
            {
                throw new FormatException("The payload is not a FHIR Bundle.");
            }

            var candidates = new List<EhrReconciliationCandidateRecord>();
            var skipped = new List<string>();
            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                return new Result(candidates, skipped);
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!resource.TryGetProperty("resourceType", out var resourceType) || resourceType.GetString() != "Observation")
                {
                    continue;
                }

                var candidate = MapObservation(resource, target, now, out var reason);
                if (candidate is null)
                {
                    skipped.Add(reason);
                }
                else
                {
                    candidates.Add(candidate);
                }
            }

            return new Result(candidates, skipped);
        }
    }

    private static EhrReconciliationCandidateRecord? MapObservation(JsonElement resource, Target target, DateTimeOffset now, out string reason)
    {
        string id = resource.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(id))
        {
            reason = "Observation without an id";
            return null;
        }

        string? loinc = FindLoincCode(resource);
        if (loinc is null || !LoincToCdash.TryGetValue(loinc, out var cdash))
        {
            reason = $"Observation/{id}: no supported LOINC code";
            return null;
        }

        if (!resource.TryGetProperty("valueQuantity", out var quantity)
            || !quantity.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.Number)
        {
            reason = $"Observation/{id}: no numeric valueQuantity";
            return null;
        }

        DateTimeOffset? effective = null;
        if (resource.TryGetProperty("effectiveDateTime", out var effectiveElement)
            && DateTimeOffset.TryParse(effectiveElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            effective = parsed;
        }

        if (effective is null)
        {
            reason = $"Observation/{id}: no effectiveDateTime";
            return null;
        }

        string? unit = quantity.TryGetProperty("unit", out var unitElement) ? unitElement.GetString() : null;

        reason = string.Empty;
        return new EhrReconciliationCandidateRecord
        {
            StudyId = target.StudyId,
            SiteId = target.SiteId,
            SubjectId = target.SubjectId,
            SubjectPseudonym = target.SubjectPseudonym,
            EhrSystemId = target.EhrSystemId,
            FhirResourceType = "Observation",
            FhirResourceId = id,
            SourceTimestamp = effective.Value,
            TargetDomain = cdash.Domain,
            TargetVariable = cdash.Testcd,
            CdashTestcd = cdash.Testcd,
            CdashTest = cdash.Test,
            CandidateValueJson = value.GetRawText(),
            CandidateUnit = unit,
            CurrentEcrfValueJson = null,
            CurrentEcrfUpdatedAt = null,
            DiscrepancyType = NotCompared,
            ConfidenceScore = 1.0,
            MappingProvenance = $"FHIR Observation.code LOINC {loinc} -> CDASH {cdash.Domain}.{cdash.Testcd}",
            RawFhirResourceJson = resource.GetRawText(),
            Status = "PENDING_REVIEW",
            CreatedAt = now,
        };
    }

    private static string? FindLoincCode(JsonElement resource)
    {
        if (!resource.TryGetProperty("code", out var code)
            || !code.TryGetProperty("coding", out var codings)
            || codings.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var coding in codings.EnumerateArray())
        {
            if (coding.TryGetProperty("system", out var system) && system.GetString() == LoincSystem
                && coding.TryGetProperty("code", out var codeValue))
            {
                return codeValue.GetString();
            }
        }

        return null;
    }
}
