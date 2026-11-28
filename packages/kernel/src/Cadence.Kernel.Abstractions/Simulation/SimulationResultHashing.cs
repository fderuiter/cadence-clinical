using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Simulation;

/// <summary>
/// Canonical JSON serialization and SHA-256 content hashing for <see cref="SimulationResult"/> (K-SIM-3, K-SIM-5),
/// shared by simulation evaluation and saved simulation report verification.
/// </summary>
public static class SimulationResultHashing
{
    /// <summary>Computes the deterministic <c>sha256:...</c> result hash of <paramref name="result"/> (excluding <see cref="SimulationResult.ResultHash"/> itself).</summary>
    public static string ComputeResultHash(SimulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ContentHash.ComputeStreaming(writer => SimulationCanonicalJsonWriter.WriteCanonicalResult(writer, result));
    }

    /// <summary>Serializes <paramref name="result"/> (without <see cref="SimulationResult.ResultHash"/>) to deterministic canonical JSON.</summary>
    public static string ToCanonicalJson(SimulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var document = new JsonObject
        {
            ["scope"] = result.Scope.ToString(),
            ["asOf"] = InstantPair(result.AsOf),
            ["candidate"] = CandidateNode(result.Candidate),
            ["engineVersions"] = EnginesNode(result.EngineVersions),
            ["inputHash"] = result.InputHash,
            ["structuralDiff"] = new JsonArray([.. result.StructuralDiff.Select(ChangeNode)]),
            ["scheduleChanges"] = new JsonArray([.. result.ScheduleChanges.Select(ScheduleChangeNode)]),
            ["dependents"] = DependentsNode(result.Dependents),
            ["matrixGaps"] = MatrixGapsNode(result.MatrixGaps),
            ["operationalImpact"] = OperationalNode(result.OperationalImpact),
            ["projectionChanges"] = ProjectionChangesNode(result.ProjectionChanges),
            ["validationDelta"] = ValidationDeltaNode(result.ValidationDelta),
            ["masked"] = MaskedNode(result.Masked),
        };
        return document.ToJsonString();
    }

    /// <summary>Formats <paramref name="asOf"/> at kernel stored microsecond precision.</summary>
    public static JsonObject InstantPair(SimulationAsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(asOf);
        return new JsonObject
        {
            ["validAt"] = Instant(asOf.ValidAt),
            ["knownAt"] = Instant(asOf.KnownAt),
        };
    }

    /// <summary>Formats <paramref name="candidate"/> as a canonical JSON object.</summary>
    public static JsonObject CandidateNode(CandidateSummary candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new JsonObject
        {
            ["kind"] = candidate.Kind.ToString(),
            ["version"] = candidate.Version?.ToString(),
            ["baseVersion"] = candidate.BaseVersion?.ToString(),
            ["contentHash"] = candidate.ContentHash,
            ["baseContentHash"] = candidate.BaseContentHash,
        };
    }

    /// <summary>Formats <paramref name="engines"/> as a canonical JSON array.</summary>
    public static JsonArray EnginesNode(IReadOnlyList<EngineVersion> engines) =>
        new([.. engines.Select(e => (JsonNode?)new JsonObject { ["name"] = e.Name, ["version"] = e.Version })]);

    /// <summary>Formats <paramref name="masked"/> as a canonical JSON array.</summary>
    public static JsonArray MaskedNode(IReadOnlyList<MaskedPart> masked) =>
        new([.. masked.Select(m => (JsonNode?)new JsonObject { ["part"] = m.Part, ["reason"] = m.Reason.ToString() })]);

    internal static string Instant(DateTimeOffset value) =>
        KernelTimestamps.ToStoredPrecision(value).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    private static JsonNode ChangeNode(ElementChange change) => new JsonObject
    {
        ["elementId"] = change.ElementId,
        ["instanceType"] = change.InstanceType,
        ["change"] = change.Change.ToString(),
        ["properties"] = new JsonArray([.. change.Properties.Select(p => (JsonNode?)JsonValue.Create(p))]),
    };

    private static JsonNode ScheduleChangeNode(ScheduleChange change) => new JsonObject
    {
        ["activityId"] = change.ActivityId,
        ["change"] = change.Change.ToString(),
        ["before"] = VisitNode(change.Before),
        ["after"] = VisitNode(change.After),
    };

    private static JsonNode? VisitNode(PlannedVisit? visit) => visit is null
        ? null
        : new JsonObject
        {
            ["epochId"] = visit.EpochId,
            ["studyDay"] = ExpectedSchedule.NormalizeStudyDay(visit.StudyDay),
            ["windowBefore"] = visit.WindowBefore,
            ["windowAfter"] = visit.WindowAfter,
        };

    private static JsonNode? DependentsNode(DependentsReport? dependents) => dependents is null
        ? null
        : new JsonObject
        {
            ["start"] = dependents.Start,
            ["depth"] = dependents.Depth,
            ["total"] = dependents.Total,
            ["truncated"] = dependents.Truncated,
            ["groups"] = new JsonArray([.. dependents.Groups.Select(GroupNode)]),
        };

