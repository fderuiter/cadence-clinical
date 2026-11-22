using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Application.Execution;

/// <summary>
/// Shared bitemporal <see cref="Applicability"/> governance logic for enrollment, effectivity, and consent handlers
/// (spec §7.5, §9.4, K-TIME-6, PRD-SUB-007).
/// </summary>
internal static class ApplicabilityGovernance
{
    public static ApplicabilityState ToState(
        Applicability applicability,
        IReadOnlyDictionary<ProtocolVersionId, int> versionNumbers)
    {
        var pending = applicability.CurrentPendingTransitions();
        return new ApplicabilityState(
            applicability.Id,
            applicability.EnrollmentId,
            applicability.ParticipantId,
            applicability.StudyId,
            applicability.StudySiteId,
            applicability.GoverningVersionId,
            applicability.GoverningVersionId is { } gov ? versionNumbers.GetValueOrDefault(gov) : null,
            applicability.ValidFrom,
            applicability.IsInconsistent,
            applicability.InconsistencyReason,
            [.. pending.Select(p => new PendingReconsentTransition(
                p.FromVersionId,
                p.TargetVersionId,
                versionNumbers.GetValueOrDefault(p.TargetVersionId),
                p.EffectivityId,
                p.RequiredSince))]);
    }

    public static HashSet<ProtocolVersionId> CoveredVersionChain(
        ProtocolVersion consentedVersion,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersion> versionsById)
    {
        var covered = new HashSet<ProtocolVersionId>();
        var current = consentedVersion;
        while (current is not null && covered.Add(current.Id))
        {
            current = current.ParentVersionId is { } parentId && versionsById.TryGetValue(parentId, out var parent)
                ? parent
                : null;
        }

        return covered;
    }

    public static TransitionPolicy ResolvePolicy(
        ProtocolAmendment amendment,
        Enrollment enrollment,
        StudySite studySite,
        Site site,
        Applicability? applicability,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersion> versionsById)
    {
        if (amendment.TransitionPolicy != TransitionPolicy.ByCohort)
        {
            return amendment.TransitionPolicy;
        }

        if (amendment.CohortRules is { Count: > 0 } rules
            && rules.FirstOrDefault(rule => MatchesCohortPredicate(rule.Predicate, enrollment, studySite, site, applicability, versionsById)) is { } matchedRule)
        {
            return matchedRule.Policy;
        }

        return amendment.DefaultCohortPolicy;
    }

