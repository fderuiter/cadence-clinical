using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Application.Simulation;

internal static class SimulationResultSealer
{
    internal static CommandResult<SimulationResult> SealResult(
        NodeRef scope,
        SimulationAsOf asOf,
        CandidateSummary summary,
        IReadOnlyList<EngineVersion> engines,
        string? visibleStateDigest,
        IReadOnlyList<ElementChange> diff,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyList<MaskedPart> masked,
        DependentsReport? dependents,
        IReadOnlyList<MatrixGapFinding>? matrixGaps,
        OperationalImpactSummary? operationalImpact,
        IReadOnlyList<ProjectionDatasetChange>? projectionChanges,
        ValidationDeltaSummary? validationDelta)
    {
        var inputHash = ComputeInputHash(scope, asOf, summary, engines, masked, visibleStateDigest);
        var preliminary = new SimulationResult(
            scope,
            asOf,
            summary,
            engines,
            inputHash,
            string.Empty,
            diff,
            schedule,
            masked,
            dependents,
            matrixGaps,
            operationalImpact,
            projectionChanges,
            validationDelta);
        var resultHash = SimulationResultHashing.ComputeResultHash(preliminary);
        return CommandResult<SimulationResult>.Success(preliminary with { ResultHash = resultHash });
    }

    internal static IReadOnlyList<MaskedPart> CombineMaskedParts(
        IReadOnlyList<MaskedPart> snapshotParts,
        IReadOnlyList<MaskReason> dependentMaskReasons,
        bool maskedBlindedFinding)
    {
        var set = new SortedSet<(string Part, MaskReason Reason)>(
            Comparer<(string Part, MaskReason Reason)>.Create((a, b) =>
            {
                var cmp = string.CompareOrdinal(a.Part, b.Part);
                return cmp != 0 ? cmp : a.Reason.CompareTo(b.Reason);
            }));

        foreach (var part in snapshotParts)
        {
            set.Add((part.Part, part.Reason));
        }

        foreach (var depReason in dependentMaskReasons)
        {
            set.Add((SimulationEngine.DependentsPart, depReason));
        }

        if (maskedBlindedFinding)
        {
            set.Add((SimulationSnapshotMasking.BlindedTreatmentPart, MaskReason.Blinded));
        }

        return [.. set.Select(x => new MaskedPart(x.Part, x.Reason))];
    }

    internal static IReadOnlyList<ScheduleChange> ScheduleChanges(string? baseContent, string candidateContent)
    {
        var before = baseContent is null ? [] : ExpectedSchedule.Plan(DesignOf(baseContent));
        var after = ExpectedSchedule.Plan(DesignOf(candidateContent));
        var beforeById = before.ToDictionary(v => v.ActivityId, StringComparer.Ordinal);
        var afterById = after.ToDictionary(v => v.ActivityId, StringComparer.Ordinal);
        var changes = new List<ScheduleChange>();
        foreach (var id in beforeById.Keys.Union(afterById.Keys).Order(StringComparer.Ordinal))
        {
            var old = beforeById.GetValueOrDefault(id);
            var @new = afterById.GetValueOrDefault(id);
            if (old is null)
            {
                changes.Add(new ScheduleChange(id, ElementChangeKind.Added, null, @new));
            }
            else if (@new is null)
            {
                changes.Add(new ScheduleChange(id, ElementChangeKind.Removed, old, null));
            }
            else if (old.EpochId != @new.EpochId
                || ExpectedSchedule.NormalizeStudyDay(old.StudyDay) != ExpectedSchedule.NormalizeStudyDay(@new.StudyDay)
                || old.WindowBefore != @new.WindowBefore
                || old.WindowAfter != @new.WindowAfter)
            {
                changes.Add(new ScheduleChange(id, ElementChangeKind.Changed, old, @new));
            }
        }

        return changes;

        static ProtocolDesign DesignOf(string content) =>
            ProtocolDesignDocument.Read(content).Design
            ?? throw new InvalidOperationException("Simulation compared content that is not a protocol design.");
    }

    internal static IReadOnlyList<MatrixGapFinding> EvaluateMatrixGaps(string content)
    {
        var read = ProtocolDesignDocument.Read(content);
        return read.Design is { } design ? MatrixGapAnalyzer.Analyze(design) : [];
    }

    private static string ComputeInputHash(
        NodeRef scope,
        SimulationAsOf asOf,
        CandidateSummary candidate,
        IReadOnlyList<EngineVersion> engines,
        IReadOnlyList<MaskedPart> masked,
        string? visibleStateDigest) =>
        ContentHash.ComputeStreaming(writer =>
        {
            writer.WriteStartObject();

            writer.WritePropertyName("as_of"u8);
            writer.WriteStartObject();
            writer.WriteString("known_at"u8, asOf.KnownAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("valid_at"u8, asOf.ValidAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteEndObject();

            writer.WritePropertyName("candidate"u8);
            writer.WriteStartObject();
            writer.WriteString("base_content_hash"u8, candidate.BaseContentHash);
            writer.WriteString("base_version"u8, candidate.BaseVersion?.ToString());
            writer.WriteString("content_hash"u8, candidate.ContentHash);
            writer.WriteString("kind"u8, candidate.Kind.ToString());
            writer.WriteString("version"u8, candidate.Version?.ToString());
            writer.WriteEndObject();

            writer.WritePropertyName("engines"u8);
            writer.WriteStartArray();
            for (var i = 0; i < engines.Count; i++)
            {
                var engine = engines[i];
                writer.WriteStartObject();
                writer.WriteString("name"u8, engine.Name);
                writer.WriteString("version"u8, engine.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("masked"u8);
            writer.WriteStartArray();
            for (var i = 0; i < masked.Count; i++)
            {
                var part = masked[i];
                writer.WriteStartObject();
                writer.WriteString("part"u8, part.Part);
                writer.WriteString("reason"u8, part.Reason.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteString("scope"u8, scope.ToString());
            if (visibleStateDigest is not null)
            {
                writer.WriteString("visible_state_digest"u8, visibleStateDigest);
            }

            writer.WriteEndObject();
        });
}
