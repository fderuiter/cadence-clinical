using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Handles <see cref="RaiseFormQuery"/>: the value must be one of the form's, at its current revision, and no Locked
/// lock may cover it. Links the new derived <see cref="FormQuery"/> aggregate to its governing enrollment, form instance,
/// and target observation revision (<c>K-LIN-1</c>).
/// </summary>
internal sealed class RaiseFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<RaiseFormQuery, FormQueryState>
{
    public async Task<CommandResult<FormQueryState>> HandleAsync(RaiseFormQuery command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<FormQuery>(command.QueryId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var target = await FormQueryScopes.ShareAsync(
            records,
            context,
            command.FormId,
            command.ObservationId,
            cancellationToken,
            command.ResolvedHeader).ConfigureAwait(false);
        if (target is null || target.EnrollmentId != command.EnrollmentId)
        {
            return FormErrors.CollectionInstanceUnknown;
        }

        if (target.ObservationId != command.ObservationId || target.ItemId is null || target.Revision is not { } currentRevision)
        {
            return FormQueryErrors.ObservationUnknown;
        }

        if (currentRevision != command.Revision)
        {
            return FormQueryErrors.RevisionStale.WithMessage(
                $"The value's current revision is {currentRevision}, not {command.Revision}; reload the form and query the current value.");
        }

        if (target.IsLocked)
        {
            return AssertionErrors.Locked;
        }

        var (query, step) = FormQuery.Raise(
            command.QueryId,
            context.TenantId,
            command.FormId,
            command.EnrollmentId,
            target.ItemId,
            command.ObservationId,
            currentRevision,
            command.Text,
            AssertionId.New(),
            context.Actor.Id,
            context.RecordedAt,
            command.ReasonForChange,
            command.Origin,
            command.OwnerRole,
            command.Priority,
            command.Assignee);
        context.Work.Add(query);
        context.Work.LinkInput(NodeRef.For(target.EnrollmentId, Math.Max(1L, target.EnrollmentVersion)), LineageEdgeKind.GovernedBy);
        context.Work.LinkInput(NodeRef.For(target.CollectionInstanceId, Math.Max(1L, target.Version)), LineageEdgeKind.CollectedBy);
        context.Work.LinkInput(NodeRef.For(command.ObservationId, currentRevision), LineageEdgeKind.DerivedFrom);
        return FormQueryScopes.Changed(
            context,
            query,
            [step],
            version: 1,
            revision: currentRevision,
            actorId: context.Actor.Id,
            reasonForChange: command.ReasonForChange,
            scope: FormQueryScopes.ToScopeContext(target, currentRevision));
    }
}

/// <summary>Handles <see cref="AnswerFormQuery"/>.</summary>
internal sealed class AnswerFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<AnswerFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(AnswerFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records,
            context,
            command.QueryId,
            command.EnrollmentId,
            (query, target) =>
            {
                var step = query.Answer(command.Text, AssertionId.New(), context.Actor.Id, context.RecordedAt, command.ReasonForChange, command.Assignee);
                if (step is null)
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                int revision = target?.Revision ?? query.Revision;
                return FormQueryScopes.StepAllowed([step], revision, context.Actor.Id, command.ReasonForChange);
            },
            cancellationToken);
}

/// <summary>
/// Handles <see cref="AutoAnswerFormQuery"/> when a queried observation is edited by a user.
/// </summary>
internal sealed class AutoAnswerFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<AutoAnswerFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(AutoAnswerFormQuery command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (context.Actor.Kind != ActorKind.ServiceAccount)
        {
            return Task.FromResult<CommandResult<FormQueryState>>(KernelErrors.AuthorizationDenied);
        }

        return FormQueryScopes.StepAsync(
            records,
            context,
            command.QueryId,
            command.EnrollmentId,
            (query, _) =>
            {
                if (query.Origin != FormQueryOrigin.Manual || query.ObservationId != command.ObservationId)
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                var autoText = $"Value updated to revision {command.NewRevision}.";
                var step = query.Answer(autoText, AssertionId.New(), command.AnsweredBy, context.RecordedAt, command.ReasonForChange);
                if (step is null)
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                context.Work.LinkInput(NodeRef.For(command.ObservationId, command.NewRevision), LineageEdgeKind.DerivedFrom);
                return FormQueryScopes.StepAllowed([step], command.NewRevision, command.AnsweredBy, command.ReasonForChange);
            },
            cancellationToken);
    }
}

/// <summary>Handles <see cref="ReopenFormQuery"/>.</summary>
internal sealed class ReopenFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<ReopenFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(ReopenFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records,
            context,
            command.QueryId,
            command.EnrollmentId,
            (query, target) =>
            {
                var step = query.Reopen(command.Text, AssertionId.New(), context.Actor.Id, context.RecordedAt, command.ReasonForChange, command.Assignee);
                if (step is null)
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                int revision = target?.Revision ?? query.Revision;
                return FormQueryScopes.StepAllowed([step], revision, context.Actor.Id, command.ReasonForChange);
            },
            cancellationToken);
}

