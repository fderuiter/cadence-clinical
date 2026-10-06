using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Interop.Domain;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Interop.Services;

public static class FhirBundleTransformer
{
    public static async Task<List<EhrReconciliationCandidateRecord>> TransformAndDiffAsync(
        string rawFhirJson,
        string studyId,
        string siteId,
        string subjectId,
        string ehrSystemId,
        ExecutionDbContext executionDb,
        IUcumNormalizationService ucumService,
        DateTimeOffset now)
    {
        var resultCandidates = new List<EhrReconciliationCandidateRecord>();
        if (string.IsNullOrWhiteSpace(rawFhirJson))
        {
            return resultCandidates;
        }

        var jsonElements = ExtractObservationElementJson(rawFhirJson);
        var pseudo = subjectId.StartsWith("PAT-", StringComparison.OrdinalIgnoreCase) ? subjectId : $"PAT-{subjectId}";

        // Pre-query existing active ClinicalObservations for subject/study from executionDb
        var activeObservations = await executionDb.ClinicalObservations
            .AsNoTracking()
            .Where(o => o.IsActive && o.StudyId.Value == studyId && o.SubjectId.Value == subjectId)
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (var root in jsonElements)
        {
            var fhirResourceId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");

            // Timestamp
            DateTimeOffset sourceTimestamp = now;
            if (root.TryGetProperty("effectiveDateTime", out var effProp) && DateTimeOffset.TryParse(effProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEff))
            {
                sourceTimestamp = parsedEff;
            }
            else if (root.TryGetProperty("issued", out var issuedProp) && DateTimeOffset.TryParse(issuedProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedIssued))
            {
                sourceTimestamp = parsedIssued;
            }

            // LOINC code
            string? loincCode = null;
            if (root.TryGetProperty("code", out var codeEl) && codeEl.TryGetProperty("coding", out var codingArr) && codingArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var coding in codingArr.EnumerateArray())
                {
                    var sys = coding.TryGetProperty("system", out var sysProp) ? sysProp.GetString() : null;
                    var code = coding.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(code) && (sys == null || sys.Contains("loinc", StringComparison.OrdinalIgnoreCase)))
                    {
                        loincCode = code;
                        break;
                    }
                }
            }

            var mapping = LoincToCdashMapper.TryMapLoinc(loincCode ?? string.Empty);
            string targetDomain = mapping?.TargetDomain ?? "VS";
            string targetVariable = mapping?.TargetVariable ?? "OBS";
            string cdashTestcd = mapping?.CdashTestcd ?? targetVariable;
            string cdashTest = mapping?.CdashTest ?? targetVariable;
            string provenance = mapping?.MappingProvenance ?? $"FHIR Observation.code {loincCode} -> CDASH {targetDomain}.{targetVariable}";

            // Candidate Value & Unit
            string? candValJson = null;
            double? candValNum = null;
            string? candUnit = null;

            if (root.TryGetProperty("valueQuantity", out var vq) && vq.ValueKind == JsonValueKind.Object)
            {
                if (vq.TryGetProperty("value", out var vProp) && vProp.TryGetDouble(out var vDouble))
                {
                    candValNum = vDouble;
                    candValJson = vDouble.ToString(CultureInfo.InvariantCulture);
                }
                if (vq.TryGetProperty("unit", out var uProp))
                {
                    candUnit = uProp.GetString();
                }
                else if (vq.TryGetProperty("code", out var uCodeProp))
                {
                    candUnit = uCodeProp.GetString();
                }
            }
            else if (root.TryGetProperty("valueString", out var vsProp))
            {
                candValJson = vsProp.GetString();
            }

            candValJson ??= "{}";

            // Find matching existing eCRF observation in ExecutionDbContext
            var matchingObs = activeObservations.FirstOrDefault(o =>
                string.Equals(o.Domain.Value, targetDomain, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.TestCode.Value, cdashTestcd, StringComparison.OrdinalIgnoreCase));

            string discrepancyType = "NEW_FIELD";
            double confidenceScore = 0.95;
            string? currentEcrfValJson = null;
            DateTimeOffset? currentEcrfUpdatedAt = null;