    public static bool MatchesCohortPredicate(
        string? predicate,
        Enrollment enrollment,
        StudySite studySite,
        Site site,
        Applicability? applicability,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersion> versionsById)
    {
        if (string.IsNullOrWhiteSpace(predicate))
        {
            return false;
        }

        var trimmed = predicate.Trim();
        if (trimmed is "*"
            || string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var clauses = trimmed.Split(["&&", " AND ", " and ", ";"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return clauses.Length > 0 && clauses.All(c => MatchesClause(c, enrollment, studySite, site, applicability, versionsById));
    }

    public static async Task ClosePriorTerminalParticipantPeriodsAsync(
        StudyId studyId,
        ParticipantId participantId,
        EnrollmentId excludeEnrollmentId,
        DateTimeOffset fallbackCloseAt,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        var priorApplicabilities = await context.Work.ReadAllAsync<Applicability>(
            a => a.StudyId == studyId && a.ParticipantId == participantId && a.EnrollmentId != excludeEnrollmentId,
            cancellationToken).ConfigureAwait(false);

        foreach (var snapshot in priorApplicabilities.Where(a => a.GoverningVersionId is not null))
        {
            var priorEnrollment = await context.Work.ReadAsync<Enrollment>(snapshot.EnrollmentId, cancellationToken).ConfigureAwait(false);
            if (priorEnrollment is not null && EnrollmentTransitions.IsActive(priorEnrollment.Status))
            {
                continue;
            }

            if (await context.Work.FindAsync<Applicability>(snapshot.Id, cancellationToken).ConfigureAwait(false) is { } tracked)
            {
                var closeAt = priorEnrollment?.StatusSince ?? fallbackCloseAt;
                tracked.CloseOpenPeriod(closeAt, context.RecordedAt);
            }
        }
    }

    public static async Task EstablishInitialIfNeededAsync(
        Enrollment enrollment,
        StudySite studySite,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        var existingSnapshot = (await context.Work.ReadAllAsync<Applicability>(
            a => a.EnrollmentId == enrollment.Id,
            cancellationToken).ConfigureAwait(false)).SingleOrDefault();
        if (existingSnapshot?.GoverningVersionId is not null)
        {
            return;
        }

        if (await context.Work.ReadAsync<Site>(studySite.SiteId, cancellationToken).ConfigureAwait(false) is not { } site)
        {
            return;
        }

        var studyId = enrollment.StudyId;
        var effectivities = await context.Work.ReadAllAsync<Effectivity>(e => e.StudyId == studyId, cancellationToken).ConfigureAwait(false);
        if (effectivities.Count == 0)
        {
            return;
        }

        var versions = (await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.Id);

        var atEnrollment = EffectivityResolution.EffectiveAt(
            effectivities, studySite.Id, site.Country, enrollment.StatusSince, context.RecordedAt);
        IReadOnlyList<Effectivity> deciding;
        DateTimeOffset validFrom;
        bool checkLaterAmendments;
        if (atEnrollment.Count > 0)
        {
            deciding = atEnrollment;
            validFrom = enrollment.StatusSince;
            checkLaterAmendments = true;
        }
        else
        {
            var atRecorded = EffectivityResolution.EffectiveAt(
                effectivities, studySite.Id, site.Country, context.RecordedAt, context.RecordedAt);
            if (atRecorded.Count == 0)
            {
                return;
            }

            deciding = atRecorded;
            var chosenEffectivity = deciding
                .Where(e => versions.ContainsKey(e.ProtocolVersionId))
                .OrderByDescending(e => versions[e.ProtocolVersionId].Number)
                .FirstOrDefault();
            if (chosenEffectivity is null)
            {
                return;
            }

            validFrom = enrollment.StatusSince >= chosenEffectivity.StatusSince
                ? enrollment.StatusSince
                : chosenEffectivity.StatusSince;
            checkLaterAmendments = false;
        }

        var candidates = deciding
            .Where(e => versions.ContainsKey(e.ProtocolVersionId))
            .Select(e => (Effectivity: e, Version: versions[e.ProtocolVersionId]))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var maxNumber = candidates.Max(c => c.Version.Number);
        var topCandidates = candidates.Where(c => c.Version.Number == maxNumber).ToList();
        if (topCandidates.Select(c => c.Version.Id).Distinct().Count() > 1)
        {
            var inconsistent = existingSnapshot is not null
                ? await context.Work.FindAsync<Applicability>(existingSnapshot.Id, cancellationToken).ConfigureAwait(false)
                : null;
            if (inconsistent is null)
            {
                inconsistent = Applicability.CreateUnestablished(
                    ApplicabilityId.New(),
                    enrollment.TenantId,
                    enrollment.Id,
                    enrollment.ParticipantId,
                    enrollment.StudyId,
                    enrollment.StudySiteId);
                context.Work.Add(inconsistent);
            }

            var reason = $"Multiple effective protocol versions share version number {maxNumber} at study site {studySite.Id}.";
            inconsistent.SetInconsistency(true, reason);
            context.Work.Raise(new ApplicabilityInconsistencyFlagged(
                enrollment.TenantId, inconsistent.Id, enrollment.Id, enrollment.ParticipantId, enrollment.StudyId, true, reason));
            return;
        }

        var winner = topCandidates[0];
        Applicability applicability;
        if (existingSnapshot is not null)
        {
            applicability = (await context.Work.FindAsync<Applicability>(existingSnapshot.Id, cancellationToken).ConfigureAwait(false))!;
            if (applicability.TransitionTo(
                winner.Version.Id,
                Applicability.InitialBasis,
                validFrom,
                context.RecordedAt,
                effectivityId: winner.Effectivity.Id) is not null)
            {
                return;
            }
        }
        else
        {
            var established = Applicability.Establish(
                ApplicabilityId.New(),
                enrollment.TenantId,
                enrollment.Id,
                enrollment.ParticipantId,
                enrollment.StudyId,
                enrollment.StudySiteId,
                winner.Version.Id,
                Applicability.InitialBasis,
                validFrom,
                context.RecordedAt,
                effectivityId: winner.Effectivity.Id);
            if (established.Value is not { } created)
            {
                return;
            }

            applicability = created;
            context.Work.Add(applicability);
        }

        context.Work.LinkInput(NodeRef.For(winner.Version.Id, winner.Version.VersionIndex), LineageEdgeKind.GovernedBy, validFrom);
        context.Work.Raise(
            new ApplicabilityEstablished(
                enrollment.TenantId,
                applicability.Id,
                enrollment.Id,
                enrollment.ParticipantId,
                enrollment.StudyId,
                winner.Version.Id,
                validFrom),
            validFrom);

        if (checkLaterAmendments)
        {
            var laterAmendments = EffectivityResolution.EffectiveAt(
                    effectivities, studySite.Id, site.Country, context.RecordedAt, context.RecordedAt)
                .Where(e => e.StatusSince > enrollment.StatusSince
                    && versions.TryGetValue(e.ProtocolVersionId, out var v)
                    && v.Number > winner.Version.Number
                    && v.Amendment is not null)
                .OrderBy(e => e.StatusSince)
                .ThenBy(e => versions[e.ProtocolVersionId].Number)
                .ToList();

            foreach (var later in laterAmendments)
            {
                var amendedVersion = versions[later.ProtocolVersionId];
                var policy = ResolvePolicy(amendedVersion.Amendment!, enrollment, studySite, site, applicability, versions);
                switch (policy)
                {
                    case TransitionPolicy.Automatic:
                        var fromVersion = applicability.GoverningVersionId;
                        if (applicability.TransitionTo(
                            amendedVersion.Id,
                            amendedVersion.Amendment!.TransitionPolicy == TransitionPolicy.ByCohort
                                ? Applicability.CohortAmendmentBasis
                                : Applicability.AutomaticAmendmentBasis,
                            later.StatusSince,
                            context.RecordedAt,
                            effectivityId: later.Id) is null)
                        {
                            context.Work.LinkInput(
                                NodeRef.For(amendedVersion.Id, amendedVersion.VersionIndex),
                                LineageEdgeKind.GovernedBy,
                                later.StatusSince);
                            context.Work.Raise(
                                new ParticipantProtocolTransitioned(
                                    enrollment.TenantId,
                                    applicability.Id,
                                    enrollment.Id,
                                    enrollment.ParticipantId,
                                    enrollment.StudyId,
                                    fromVersion,
                                    amendedVersion.Id,
                                    later.StatusSince),
                                later.StatusSince);
                        }

                        break;

                    case TransitionPolicy.RequiresReconsent:
                        if (applicability.HoldForReconsent(amendedVersion.Id, later.Id, later.StatusSince, context.RecordedAt) is null)
                        {
                            context.Work.Raise(
                                new ParticipantReconsentRequired(
                                    enrollment.TenantId,
                                    applicability.Id,
                                    enrollment.Id,
                                    enrollment.ParticipantId,
                                    enrollment.StudyId,
                                    applicability.GoverningVersionId,
                                    amendedVersion.Id,
                                    later.Id,
                                    later.StatusSince),
                                later.StatusSince);
                        }

                        break;

                    case TransitionPolicy.NewParticipantsOnly:
                    default:
                        break;
                }
            }
        }
    }

    private static bool MatchesClause(
        string clause,
        Enrollment enrollment,
        StudySite studySite,
        Site site,
        Applicability? applicability,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersion> versionsById)
    {
        bool negated = false;
        string key;
        string expected;

        if (TrySplitOperator(clause, "!=", out key, out expected))
        {
            negated = true;
        }
        else if (!TrySplitOperator(clause, "==", out key, out expected)
            && !TrySplitOperator(clause, "=", out key, out expected)
            && !TrySplitOperator(clause, ":", out key, out expected))
        {
            return false;
        }

        expected = expected.Trim().Trim('\'', '"');
        key = key.Trim().ToLowerInvariant();

        bool matched = key switch
        {
            "status" or "enrollment_status" =>
                string.Equals(enrollment.Status.ToString(), expected, StringComparison.OrdinalIgnoreCase),
            "country" =>
                string.Equals(site.Country, expected, StringComparison.OrdinalIgnoreCase),
            "site" or "site_code" =>
                string.Equals(site.SiteCode, expected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(studySite.Id.ToString(), expected, StringComparison.OrdinalIgnoreCase),
            "study_site" or "study_site_id" =>
                string.Equals(studySite.Id.ToString(), expected, StringComparison.OrdinalIgnoreCase),
            "subject" or "subject_code" =>
                string.Equals(enrollment.SubjectCode.Value, expected, StringComparison.OrdinalIgnoreCase),
            "enrollment" or "enrollment_id" =>
                string.Equals(enrollment.Id.ToString(), expected, StringComparison.OrdinalIgnoreCase),
            "participant" or "participant_id" =>
                string.Equals(enrollment.ParticipantId.ToString(), expected, StringComparison.OrdinalIgnoreCase),
            "version" or "protocol_version" =>
                MatchesVersion(applicability, versionsById, expected),
            "enrolled_before" or "screened_before" =>
                DateTimeOffset.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var cutoff)
                && enrollment.StatusSince < KernelTimestamps.ToStoredPrecision(cutoff),
            _ => false,
        };

        return negated ? !matched : matched;
    }

    private static bool MatchesVersion(
        Applicability? applicability,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersion> versionsById,
        string expected)
    {
        if (applicability?.GoverningVersionId is not { } governingId)
        {
            return false;
        }

        if (string.Equals(governingId.ToString(), expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = expected.StartsWith('v') || expected.StartsWith('V') ? expected[1..] : expected;
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && versionsById.TryGetValue(governingId, out var version)
            && version.Number == number;
    }

    private static bool TrySplitOperator(string input, string op, out string left, out string right)
    {
        var idx = input.IndexOf(op, StringComparison.Ordinal);
        if (idx <= 0)
        {
            left = string.Empty;
            right = string.Empty;
            return false;
        }

        left = input[..idx];
        right = input[(idx + op.Length)..];
        return right.Length > 0;
    }
}