/// <summary>Handles <see cref="ReassignFormQuery"/>.</summary>
internal sealed class ReassignFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<ReassignFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(ReassignFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records,
            context,
            command.QueryId,
            command.EnrollmentId,
            (query, target) =>
            {
                if (!query.Reassign(command.Assignee, command.OwnerRole, context.Actor.Id, context.RecordedAt, command.ReasonForChange, command.Text))
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                int revision = target?.Revision ?? query.Revision;
                return FormQueryScopes.StepAllowed([], revision, context.Actor.Id, command.ReasonForChange);
            },
            cancellationToken);
}

/// <summary>Handles <see cref="CloseFormQuery"/>, enforcing manual query separation of duties.</summary>
internal sealed class CloseFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<CloseFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(CloseFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records,
            context,
            command.QueryId,
            command.EnrollmentId,
            (query, target) =>
            {
                var note = string.IsNullOrWhiteSpace(command.Text) ? command.ReasonForChange : command.Text;
                var outcome = query.Close(note, AssertionId.New(), context.Actor.Id, context.RecordedAt, command.ReasonForChange);
                if (outcome.Step is null)
                {
                    if (outcome.Denial == FormQueryCloseDenial.SeparationOfDuties)
                    {
                        return (null, FormQueryErrors.SeparationOfDuties);
                    }

                    if (query.Origin == FormQueryOrigin.Manual && query.Status == FormQueryStatus.Open)
                    {
                        return (null, FormQueryErrors.InvalidTransition.WithMessage("An open manual query must be answered before it can be closed; cancel it if raised in error."));
                    }

                    return (null, FormQueryScopes.InvalidTransitionError(query.Status));
                }

                int revision = target?.Revision ?? query.Revision;
                return FormQueryScopes.StepAllowed([outcome.Step], revision, context.Actor.Id, command.ReasonForChange);
            },
            cancellationToken);
}

/// <summary>Handles <see cref="CancelFormQuery"/>.</summary>
internal sealed class CancelFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<CancelFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(CancelFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records,
            context,
            command.QueryId,
            command.EnrollmentId,
            (query, target) =>
            {
                var note = string.IsNullOrWhiteSpace(command.Text) ? command.ReasonForChange : command.Text;
                var step = query.Cancel(note, AssertionId.New(), context.Actor.Id, context.RecordedAt, command.ReasonForChange);
                if (step is null)
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                int revision = target?.Revision ?? query.Revision;
                return FormQueryScopes.StepAllowed([step], revision, context.Actor.Id, command.ReasonForChange);
            },
            cancellationToken);
}

/// <summary>Handles <see cref="EscalateFormQuery"/> when an open query passes its 14-day SLA on <c>IClock</c>.</summary>
internal sealed class EscalateFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<EscalateFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(EscalateFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records,
            context,
            command.QueryId,
            command.EnrollmentId,
            (query, target) =>
            {
                if (!query.Escalate(context.Actor.Id, context.RecordedAt, command.ReasonForChange))
                {
                    return (null, FormQueryErrors.InvalidTransition.WithMessage("Only an unescalated open query past its 14-day due date can be escalated."));
                }

                int revision = target?.Revision ?? query.Revision;
                return FormQueryScopes.StepAllowed([], revision, context.Actor.Id, command.ReasonForChange);
            },
            cancellationToken);
}

/// <summary>Handles <see cref="GenerateFormQueryAgingDigest"/>.</summary>
internal sealed class GenerateFormQueryAgingDigestHandler : ICommandHandler<GenerateFormQueryAgingDigest, FormQueryAgingDigestState>
{
    public async Task<CommandResult<FormQueryAgingDigestState>> HandleAsync(
        GenerateFormQueryAgingDigest command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<FormQueryAgingDigest>(command.DigestId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var scopeKey = command.StudySiteId is { } site ? $"site:{site.Value:D}" : "study";
        var existing = await context.Work.ReadAllAsync<FormQueryAgingDigest>(
            d => d.StudyId == command.StudyId && d.DigestDate == command.DigestDate && d.ScopeKey == scopeKey,
            cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            var found = existing[0];
            return CommandResult<FormQueryAgingDigestState>.Success(new FormQueryAgingDigestState(
                found.Id,
                found.StudyId,
                found.StudySiteId,
                found.ScopeKey,
                found.RecipientRole,
                found.DigestDate,
                found.GeneratedAt,
                found.StudyCode,
                found.SiteCode,
                found.EscalatedCount,
                [.. found.Entries]));
        }

        var digest = FormQueryAgingDigest.Create(
            command.DigestId,
            context.TenantId,
            command.StudyId,
            command.StudySiteId,
            command.RecipientRole,
            command.DigestDate,
            context.RecordedAt,
            command.StudyCode,
            command.SiteCode,
            command.Entries);
        context.Work.Add(digest);
        context.Work.Raise(new FormQueryAgingDigestGenerated(
            context.TenantId,
            digest.Id,
            digest.StudyId,
            digest.StudySiteId,
            digest.ScopeKey,
            digest.RecipientRole,
            digest.DigestDate,
            digest.GeneratedAt,
            digest.StudyCode,
            digest.SiteCode,
            digest.EscalatedCount));
        return CommandResult<FormQueryAgingDigestState>.Success(new FormQueryAgingDigestState(
            digest.Id,
            digest.StudyId,
            digest.StudySiteId,
            digest.ScopeKey,
            digest.RecipientRole,
            digest.DigestDate,
            digest.GeneratedAt,
            digest.StudyCode,
            digest.SiteCode,
            digest.EscalatedCount,
            [.. digest.Entries]));
    }
}