    private static JsonObject HiddenNode(HiddenObjects hidden) => new()
    {
        ["moreExist"] = hidden.MoreExist,
        ["reasons"] = new JsonArray([.. hidden.Reasons.Select(r => (JsonNode?)JsonValue.Create(r.ToString()))]),
    };

    private static JsonNode? MatrixGapsNode(IReadOnlyList<MatrixGapFinding>? gaps) => gaps is null
        ? null
        : new JsonArray([.. gaps.Select(g => (JsonNode?)new JsonObject
        {
            ["elementId"] = g.ElementId,
            ["kind"] = g.Kind.ToString(),
            ["severity"] = g.Severity.ToString(),
            ["jsonPointerPath"] = g.JsonPointerPath,
            ["description"] = g.Description,
            ["recommendation"] = g.Recommendation,
        })]);

    private static JsonNode GroupNode(DependentGroup group) => new JsonObject
    {
        ["kind"] = group.Kind,
        ["count"] = group.Count,
        ["objects"] = new JsonArray([.. group.Objects.Select(o => (JsonNode?)new JsonObject
        {
            ["handle"] = o.Handle,
            ["mask"] = o.Mask?.ToString(),
            ["distance"] = o.Distance,
        })]),
    };

    private static JsonNode? OperationalNode(OperationalImpactSummary? impact) => impact is null
        ? null
        : new JsonObject
        {
            ["activeSiteCount"] = impact.ActiveSiteCount,
            ["pendingSiteCount"] = impact.PendingSiteCount,
            ["totalParticipantCount"] = impact.TotalParticipantCount,
            ["affectedParticipantCount"] = impact.AffectedParticipantCount,
            ["reconsentParticipantCount"] = impact.ReconsentParticipantCount,
            ["participants"] = new JsonArray([.. impact.ParticipantImpacts.Select(ParticipantImpactNode)]),
            ["hidden"] = HiddenNode(impact.Hidden),
        };

    private static JsonNode ParticipantImpactNode(ParticipantSimulationImpact participant) => new JsonObject
    {
        ["enrollment"] = participant.Enrollment.ToString(),
        ["subjectCode"] = participant.SubjectCode,
        ["status"] = participant.Status.ToString(),
        ["cohort"] = participant.Cohort.ToString(),
        ["requiresReconsent"] = participant.RequiresReconsent,
        ["scheduleShifts"] = new JsonArray([.. participant.ScheduleShifts.Select(ParticipantShiftNode)]),
    };

    private static JsonNode ParticipantShiftNode(ParticipantScheduleShift shift) => new JsonObject
    {
        ["activityId"] = shift.ActivityId,
        ["change"] = shift.Change.ToString(),
        ["before"] = ParticipantVisitNode(shift.Before),
        ["after"] = ParticipantVisitNode(shift.After),
    };

    private static JsonNode? ParticipantVisitNode(ParticipantPlannedVisit? visit) => visit is null
        ? null
        : new JsonObject
        {
            ["visitId"] = visit.VisitId,
            ["targetDate"] = visit.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["windowStart"] = visit.WindowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["windowEnd"] = visit.WindowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

    private static JsonNode? ProjectionChangesNode(IReadOnlyList<ProjectionDatasetChange>? changes) => changes is null
        ? null
        : new JsonArray([.. changes.Select(c => (JsonNode?)new JsonObject
        {
            ["dataset"] = c.Dataset,
            ["addedCount"] = c.AddedCount,
            ["removedCount"] = c.RemovedCount,
            ["changedCount"] = c.ChangedCount,
            ["samples"] = new JsonArray([.. c.Samples.Select(SampleNode)]),
        })]);

    private static JsonNode SampleNode(ProjectionRecordChangeSample sample) => new JsonObject
    {
        ["recordKey"] = sample.RecordKey,
        ["change"] = sample.Change.ToString(),
        ["before"] = DictionaryNode(sample.Before),
        ["after"] = DictionaryNode(sample.After),
    };

    private static JsonNode? DictionaryNode(IReadOnlyDictionary<string, string?>? values)
    {
        if (values is null)
        {
            return null;
        }

        var obj = new JsonObject();
        foreach (var (key, value) in values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            obj[key] = value;
        }

        return obj;
    }

    private static JsonNode? ValidationDeltaNode(ValidationDeltaSummary? delta) => delta is null
        ? null
        : new JsonObject
        {
            ["opening"] = new JsonArray([.. delta.Opening.Select(FindingNode)]),
            ["resolving"] = new JsonArray([.. delta.Resolving.Select(FindingNode)]),
            ["unchangedCount"] = delta.UnchangedCount,
        };

    private static JsonNode FindingNode(SimulatedValidationFinding finding) => new JsonObject
    {
        ["ruleId"] = finding.RuleId,
        ["ruleVersion"] = finding.RuleVersion,
        ["severity"] = finding.Severity.ToString(),
        ["stage"] = finding.Stage.ToString(),
        ["dataset"] = finding.Dataset,
        ["subject"] = finding.Subject,
        ["explanation"] = finding.Explanation,
    };
}
