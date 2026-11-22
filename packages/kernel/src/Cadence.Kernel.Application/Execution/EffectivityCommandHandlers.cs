using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Execution;

/// <summary>
/// Handles <see cref="DeclareEffectivity"/>. The protocol version must exist and be approved (or superseded), a study-site
/// scope must belong to its study, and the version may have only one effectivity per scope.
/// </summary>
internal sealed class DeclareEffectivityHandler : ICommandHandler<DeclareEffectivity, EffectivityState>
{
    public async Task<CommandResult<EffectivityState>> HandleAsync(DeclareEffectivity command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (command.Scope?.Problem() is { } problem)
        {
            return EffectivityErrors.ScopeInvalid.WithMessage(problem);
        }

        if (await context.Work.ExistsAsync<Effectivity>(command.EffectivityId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return EffectivityErrors.ProtocolVersionUnknown;
        }

        if (version.Status is not (ProtocolVersionStatus.Approved or ProtocolVersionStatus.Superseded))
        {
            return EffectivityErrors.ProtocolVersionNotApproved;
        }

        var scope = command.Scope!;
        if (scope.StudySiteId is { } studySiteId
            && (await context.Work.ReadAsync<StudySite>(studySiteId, cancellationToken).ConfigureAwait(false))?.StudyId != version.StudyId)
        {
            return EffectivityErrors.StudySiteNotInStudy;
        }

        var versionId = command.ProtocolVersionId;
        var declared = await context.Work.ReadAllAsync<Effectivity>(e => e.ProtocolVersionId == versionId, cancellationToken).ConfigureAwait(false);
        if (declared.Any(e => e.Scope == scope))
        {
            return EffectivityErrors.AlreadyDeclared;
        }

        var created = Effectivity.Declare(
            command.EffectivityId, command.TenantId, version.StudyId, versionId, scope, command.DeclaredAt, context.RecordedAt);
        if (created.Value is not { } effectivity)
        {
            return created.Error!;
        }

        context.Work.Add(effectivity);
        context.Work.LinkInput(NodeRef.For(version.Id, version.VersionIndex), LineageEdgeKind.GovernedBy, effectivity.StatusSince);
        context.Work.Raise(
            new EffectivityDeclared(effectivity.TenantId, effectivity.Id, versionId, version.StudyId, scope, effectivity.StatusSince),
            effectivity.StatusSince);
        return EffectivityStates.Of(effectivity);
    }
}

/// <summary>
/// Handles <see cref="ChangeEffectivityStatus"/>. Becoming <see cref="EffectivityStatus.Effective"/> requires ethics or
/// regulatory evidence (K-TIME-5), an approved protocol version, and evaluates the version's <see cref="ProtocolAmendment"/>
/// <see cref="TransitionPolicy"/> across open enrollments at covered study sites (spec §7.5, K-TIME-6, PRD-SUB-007).
/// </summary>
internal sealed class ChangeEffectivityStatusHandler : ICommandHandler<ChangeEffectivityStatus, EffectivityState>
{
    public async Task<CommandResult<EffectivityState>> HandleAsync(ChangeEffectivityStatus command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ReadAsync<Effectivity>(command.EffectivityId, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            return KernelErrors.NotFound;
        }

        await context.Work.LockStudyGovernanceAsync(snapshot.StudyId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.FindAsync<Effectivity>(command.EffectivityId, cancellationToken).ConfigureAwait(false) is not { } effectivity)
        {
            return KernelErrors.NotFound;
        }

        var evidence = command.Evidence ?? [];
        if (effectivity.CheckTransition(command.To, command.EffectiveAt, context.RecordedAt) is { } invalid)
        {
            return invalid;
        }

        if (evidence.Count > Effectivity.MaxEvidence)
        {
            return EffectivityErrors.EvidenceInvalid.WithMessage($"Name at most {Effectivity.MaxEvidence} distinct artifact versions.");
        }

        if (await EvidenceProblemAsync(effectivity, command.To, evidence, context, cancellationToken).ConfigureAwait(false) is { } evidenceProblem)
        {
            return evidenceProblem;
        }

        var version = await context.Work.ReadAsync<ProtocolVersion>(effectivity.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        if (command.To == EffectivityStatus.Effective
            && version?.Status is not (ProtocolVersionStatus.Approved or ProtocolVersionStatus.Superseded))
        {
            return EffectivityErrors.ProtocolVersionNotApproved;
        }

        var from = effectivity.Status;
        if (effectivity.ChangeStatus(command.To, evidence, command.EffectiveAt, context.RecordedAt) is { } error)
        {
            return error;
        }

        if (version is not null)
        {
            context.Work.LinkInput(NodeRef.For(version.Id, version.VersionIndex), LineageEdgeKind.GovernedBy, effectivity.StatusSince);
        }

        context.Work.Raise(
            new EffectivityStatusChanged(
                effectivity.TenantId, effectivity.Id, effectivity.ProtocolVersionId, from, effectivity.Status, effectivity.StatusSince, [.. evidence]),
            effectivity.StatusSince);

        if (command.To == EffectivityStatus.Effective && version is not null)
        {
            var applyError = await ApplyEffectiveGovernanceAsync(effectivity, version, context, cancellationToken).ConfigureAwait(false);
            if (applyError is not null)
            {
                return applyError;
            }
        }

        return EffectivityStates.Of(effectivity);
    }

    private static async Task<KernelError?> ApplyEffectiveGovernanceAsync(
        Effectivity effectivity,
        ProtocolVersion version,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        var studyId = effectivity.StudyId;
        var effectiveAt = effectivity.StatusSince;

        var otherEffectivities = await context.Work.ReadAllAsync<Effectivity>(
            e => e.StudyId == studyId && e.Id != effectivity.Id,
            cancellationToken).ConfigureAwait(false);
        List<Effectivity> allEffectivities = [effectivity, .. otherEffectivities];

        var studySites = await context.Work.ReadAllAsync<StudySite>(s => s.StudyId == studyId, cancellationToken).ConfigureAwait(false);
        if (studySites.Count == 0)
        {
            return null;
        }

        var siteIds = studySites.Select(s => s.SiteId).Distinct().ToList();
        var sitesById = (await context.Work.ReadAllAsync<Site>(s => siteIds.Contains(s.Id), cancellationToken).ConfigureAwait(false))
            .ToDictionary(s => s.Id);

        var coveredSiteIds = new HashSet<StudySiteId>();
        foreach (var studySite in studySites)
        {
            if (!sitesById.TryGetValue(studySite.SiteId, out var site)
                || !effectivity.Scope.Covers(studySite.Id, site.Country))
            {
                continue;
            }

            // Ensure a more-specific pending/withdrawn effectivity for the same version does not get bypassed.
            var deciding = EffectivityResolution.EffectiveAt(allEffectivities, studySite.Id, site.Country, effectiveAt, context.RecordedAt);
            if (deciding.Any(d => d.Id == effectivity.Id))
            {
                coveredSiteIds.Add(studySite.Id);
            }
        }

        if (coveredSiteIds.Count == 0)
        {
            return null;
        }

        var enrollments = (await context.Work.ReadAllAsync<Enrollment>(
            e => e.StudyId == studyId && coveredSiteIds.Contains(e.StudySiteId),
            cancellationToken).ConfigureAwait(false))
            .Where(e => EnrollmentTransitions.IsActive(e.Status))
            .ToList();
        if (enrollments.Count == 0)
        {
            return null;
        }

        var versionsById = (await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.Id);
        var enrollmentIds = enrollments.Select(e => e.Id).ToList();
        var applicabilitySnapshots = (await context.Work.ReadAllAsync<Applicability>(
            a => enrollmentIds.Contains(a.EnrollmentId),
            cancellationToken).ConfigureAwait(false))
            .ToDictionary(a => a.EnrollmentId);
        var consentsByEnrollment = (await context.Work.ReadAllAsync<ConsentStatus>(
            c => enrollmentIds.Contains(c.EnrollmentId),
            cancellationToken).ConfigureAwait(false))
            .GroupBy(c => c.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var studySitesById = studySites.ToDictionary(s => s.Id);

        foreach (var enrollment in enrollments)
        {
            var studySite = studySitesById[enrollment.StudySiteId];
            var site = sitesById[studySite.SiteId];
            applicabilitySnapshots.TryGetValue(enrollment.Id, out var existingSnapshot);

            if (version.Amendment is null)
            {
                if (existingSnapshot?.GoverningVersionId is not null)
                {
                    continue;
                }

                var initialValidFrom = enrollment.StatusSince > effectiveAt ? enrollment.StatusSince : effectiveAt;
                if (existingSnapshot is not null)
                {
                    var tracked = (await context.Work.FindAsync<Applicability>(existingSnapshot.Id, cancellationToken).ConfigureAwait(false))!;
                    if (tracked.TransitionTo(
                        version.Id,
                        Applicability.InitialBasis,
                        initialValidFrom,
                        context.RecordedAt,
                        effectivityId: effectivity.Id) is { } err)
                    {
                        return err;
                    }

                    context.Work.Raise(
                        new ApplicabilityEstablished(
                            enrollment.TenantId, tracked.Id, enrollment.Id, enrollment.ParticipantId, studyId, version.Id, initialValidFrom),
                        initialValidFrom);
                }
                else
                {
                    var created = Applicability.Establish(
                        ApplicabilityId.New(),
                        enrollment.TenantId,
                        enrollment.Id,
                        enrollment.ParticipantId,
                        studyId,
                        enrollment.StudySiteId,
                        version.Id,
                        Applicability.InitialBasis,
                        initialValidFrom,
                        context.RecordedAt,
                        effectivityId: effectivity.Id);
                    if (created.Value is not { } applicability)
                    {
                        return created.Error!;
                    }

                    context.Work.Add(applicability);
                    context.Work.Raise(
                        new ApplicabilityEstablished(
                            enrollment.TenantId, applicability.Id, enrollment.Id, enrollment.ParticipantId, studyId, version.Id, initialValidFrom),
                        initialValidFrom);
                }

                continue;
            }

            var policy = ApplicabilityGovernance.ResolvePolicy(
                version.Amendment, enrollment, studySite, site, existingSnapshot, versionsById);
            switch (policy)
            {
                case TransitionPolicy.NewParticipantsOnly:
                    // Existing enrollments remain on their prior governing protocol version.
                    if (existingSnapshot is null && enrollment.StatusSince >= effectiveAt)
                    {
                        var created = Applicability.Establish(
                            ApplicabilityId.New(),
                            enrollment.TenantId,
                            enrollment.Id,
                            enrollment.ParticipantId,
                            studyId,
                            enrollment.StudySiteId,
                            version.Id,
                            Applicability.InitialBasis,
                            enrollment.StatusSince,
                            context.RecordedAt,
                            effectivityId: effectivity.Id);
                        if (created.Value is { } fresh)
                        {
                            context.Work.Add(fresh);
                            context.Work.Raise(
                                new ApplicabilityEstablished(
                                    enrollment.TenantId, fresh.Id, enrollment.Id, enrollment.ParticipantId, studyId, version.Id, enrollment.StatusSince),
                                enrollment.StatusSince);
                        }
                    }

                    break;

                case TransitionPolicy.Automatic:
                    {
                        var transitionAt = enrollment.StatusSince > effectiveAt ? enrollment.StatusSince : effectiveAt;
                        var basis = version.Amendment.TransitionPolicy == TransitionPolicy.ByCohort
                            ? Applicability.CohortAmendmentBasis
                            : Applicability.AutomaticAmendmentBasis;

                        if (existingSnapshot is not null)
                        {
                            var tracked = (await context.Work.FindAsync<Applicability>(existingSnapshot.Id, cancellationToken).ConfigureAwait(false))!;
                            var priorVersion = tracked.GoverningVersionId;
                            if (tracked.TransitionTo(version.Id, basis, transitionAt, context.RecordedAt, effectivityId: effectivity.Id) is { } err)
                            {
                                return err;
                            }

                            context.Work.Raise(
                                new ParticipantProtocolTransitioned(
                                    enrollment.TenantId, tracked.Id, enrollment.Id, enrollment.ParticipantId, studyId, priorVersion, version.Id, transitionAt),
                                transitionAt);
                        }
                        else
                        {
                            var created = Applicability.Establish(
                                ApplicabilityId.New(),
                                enrollment.TenantId,
                                enrollment.Id,
                                enrollment.ParticipantId,
                                studyId,
                                enrollment.StudySiteId,
                                version.Id,
                                basis,
                                transitionAt,
                                context.RecordedAt,
                                effectivityId: effectivity.Id);
                            if (created.Value is not { } fresh)
                            {
                                return created.Error!;
                            }

                            context.Work.Add(fresh);
                            context.Work.Raise(
                                new ParticipantProtocolTransitioned(
                                    enrollment.TenantId, fresh.Id, enrollment.Id, enrollment.ParticipantId, studyId, null, version.Id, transitionAt),
                                transitionAt);
                        }

                        break;
                    }

                case TransitionPolicy.RequiresReconsent:
                    {
                        var matchingConsent = consentsByEnrollment.TryGetValue(enrollment.Id, out var enrollmentConsents)
                            ? enrollmentConsents
                                .Where(c => c.Signed
                                    && versionsById.TryGetValue(c.ProtocolVersionId, out var cv)
                                    && ApplicabilityGovernance.CoveredVersionChain(cv, versionsById).Contains(version.Id))
                                .OrderByDescending(c => versionsById[c.ProtocolVersionId].Number)
                                .FirstOrDefault()
                            : null;

                        if (matchingConsent is not null)
                        {
                            var targetConsentVersion = matchingConsent.ProtocolVersionId;
                            var transitionAt = matchingConsent.SignedAt > effectiveAt ? matchingConsent.SignedAt : effectiveAt;
                            if (existingSnapshot is not null)
                            {
                                var tracked = (await context.Work.FindAsync<Applicability>(existingSnapshot.Id, cancellationToken).ConfigureAwait(false))!;
                                var priorVersion = tracked.GoverningVersionId;
                                if (tracked.TransitionTo(
                                    targetConsentVersion,
                                    Applicability.ConsentBasis,
                                    transitionAt,
                                    context.RecordedAt,
                                    effectivityId: effectivity.Id,
                                    consentStatusId: matchingConsent.Id) is { } err)
                                {
                                    return err;
                                }

                                context.Work.Raise(
                                    new ParticipantProtocolTransitioned(
                                        enrollment.TenantId, tracked.Id, enrollment.Id, enrollment.ParticipantId, studyId, priorVersion, targetConsentVersion, transitionAt),
                                    transitionAt);
                            }
                            else
                            {
                                var created = Applicability.Establish(
                                    ApplicabilityId.New(),
                                    enrollment.TenantId,
                                    enrollment.Id,
                                    enrollment.ParticipantId,
                                    studyId,
                                    enrollment.StudySiteId,
                                    targetConsentVersion,
                                    Applicability.ConsentBasis,
                                    transitionAt,
                                    context.RecordedAt,
                                    effectivityId: effectivity.Id,
                                    consentStatusId: matchingConsent.Id);
                                if (created.Value is not { } fresh)
                                {
                                    return created.Error!;
                                }

                                context.Work.Add(fresh);
                                context.Work.Raise(
                                    new ParticipantProtocolTransitioned(
                                        enrollment.TenantId, fresh.Id, enrollment.Id, enrollment.ParticipantId, studyId, null, targetConsentVersion, transitionAt),
                                    transitionAt);
                            }
                        }
                        else
                        {
                            Applicability tracked;
                            if (existingSnapshot is not null)
                            {
                                tracked = (await context.Work.FindAsync<Applicability>(existingSnapshot.Id, cancellationToken).ConfigureAwait(false))!;
                            }
                            else
                            {
                                tracked = Applicability.CreateUnestablished(
                                    ApplicabilityId.New(),
                                    enrollment.TenantId,
                                    enrollment.Id,
                                    enrollment.ParticipantId,
                                    studyId,
                                    enrollment.StudySiteId);
                                context.Work.Add(tracked);
                            }

                            if (tracked.HoldForReconsent(version.Id, effectivity.Id, effectiveAt, context.RecordedAt) is { } err)
                            {
                                return err;
                            }

                            context.Work.Raise(
                                new ParticipantReconsentRequired(
                                    enrollment.TenantId,
                                    tracked.Id,
                                    enrollment.Id,
                                    enrollment.ParticipantId,
                                    studyId,
                                    tracked.GoverningVersionId,
                                    version.Id,
                                    effectivity.Id,
                                    effectiveAt),
                                effectiveAt);
                        }

                        break;
                    }
            }
        }

        return null;
    }

    private static async Task<KernelError?> EvidenceProblemAsync(
        Effectivity effectivity, EffectivityStatus to, IReadOnlyList<ArtifactVersionId> evidence, ICommandContext context, CancellationToken cancellationToken)
    {
        var accepted = EffectivityTransitions.EvidenceKinds(to);
        if (evidence.Count > 0 && accepted.Count == 0)
        {
            return EffectivityErrors.EvidenceInvalid.WithMessage($"A move to {to} takes no evidence.");
        }

        if (evidence.Distinct().Count() != evidence.Count)
        {
            return EffectivityErrors.EvidenceInvalid.WithMessage("Each artifact version is named once.");
        }

        foreach (var versionId in evidence)
        {
            var id = versionId;
            var artifacts = await context.Work.ReadAllAsync<Artifact>(a => a.Versions.Any(v => v.Id == id), cancellationToken).ConfigureAwait(false);
            if (artifacts.SingleOrDefault() is not { } artifact
                || !accepted.Contains(artifact.Kind)
                || (artifact.StudyId is { } study && study != effectivity.StudyId))
            {
                return EffectivityErrors.EvidenceInvalid.WithMessage(
                    $"Artifact version {versionId} does not exist, belongs to another study, or is not one of: {string.Join(", ", accepted.Order())}.");
            }
        }

        return null;
    }
}

/// <summary>
/// Handles <see cref="ActivateStudySite"/>: the study site becomes active only when a protocol version is in force there
/// at the command's recorded time (K-TIME-4).
/// </summary>
internal sealed class ActivateStudySiteHandler : ICommandHandler<ActivateStudySite, StudySiteState>
{
    /// <summary>The status an active study site has.</summary>
    public const string ActiveStatus = "Active";

    public async Task<CommandResult<StudySiteState>> HandleAsync(ActivateStudySite command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<StudySite>(command.StudySiteId, cancellationToken).ConfigureAwait(false) is not { } studySite)
        {
            return EffectivityErrors.StudySiteUnknown;
        }

        if (string.Equals(studySite.Status, ActiveStatus, StringComparison.Ordinal))
        {
            return EffectivityErrors.StudySiteAlreadyActive;
        }

        var site = await context.Work.ReadAsync<Site>(studySite.SiteId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Study site {studySite.Id} names site {studySite.SiteId}, which does not exist.");
        var studyId = studySite.StudyId;
        var effectivities = await context.Work.ReadAllAsync<Effectivity>(e => e.StudyId == studyId, cancellationToken).ConfigureAwait(false);
        var effective = await EffectiveVersionsAsync(
            EffectivityResolution.EffectiveAt(effectivities, studySite.Id, site.Country, context.RecordedAt, context.RecordedAt), context, cancellationToken)
            .ConfigureAwait(false);
        if (effective.Count == 0)
        {
            return EffectivityErrors.NoEffectiveVersion;
        }

        studySite.Activate(command.ReasonForChange);
        context.Work.Raise(new StudySiteActivated(studySite.TenantId, studySite.Id, studyId, [.. effective.Select(v => v.ProtocolVersionId)]));
        return CommandResult<StudySiteState>.Success(new StudySiteState(studySite.Id, studySite.Status, effective));
    }

    private static async Task<IReadOnlyList<EffectiveVersion>> EffectiveVersionsAsync(
        IReadOnlyList<Effectivity> deciding, ICommandContext context, CancellationToken cancellationToken)
    {
        var versions = new List<EffectiveVersion>(deciding.Count);
        foreach (var effectivity in deciding)
        {
            var version = await context.Work.ReadAsync<ProtocolVersion>(effectivity.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
            versions.Add(new EffectiveVersion(effectivity.ProtocolVersionId, version?.Number ?? 0, effectivity.Id, effectivity.ScopeLevel));
        }

        return [.. versions.OrderByDescending(v => v.Number)];
    }
}

/// <summary>
/// Handles <see cref="RecordConsentStatus"/> (spec §7.5, §9.4, PRD-SUB-007). Persists bitemporal <see cref="ConsentStatus"/>,
/// resolves every pending re-consent transition covered by the consented protocol version (and its amendment ancestor chain),
/// and establishes or advances bitemporal <see cref="Applicability"/> at <c>SignedAt</c>.
/// </summary>
internal sealed class RecordConsentStatusHandler : ICommandHandler<RecordConsentStatus, ConsentStatusState>
{
    public async Task<CommandResult<ConsentStatusState>> HandleAsync(
        RecordConsentStatus command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ReadAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return ConsentErrors.EnrollmentUnknown;
        }

        if (await context.Work.ReadAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is not { } version
            || version.StudyId != enrollment.StudyId)
        {
            return ConsentErrors.ProtocolVersionUnknown;
        }

        var signedAt = KernelTimestamps.ToStoredPrecision(command.SignedAt);
        if (signedAt > context.RecordedAt)
        {
            return ConsentErrors.SignedAtInvalid;
        }

        var scopes = command.Scopes;
        if (scopes is null || scopes.Count == 0 || scopes.Any(s => s is null || string.IsNullOrWhiteSpace(s.Purpose) || !Enum.IsDefined(s.Decision)))
        {
            return ConsentErrors.ScopesInvalid;
        }

        await context.Work.LockStudyGovernanceAsync(enrollment.StudyId, cancellationToken).ConfigureAwait(false);

        var existingForPair = (await context.Work.ReadAllAsync<ConsentStatus>(
            c => c.EnrollmentId == enrollment.Id && c.ProtocolVersionId == version.Id,
            cancellationToken).ConfigureAwait(false)).SingleOrDefault();

        ConsentStatus consentStatus;
        if (existingForPair is not null)
        {
            consentStatus = (await context.Work.FindAsync<ConsentStatus>(existingForPair.Id, cancellationToken).ConfigureAwait(false))!;
            if (consentStatus.Update(
                command.Signed,
                signedAt,
                command.BasisKind,
                scopes,
                context.RecordedAt,
                command.AttestationId,
                command.ArtifactVersionId) is { } updateError)
            {
                return updateError;
            }
        }
        else
        {
            if (await context.Work.ExistsAsync<ConsentStatus>(command.ConsentStatusId, cancellationToken).ConfigureAwait(false))
            {
                return KernelErrors.AlreadyExists;
            }

            var recorded = ConsentStatus.Record(
                command.ConsentStatusId,
                command.TenantId,
                enrollment.Id,
                enrollment.ParticipantId,
                enrollment.StudyId,
                enrollment.StudySiteId,
                version.Id,
                command.Signed,
                signedAt,
                command.BasisKind,
                scopes,
                context.RecordedAt,
                command.AttestationId,
                command.ArtifactVersionId);
            if (recorded.Value is not { } created)
            {
                return recorded.Error!;
            }

            consentStatus = created;
            context.Work.Add(consentStatus);
        }

        context.Work.LinkInput(NodeRef.For(version.Id, version.VersionIndex), LineageEdgeKind.GovernedBy, signedAt);
        context.Work.Raise(
            new ConsentStatusRecorded(
                consentStatus.TenantId,
                consentStatus.Id,
                enrollment.Id,
                enrollment.ParticipantId,
                enrollment.StudyId,
                version.Id,
                consentStatus.Signed,
                consentStatus.SignedAt,
                consentStatus.BasisKind,
                consentStatus.CurrentScopes()),
            consentStatus.SignedAt);

        var studyId = enrollment.StudyId;
        var allVersions = await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false);
        var versionsById = allVersions.ToDictionary(v => v.Id);
        var versionNumbers = allVersions.ToDictionary(v => v.Id, v => v.Number);

        var existingApplicabilitySnapshot = (await context.Work.ReadAllAsync<Applicability>(
            a => a.EnrollmentId == enrollment.Id,
            cancellationToken).ConfigureAwait(false)).SingleOrDefault();

        Applicability? finalApplicability = existingApplicabilitySnapshot;

        bool mainStudyGranted = command.Signed
            && scopes.Any(s => s.Decision == ConsentDecision.Granted
                && (string.Equals(s.Purpose, ConsentPurposes.MainStudy, StringComparison.OrdinalIgnoreCase) || scopes.Count == 1));
        if (!mainStudyGranted && command.Signed)
        {
            mainStudyGranted = scopes.Any(s => s.Decision == ConsentDecision.Granted);
        }

        if (mainStudyGranted)
        {
            var coveredVersions = ApplicabilityGovernance.CoveredVersionChain(version, versionsById);
            var hadCoveredPending = existingApplicabilitySnapshot?.CurrentPendingTransitions()
                .Any(t => coveredVersions.Contains(t.TargetVersionId)) is true;
            var latestRequiredSince = existingApplicabilitySnapshot?.CurrentPendingTransitions()
                .Where(t => coveredVersions.Contains(t.TargetVersionId))
                .Select(t => (DateTimeOffset?)t.RequiredSince)
                .Max();

            // Check whether version is effective at the enrollment's study site as of signedAt or recordedAt.
            DateTimeOffset? siteEffectiveSince = null;
            if (await context.Work.ReadAsync<StudySite>(enrollment.StudySiteId, cancellationToken).ConfigureAwait(false) is { } studySite
                && await context.Work.ReadAsync<Site>(studySite.SiteId, cancellationToken).ConfigureAwait(false) is { } site)
            {
                var effectivities = await context.Work.ReadAllAsync<Effectivity>(e => e.StudyId == studyId, cancellationToken).ConfigureAwait(false);
                var decidingAtSigned = EffectivityResolution.EffectiveAt(effectivities, studySite.Id, site.Country, signedAt, context.RecordedAt)
                    .FirstOrDefault(e => e.ProtocolVersionId == version.Id);
                var decidingAtNow = decidingAtSigned
                    ?? EffectivityResolution.EffectiveAt(effectivities, studySite.Id, site.Country, context.RecordedAt, context.RecordedAt)
                        .FirstOrDefault(e => e.ProtocolVersionId == version.Id);
                siteEffectiveSince = decidingAtNow?.StatusSince;
            }

            var currentGovNumber = existingApplicabilitySnapshot?.GoverningVersionId is { } curGov && versionNumbers.TryGetValue(curGov, out var n)
                ? n
                : 0;
            bool shouldAdvance = hadCoveredPending
                || existingApplicabilitySnapshot?.GoverningVersionId is null
                || (siteEffectiveSince is not null && version.Number >= currentGovNumber);

            if (existingApplicabilitySnapshot is not null && (hadCoveredPending || shouldAdvance))
            {
                var tracked = (await context.Work.FindAsync<Applicability>(existingApplicabilitySnapshot.Id, cancellationToken).ConfigureAwait(false))!;
                if (tracked.ResolvePendingTransitions(coveredVersions, signedAt, context.RecordedAt) is { } resolveErr)
                {
                    return resolveErr;
                }

                if (shouldAdvance && version.Number >= currentGovNumber)
                {
                    var transitionAt = signedAt;
                    if (latestRequiredSince is { } req && req > transitionAt)
                    {
                        transitionAt = req;
                    }

                    if (siteEffectiveSince is { } eff && eff > transitionAt && eff <= context.RecordedAt)
                    {
                        transitionAt = eff;
                    }

                    var priorVersion = tracked.GoverningVersionId;
                    if (tracked.TransitionTo(
                        version.Id,
                        Applicability.ConsentBasis,
                        transitionAt,
                        context.RecordedAt,
                        consentStatusId: consentStatus.Id) is { } transErr)
                    {
                        return transErr;
                    }

                    if (priorVersion != version.Id)
                    {
                        context.Work.Raise(
                            priorVersion is null
                                ? new ApplicabilityEstablished(
                                    enrollment.TenantId, tracked.Id, enrollment.Id, enrollment.ParticipantId, studyId, version.Id, transitionAt)
                                : new ParticipantProtocolTransitioned(
                                    enrollment.TenantId, tracked.Id, enrollment.Id, enrollment.ParticipantId, studyId, priorVersion, version.Id, transitionAt),
                            transitionAt);
                    }
                }

                finalApplicability = tracked;
            }
            else if (existingApplicabilitySnapshot is null && siteEffectiveSince is not null)
            {
                var validFrom = siteEffectiveSince.Value > signedAt && siteEffectiveSince.Value <= context.RecordedAt
                    ? siteEffectiveSince.Value
                    : signedAt;
                var established = Applicability.Establish(
                    ApplicabilityId.New(),
                    enrollment.TenantId,
                    enrollment.Id,
                    enrollment.ParticipantId,
                    studyId,
                    enrollment.StudySiteId,
                    version.Id,
                    Applicability.ConsentBasis,
                    validFrom,
                    context.RecordedAt,
                    consentStatusId: consentStatus.Id);
                if (established.Value is not { } createdApplicability)
                {
                    return established.Error!;
                }

                context.Work.Add(createdApplicability);
                context.Work.Raise(
                    new ApplicabilityEstablished(
                        enrollment.TenantId,
                        createdApplicability.Id,
                        enrollment.Id,
                        enrollment.ParticipantId,
                        studyId,
                        version.Id,
                        validFrom),
                    validFrom);
                finalApplicability = createdApplicability;
            }
        }

        return CommandResult<ConsentStatusState>.Success(new ConsentStatusState(
            consentStatus.Id,
            consentStatus.EnrollmentId,
            consentStatus.ParticipantId,
            consentStatus.StudyId,
            consentStatus.StudySiteId,
            consentStatus.ProtocolVersionId,
            version.Number,
            consentStatus.Signed,
            consentStatus.SignedAt,
            consentStatus.BasisKind,
            consentStatus.AttestationId,
            consentStatus.ArtifactVersionId,
            consentStatus.CurrentScopes(),
            finalApplicability is null ? null : ApplicabilityGovernance.ToState(finalApplicability, versionNumbers)));
    }
}

/// <summary>
/// Handles <see cref="FlagApplicabilityInconsistency"/> (spec §7.5, K-TIME-6).
/// </summary>
internal sealed class FlagApplicabilityInconsistencyHandler : ICommandHandler<FlagApplicabilityInconsistency, ApplicabilityState>
{
    public async Task<CommandResult<ApplicabilityState>> HandleAsync(
        FlagApplicabilityInconsistency command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ReadAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return ConsentErrors.EnrollmentUnknown;
        }

