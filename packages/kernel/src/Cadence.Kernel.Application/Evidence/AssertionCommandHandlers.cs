using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Handles <see cref="ApplyLock"/> (spec §10.5, ADR-2621): it resolves the scope within the study, takes the scope's
/// exclusive advisory lock, which waits for every covered write in flight, refuses a second lock of the same kind there,
/// checks the signature over <see cref="LockStatement.Apply"/> and records the lock.
/// </summary>
internal sealed class ApplyLockHandler(IAttestationVerifier verifier, IValidationFindings? findings = null) : ICommandHandler<ApplyLock, LockState>
{
    public async Task<CommandResult<LockState>> HandleAsync(ApplyLock command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (!LockScopes.IsScope(command.Scope) || !System.Enum.IsDefined(command.Kind))
        {
            return AssertionErrors.ScopeInvalid;
        }

        if (await context.Work.ExistsAsync<Assertion>(command.AssertionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var (found, enrollment) = await ScopeEnrollmentAsync(command.Scope, command.StudyId, context.Work, cancellationToken).ConfigureAwait(false);
        if (!found)
        {
            return AssertionErrors.ScopeUnknown;
        }

        await context.Work.LockScopeAsync(command.Scope, cancellationToken).ConfigureAwait(false);
        var held = await LocksInForce.ReadAsync(context.Work, [command.Scope], cancellationToken).ConfigureAwait(false);
        if (held.Any(l => l.Kind == (AssertionKind)command.Kind))
        {
            return AssertionErrors.AlreadyLocked;
        }

        if (command.Kind == LockKind.Locked
            && await LockReadinessEvaluator.CheckAsync(
                context.TenantId,
                context.Work,
                command.StudyId,
                command.Scope,
                findings,
                cancellationToken).ConfigureAwait(false) is { } notReady)
        {
            return notReady;
        }

        var target = LockStatement.Target(command.AssertionId, LockStatement.Apply(command.AssertionId, command.Kind, command.Scope));
        if (await LockSignature.RecordOrVerifyAsync(
                verifier,
                context,
                command.AttestationId,
                target,
                command.Meaning,
                command.AuthenticatedAt,
                command.Method,
                command.ReasonForChange,
                cancellationToken).ConfigureAwait(false) is { } rejected)
        {
            return rejected;
        }

        var scopeVersion = await AssertionScopes.VersionAsync(command.Scope, context.Work, cancellationToken).ConfigureAwait(false);
        var applied = Assertion.Lock(
            command.AssertionId, context.TenantId, command.Kind, command.StudyId, enrollment, command.Scope, command.AttestationId,
            context.Actor.Id, context.RecordedAt);
        context.Work.Add(applied);
        if (scopeVersion is not null)
        {
            context.Work.LinkInput(scopeVersion, LineageEdgeKind.DefinedBy, target: NodeRef.For(applied.Id));
        }

        context.Work.Raise(new LockApplied(context.TenantId, applied.Id, command.Kind, command.StudyId, command.Scope, command.AttestationId));
        return CommandResult<LockState>.Success(new LockState(
            applied.Id, command.Kind, command.Scope, command.AttestationId, Released: false, applied.AssertedAt, applied.AssertedBy));
    }

    /// <summary>
    /// Whether the scope exists in <paramref name="study"/>, and its enrollment: null for a study or study site.
    /// </summary>
    private static async Task<(bool Found, EnrollmentId? Enrollment)> ScopeEnrollmentAsync(
        NodeRef scope, StudyId study, IKernelUnitOfWork work, CancellationToken cancellationToken)
    {
        switch (scope.Kind)
        {
            case NodeKinds.Study:
                return (StudyId.FromGuid(scope.Id) == study, null);
            case NodeKinds.StudySite:
                var site = await work.ReadAsync<StudySite>(StudySiteId.FromGuid(scope.Id), cancellationToken).ConfigureAwait(false);
                return (site?.StudyId == study, null);
            case NodeKinds.Enrollment:
                var enrollment = await work.ReadAsync<Enrollment>(EnrollmentId.FromGuid(scope.Id), cancellationToken).ConfigureAwait(false);
                return (enrollment?.StudyId == study, enrollment?.Id);
            case NodeKinds.CollectionInstance:
                var instance = await work.ReadAsync<CollectionInstance>(CollectionInstanceId.FromGuid(scope.Id), cancellationToken).ConfigureAwait(false);
                return (instance?.StudyId == study, instance?.EnrollmentId);
            default:
                var observation = await work.ReadAsync<Observation>(ObservationId.FromGuid(scope.Id), cancellationToken).ConfigureAwait(false);
                return (observation?.StudyId == study, observation?.EnrollmentId);
        }
    }
}

/// <summary>
/// Handles <see cref="ReleaseLock"/>: under the scope's exclusive advisory lock it refuses a lock already released,
/// checks the signature over <see cref="LockStatement.Release"/> and records the retracting assertion.
/// </summary>
internal sealed class ReleaseLockHandler(IAttestationVerifier verifier) : ICommandHandler<ReleaseLock, LockState>
{
    public async Task<CommandResult<LockState>> HandleAsync(ReleaseLock command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Assertion>(command.AssertionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<Assertion>(command.LockId, cancellationToken).ConfigureAwait(false) is not { IsLock: true, RetractsId: null } applied
            || applied.StudyId != command.StudyId)
        {
            return KernelErrors.NotFound;
        }

        await context.Work.LockScopeAsync(applied.Target, cancellationToken).ConfigureAwait(false);
        if ((await LocksInForce.RetractedAsync(context.Work, [applied.Id], cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return AssertionErrors.AlreadyRetracted;
        }

        var target = LockStatement.Target(command.AssertionId, LockStatement.Release(command.AssertionId, applied.Id));
        if (await LockSignature.RecordOrVerifyAsync(
                verifier,
                context,
                command.AttestationId,
                target,
                command.Meaning,
                command.AuthenticatedAt,
                command.Method,
                command.ReasonForChange,
                cancellationToken).ConfigureAwait(false) is { } rejected)
        {
            return rejected;
        }

        var scopeVersion = await AssertionScopes.VersionAsync(applied.Target, context.Work, cancellationToken).ConfigureAwait(false);
        var release = Assertion.Retract(command.AssertionId, applied, command.AttestationId, context.Actor.Id, context.RecordedAt);
        var kind = (LockKind)applied.Kind;
        var releaseTarget = NodeRef.For(release.Id);
        context.Work.Add(release);
        context.Work.LinkInput(NodeRef.For(applied.Id, Math.Max(1L, applied.VersionIndex)), LineageEdgeKind.DerivedFrom, target: releaseTarget);
        if (scopeVersion is not null)
        {
            context.Work.LinkInput(scopeVersion, LineageEdgeKind.DefinedBy, target: releaseTarget);
        }

        context.Work.Raise(new LockReleased(context.TenantId, release.Id, applied.Id, kind, applied.StudyId, applied.Target, command.AttestationId));
        return CommandResult<LockState>.Success(new LockState(
            applied.Id, kind, applied.Target, command.AttestationId, Released: true, release.AssertedAt, release.AssertedBy));
    }
}

/// <summary>
/// Handles <see cref="VerifyObservation"/>: under an exclusive advisory lock on the observation and shared locks on its
/// covering scopes, refuses when a <see cref="AssertionKind.Locked"/> lock holds, when the revision is stale, when an
/// open query holds on the observation, or when the revision already holds an active verification (ADR-2621).
/// </summary>
internal sealed class VerifyObservationHandler : ICommandHandler<VerifyObservation, AssertionState>
{
    public async Task<CommandResult<AssertionState>> HandleAsync(VerifyObservation command, ICommandContext context, CancellationToken cancellationToken)
    {
        var (observation, refused) = await ObservationAssertions.OpenAsync(
            context, command.AssertionId, command.EnrollmentId, command.ObservationId, cancellationToken, lockObservation: true).ConfigureAwait(false);
        if (observation is null)
        {
            return refused!;
        }

        if (command.Revision != observation.Revision)
        {
            return AssertionErrors.RevisionStale.WithMessage(
                $"The observation's current revision is {observation.Revision}, not {command.Revision}; reload it and verify the current value.");
        }

        var assertions = await context.Work.ReadAllAsync<Assertion>(
            a => a.TargetKind == NodeKinds.Observation && a.TargetId == observation.Id.Value,
            cancellationToken).ConfigureAwait(false);
        var retractedIds = assertions
            .Where(a => a.RetractsId is not null)
            .Select(a => a.RetractsId!.Value)
            .ToHashSet();

        if (assertions.Any(a => a.Kind == AssertionKind.Queried && a.RetractsId is null && !retractedIds.Contains(a.Id)))
        {
            return AssertionErrors.QueryOpen;
        }

        if (assertions.Any(a => a.Kind == AssertionKind.Verified && a.RetractsId is null && a.TargetRevision == command.Revision && !retractedIds.Contains(a.Id)))
        {
            return AssertionErrors.AlreadyVerified;
        }

        var verified = Assertion.Verify(command.AssertionId, observation, command.Revision, context.Actor.Id, context.RecordedAt);
        context.Work.LinkInput(NodeRef.For(observation.Id, command.Revision), LineageEdgeKind.DerivedFrom, target: NodeRef.For(verified.Id));
        return ObservationAssertions.Made(context, verified);
    }
}

/// <summary>Handles <see cref="RaiseQuery"/> the way <see cref="VerifyObservationHandler"/> checks its observation.</summary>
internal sealed class RaiseQueryHandler : ICommandHandler<RaiseQuery, AssertionState>
{
    public async Task<CommandResult<AssertionState>> HandleAsync(RaiseQuery command, ICommandContext context, CancellationToken cancellationToken)
    {
        var (observation, refused) = await ObservationAssertions.OpenAsync(
            context, command.AssertionId, command.EnrollmentId, command.ObservationId, cancellationToken, lockObservation: true).ConfigureAwait(false);
        if (observation is null)
        {
            return refused!;
        }

        var assertedBy = ObservationAssertions.ResolveAssertedBy(context, command.AssertedBy);
        var query = Assertion.Query(command.AssertionId, observation, command.Text, assertedBy, context.RecordedAt, command.Revision);
        if (query.Value is not { } raised)
        {
            return query.Error!;
        }

        context.Work.LinkInput(NodeRef.For(observation.Id, raised.TargetRevision!.Value), LineageEdgeKind.DerivedFrom, target: NodeRef.For(raised.Id));
        return ObservationAssertions.Made(context, raised);
    }
}

/// <summary>Handles <see cref="AnswerQuery"/>: the query must be open, and its observation not locked.</summary>
internal sealed class AnswerQueryHandler : ICommandHandler<AnswerQuery, AssertionState>
{
    public async Task<CommandResult<AssertionState>> HandleAsync(AnswerQuery command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ReadAsync<Assertion>(command.QueryId, cancellationToken).ConfigureAwait(false) is not { } query
            || query.EnrollmentId != command.EnrollmentId)
        {
            return KernelErrors.NotFound;
        }

        if (query.Kind != AssertionKind.Queried || query.RetractsId is not null)
        {
            return AssertionErrors.NotAQuery;
        }

        var (observation, refused) = await ObservationAssertions.OpenAsync(
            context, command.AssertionId, command.EnrollmentId, ObservationId.FromGuid(query.TargetId), cancellationToken).ConfigureAwait(false);
        if (refused is not null)
        {
            return refused;
        }

        if (command.Revision is { } rev && (rev < 1 || rev > observation!.Revision))
        {
            return AssertionErrors.RevisionStale;
        }

        if ((await LocksInForce.RetractedAsync(context.Work, [query.Id], cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return AssertionErrors.AlreadyRetracted.WithMessage("The query is closed; raise a new one.");
        }

        var assertedBy = ObservationAssertions.ResolveAssertedBy(context, command.AssertedBy);
        var answer = Assertion.Answer(command.AssertionId, query, command.Text, assertedBy, context.RecordedAt, command.Revision);
        if (answer.Value is not { } answered)
        {
            return answer.Error!;
        }

        var answerTarget = NodeRef.For(answered.Id);
        context.Work.LinkInput(NodeRef.For(query.Id, Math.Max(1L, query.VersionIndex)), LineageEdgeKind.DerivedFrom, target: answerTarget);
        context.Work.LinkInput(NodeRef.For(observation!.Id, answered.TargetRevision ?? observation.Revision), LineageEdgeKind.DerivedFrom, target: answerTarget);
        return ObservationAssertions.Made(context, answered);
    }
}

/// <summary>
/// Handles <see cref="RetractAssertion"/>: withdraws a verification or an answer, or closes a query, once. Locks are
/// released with <see cref="ReleaseLock"/>, which needs a signature.
/// </summary>
internal sealed class RetractAssertionHandler : ICommandHandler<RetractAssertion, AssertionState>
{
    public async Task<CommandResult<AssertionState>> HandleAsync(RetractAssertion command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ReadAsync<Assertion>(command.RetractedId, cancellationToken).ConfigureAwait(false) is not { } retracted
            || retracted.EnrollmentId != command.EnrollmentId)
        {
            return KernelErrors.NotFound;
        }

        if (retracted.IsLock || retracted.RetractsId is not null)
        {
            return AssertionErrors.NotRetractable;
        }

        // An answer is about its query; the query and a verification are about the observation.
        var observationId = ObservationId.FromGuid(retracted.TargetId);
        if (retracted.Kind == AssertionKind.QueryAnswered)
        {
            if (await context.Work.ReadAsync<Assertion>(AssertionId.FromGuid(retracted.TargetId), cancellationToken).ConfigureAwait(false) is not { } query)
            {
                return KernelErrors.NotFound;
            }

            observationId = ObservationId.FromGuid(query.TargetId);
        }

        var (observation, refused) = await ObservationAssertions.OpenAsync(
            context, command.AssertionId, command.EnrollmentId, observationId, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
        {
            return refused;
        }

        if ((await LocksInForce.RetractedAsync(context.Work, [retracted.Id], cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return AssertionErrors.AlreadyRetracted;
        }

        var assertedBy = ObservationAssertions.ResolveAssertedBy(context, command.AssertedBy);
        var retraction = Assertion.Retract(command.AssertionId, retracted, attestation: null, assertedBy, context.RecordedAt);
        var retractionTarget = NodeRef.For(retraction.Id);
        context.Work.Add(retraction);
        context.Work.LinkInput(NodeRef.For(retracted.Id, Math.Max(1L, retracted.VersionIndex)), LineageEdgeKind.DerivedFrom, target: retractionTarget);
        context.Work.LinkInput(NodeRef.For(observation!.Id, retracted.TargetRevision ?? observation.Revision), LineageEdgeKind.DerivedFrom, target: retractionTarget);
        context.Work.Raise(new AssertionRetracted(
            context.TenantId, retraction.Id, retracted.Id, retracted.Kind, retracted.StudyId, command.EnrollmentId));
        return ObservationAssertions.StateOf(retraction);
    }
}

/// <summary>Resolves the current versioned <see cref="NodeRef"/> of a lock scope.</summary>
internal static class AssertionScopes
{
    public static async Task<NodeRef?> VersionAsync(NodeRef scope, IKernelUnitOfWork work, CancellationToken cancellationToken)
    {
        switch (scope.Kind)
        {
            case NodeKinds.Study:
                var study = await work.ReadAsync<Study>(StudyId.FromGuid(scope.Id), cancellationToken).ConfigureAwait(false);
                return scope.AtVersion(Math.Max(1L, study?.VersionIndex ?? 1L));
            case NodeKinds.StudySite:
                var site = await work.ReadAsync<StudySite>(StudySiteId.FromGuid(scope.Id), cancellationToken).ConfigureAwait(false);
                return site is null ? null : scope.AtVersion(Math.Max(1L, site.VersionIndex));
            case NodeKinds.Enrollment:
                var enrollment = await work.ReadAsync<Enrollment>(EnrollmentId.FromGuid(scope.Id), cancellationToken).ConfigureAwait(false);
                return enrollment is null ? null : scope.AtVersion(Math.Max(1L, enrollment.VersionIndex));
            case NodeKinds.CollectionInstance:
                var instance = await work.ReadAsync<CollectionInstance>(CollectionInstanceId.FromGuid(scope.Id), cancellationToken).ConfigureAwait(false);
                return instance is null ? null : scope.AtVersion(Math.Max(1L, instance.VersionIndex));
            default:
                var observation = await work.ReadAsync<Observation>(ObservationId.FromGuid(scope.Id), cancellationToken).ConfigureAwait(false);
                return observation is null ? null : scope.AtVersion(Math.Max(1L, observation.VersionIndex));
        }
    }
}

/// <summary>Shared steps of the handlers that assert something about an observation.</summary>
internal static class ObservationAssertions
{
    /// <summary>
    /// Checks that <paramref name="assertion"/> is new, then, under shared locks on the observation's covering scopes,
    /// that no <see cref="AssertionKind.Locked"/> lock holds and that the observation belongs to the enrollment.
    /// Returns the observation, or the error that refuses the command.
    /// </summary>
    public static async Task<(Observation? Observation, KernelError? Refused)> OpenAsync(
        ICommandContext context,
        AssertionId assertion,
        EnrollmentId enrollmentId,
        ObservationId observationId,
        CancellationToken cancellationToken,
        bool lockObservation = false)
    {
        if (await context.Work.ExistsAsync<Assertion>(assertion, cancellationToken).ConfigureAwait(false))
        {
            return (null, KernelErrors.AlreadyExists);
        }

        if (await context.Work.ReadAsync<Enrollment>(enrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return (null, KernelErrors.NotFound);
        }

        if (lockObservation)
        {
            await context.Work.LockAsync<Observation>(observationId, cancellationToken).ConfigureAwait(false);
        }

        var instance = await CollectionMembership.InstanceOfAsync(context.Work, observationId, cancellationToken).ConfigureAwait(false);
        if (await LocksInForce.AnyAsync(
            context.Work,
            LockScopes.Covering(enrollment.StudyId, enrollment.StudySiteId, enrollment.Id, observationId, instance),
            LocksInForce.DenyingAssertions,
            cancellationToken).ConfigureAwait(false))
        {
            return (null, AssertionErrors.Locked);
        }

        return await context.Work.ReadAsync<Observation>(observationId, cancellationToken).ConfigureAwait(false) is { } observation
            && observation.EnrollmentId == enrollmentId
            ? (observation, null)
            : (null, KernelErrors.NotFound);
    }

    /// <summary>Adds <paramref name="assertion"/> and raises <see cref="AssertionMade"/>.</summary>
    public static CommandResult<AssertionState> Made(ICommandContext context, Assertion assertion)
    {
        context.Work.Add(assertion);
        context.Work.Raise(new AssertionMade(
            assertion.TenantId, assertion.Id, assertion.Kind, assertion.StudyId, assertion.EnrollmentId!.Value, assertion.Target, assertion.TargetRevision));
        return StateOf(assertion);
    }

    public static CommandResult<AssertionState> StateOf(Assertion assertion) =>
        CommandResult<AssertionState>.Success(new AssertionState(
            assertion.Id, assertion.Kind, assertion.Target, assertion.TargetRevision, assertion.RetractsId, assertion.AssertedAt, assertion.AssertedBy));

    public static string ResolveAssertedBy(ICommandContext context, string? forwardedActorId) =>
        context.Actor.Kind == ActorKind.ServiceAccount && !string.IsNullOrWhiteSpace(forwardedActorId)
            ? forwardedActorId.Trim()
            : context.Actor.Id;
}

/// <summary>Records or checks a lock's or release's signature: meaning Approved or Responsibility, over the lock statement.</summary>
internal static class LockSignature
{
    public static async Task<KernelError?> RecordOrVerifyAsync(
        IAttestationVerifier verifier,
        ICommandContext context,
        AttestationId attestationId,
        AttestationTarget target,
        AttestationMeaning? meaning,
        DateTimeOffset? authenticatedAt,
        string? method,
        string reasonForChange,
        CancellationToken cancellationToken)
    {
        if (meaning is null && authenticatedAt is null && method is null)
        {
            return await CheckAsync(verifier, context, attestationId, target, cancellationToken).ConfigureAwait(false);
        }

        if (meaning is not (AttestationMeaning.Approved or AttestationMeaning.Responsibility))
        {
            return AssertionErrors.AttestationRejected.WithMessage("Lock signatures require meaning Approved or Responsibility.");
        }

        if (await context.Work.ExistsAsync<Attestation>(attestationId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var signed = Attestation.Sign(
            attestationId,
            context.TenantId,
            context.Actor,
            meaning.Value,
            target,
            authenticatedAt ?? default,
            method ?? string.Empty,
            context.RecordedAt,
            reasonForChange);
        if (signed.Value is not { } attestation)
        {
            return signed.Error!;
        }

        context.Work.Add(attestation);
        context.Work.Raise(new AttestationRecorded(attestation.TenantId, attestation.Id, attestation.ActorId, attestation.Meaning, attestation.Target));
        return null;
    }

    public static async Task<KernelError?> CheckAsync(
        IAttestationVerifier verifier, ICommandContext context, AttestationId attestation, AttestationTarget target, CancellationToken cancellationToken)
    {
        var approved = await verifier.VerifyAsync(
            new AttestationRequirement(context.TenantId, attestation, AttestationMeaning.Approved, target, context.Actor), cancellationToken).ConfigureAwait(false);
        if (approved.Satisfied)
        {
            return null;
        }

        var responsible = await verifier.VerifyAsync(
            new AttestationRequirement(context.TenantId, attestation, AttestationMeaning.Responsibility, target, context.Actor), cancellationToken).ConfigureAwait(false);
        return responsible.Satisfied
            ? null
            : AssertionErrors.AttestationRejected.WithMessage(approved.Reason ?? AssertionErrors.AttestationRejected.Message);
    }
}

/// <summary>
/// Lets <see cref="Attest"/> sign a lock statement before it is recorded (spec §10.5): the target is the Assertion that
/// will record it, which must not exist yet; the lock command recomputes the statement's hash from its own inputs.
/// </summary>
internal sealed class LockStatementAttestationCheck : IAttestationTargetCheck
{
    public string Kind => NodeKinds.Assertion;

    public async Task<KernelError?> CheckAsync(AttestationTarget target, ICommandContext context, CancellationToken cancellationToken) =>
        await context.Work.ExistsAsync<Assertion>(AssertionId.FromGuid(target.Id), cancellationToken).ConfigureAwait(false)
            ? TrustErrors.TargetNotSignable.WithMessage("This assertion is already recorded; sign a lock statement before recording it.")
            : null;
}

/// <summary>Registers the assertion and lock command handlers (spec §8.2, §10.5).</summary>
public static class AssertionServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelAssertionHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<ApplyLock, LockState>, ApplyLockHandler>();
        services.AddTransient<ICommandHandler<ReleaseLock, LockState>, ReleaseLockHandler>();
        services.AddTransient<ICommandHandler<VerifyObservation, AssertionState>, VerifyObservationHandler>();
        services.AddTransient<ICommandHandler<RaiseQuery, AssertionState>, RaiseQueryHandler>();
        services.AddTransient<ICommandHandler<AnswerQuery, AssertionState>, AnswerQueryHandler>();
        services.AddTransient<ICommandHandler<RetractAssertion, AssertionState>, RetractAssertionHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IAttestationTargetCheck, LockStatementAttestationCheck>());
        return services;
    }
}