            if (matchingObs != null)
            {
                currentEcrfValJson = matchingObs.Value?.ToString(CultureInfo.InvariantCulture) ?? matchingObs.ValueString;
                currentEcrfUpdatedAt = new DateTimeOffset(matchingObs.ObservationDate, TimeSpan.Zero);

                // Diffing logic
                if (candValNum.HasValue && matchingObs.Value.HasValue)
                {
                    double candNum = candValNum.Value;
                    double ecrfNum = matchingObs.Value.Value;

                    var candNorm = ucumService.GetNormalizedRepresentation(candNum, candUnit);
                    var ecrfNorm = ucumService.GetNormalizedRepresentation(ecrfNum, matchingObs.Unit);

                    bool normValuesEqual = candNorm.NormalizedValue.HasValue && ecrfNorm.NormalizedValue.HasValue
                        ? Math.Abs(candNorm.NormalizedValue.Value - ecrfNorm.NormalizedValue.Value) < 0.001
                        : Math.Abs(candNum - ecrfNum) < 0.001;

                    bool rawUnitsEqual = string.Equals(candUnit, matchingObs.Unit, StringComparison.OrdinalIgnoreCase);

                    if (normValuesEqual && rawUnitsEqual)
                    {
                        var timeDiff = Math.Abs((sourceTimestamp - currentEcrfUpdatedAt.Value).TotalDays);
                        if (timeDiff > 2.0)
                        {
                            discrepancyType = "DATE_MISMATCH";
                            confidenceScore = 0.89;
                        }
                        else
                        {
                            discrepancyType = "EXACT_MATCH";
                            confidenceScore = 1.0;
                        }
                    }
                    else if (normValuesEqual && !rawUnitsEqual)
                    {
                        discrepancyType = "UNIT_MISMATCH";
                        confidenceScore = 0.92;
                    }
                    else
                    {
                        discrepancyType = "VALUE_MISMATCH";
                        confidenceScore = 0.98;
                    }
                }
                else if (string.Equals(candValJson, currentEcrfValJson, StringComparison.OrdinalIgnoreCase))
                {
                    discrepancyType = "EXACT_MATCH";
                    confidenceScore = 1.0;
                }
                else
                {
                    discrepancyType = "VALUE_MISMATCH";
                    confidenceScore = 0.98;
                }
            }

            var record = new EhrReconciliationCandidateRecord
            {
                Id = Guid.NewGuid().ToString(),
                StudyId = studyId,
                SiteId = siteId,
                SubjectId = subjectId,
                SubjectPseudonym = pseudo,
                EhrSystemId = ehrSystemId,
                FhirResourceType = "Observation",
                FhirResourceId = fhirResourceId,
                SourceTimestamp = sourceTimestamp,
                TargetDomain = targetDomain,
                TargetVariable = targetVariable,
                CdashTestcd = cdashTestcd,
                CdashTest = cdashTest,
                CandidateValueJson = candValJson,
                CandidateUnit = candUnit,
                CurrentEcrfValueJson = currentEcrfValJson,
                CurrentEcrfUpdatedAt = currentEcrfUpdatedAt,
                DiscrepancyType = discrepancyType,
                ConfidenceScore = confidenceScore,
                MappingProvenance = provenance,
                RawFhirResourceJson = root.GetRawText(),
                Status = "PENDING_REVIEW",
                CreatedAt = now
            };

            resultCandidates.Add(record);
        }

        return resultCandidates;
    }

    private static List<JsonElement> ExtractObservationElementJson(string rawJson)
    {
        var list = new List<JsonElement>();
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("resourceType", out var rt) && rt.GetString() == "Bundle")
                {
                    if (doc.RootElement.TryGetProperty("entry", out var entryArr) && entryArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in entryArr.EnumerateArray())
                        {
                            if (entry.TryGetProperty("resource", out var res) && res.ValueKind == JsonValueKind.Object && res.TryGetProperty("resourceType", out var innerRt) && innerRt.GetString() == "Observation")
                            {
                                list.Add(res.Clone());
                            }
                        }
                    }
                }
                else if (doc.RootElement.TryGetProperty("resourceType", out var singleRt) && singleRt.GetString() == "Observation")
                {
                    list.Add(doc.RootElement.Clone());
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("resourceType", out var rtItem) && rtItem.GetString() == "Observation")
                    {
                        list.Add(item.Clone());
                    }
                }
            }
        }
        catch
        {
            // Invalid json input or unable to parse
        }

        return list;
    }
}