        await context.Work.LockStudyGovernanceAsync(enrollment.StudyId, cancellationToken).ConfigureAwait(false);
        var existing = (await context.Work.ReadAllAsync<Applicability>(
            a => a.EnrollmentId == enrollment.Id,
            cancellationToken).ConfigureAwait(false)).SingleOrDefault();

        Applicability applicability;
        if (existing is not null)
        {
            applicability = (await context.Work.FindAsync<Applicability>(existing.Id, cancellationToken).ConfigureAwait(false))!;
        }
        else
        {
            applicability = Applicability.CreateUnestablished(
                ApplicabilityId.New(),
                enrollment.TenantId,
                enrollment.Id,
                enrollment.ParticipantId,
                enrollment.StudyId,
                enrollment.StudySiteId);
            context.Work.Add(applicability);
        }

        applicability.SetInconsistency(command.IsInconsistent, command.InconsistencyReason);
        context.Work.Raise(new ApplicabilityInconsistencyFlagged(
            enrollment.TenantId,
            applicability.Id,
            enrollment.Id,
            enrollment.ParticipantId,
            enrollment.StudyId,
            applicability.IsInconsistent,
            applicability.InconsistencyReason));

        var studyId = enrollment.StudyId;
        var versionNumbers = (await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.Id, v => v.Number);
        return CommandResult<ApplicabilityState>.Success(ApplicabilityGovernance.ToState(applicability, versionNumbers));
    }
}

/// <summary>Builds <see cref="EffectivityState"/> results.</summary>
internal static class EffectivityStates
{
    public static CommandResult<EffectivityState> Of(Effectivity effectivity) =>
        CommandResult<EffectivityState>.Success(new EffectivityState(
            effectivity.Id, effectivity.ProtocolVersionId, effectivity.Scope, effectivity.Status, effectivity.StatusSince, effectivity.CurrentEvidence()));
}

/// <summary>Registers the effectivity, consent, and study-site activation handlers (spec §7.1, §7.5, §9.4).</summary>
public static class EffectivityServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelEffectivityHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<DeclareEffectivity, EffectivityState>, DeclareEffectivityHandler>();
        services.AddTransient<ICommandHandler<ChangeEffectivityStatus, EffectivityState>, ChangeEffectivityStatusHandler>();
        services.AddTransient<ICommandHandler<ActivateStudySite, StudySiteState>, ActivateStudySiteHandler>();
        services.AddTransient<ICommandHandler<RecordConsentStatus, ConsentStatusState>, RecordConsentStatusHandler>();
        services.AddTransient<ICommandHandler<FlagApplicabilityInconsistency, ApplicabilityState>, FlagApplicabilityInconsistencyHandler>();
        return services;
    }
}
