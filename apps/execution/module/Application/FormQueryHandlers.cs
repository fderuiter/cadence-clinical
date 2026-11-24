using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Domain;

using FluentValidation;

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

/// <summary>The steps every query command shares.</summary>
internal static class FormQueryScopes
{
    internal sealed record StepDecision(
        System.Collections.Generic.IReadOnlyList<KernelAssertionStep> Steps,
        int Revision,
        string ActorId,
        string ReasonForChange);

    /// <summary>
    /// Takes the shared locks on the scopes covering <paramref name="observation"/> of <paramref name="form"/>, widest
    /// first (ADR-2621), and checks the target observation revision and active <c>Locked</c> locks under them.
    /// </summary>
    public static async Task<CollectionQueryTarget?> ShareAsync(
        ICollectionInstanceRecords records,
        ICommandContext context,
        CollectionInstanceId form,
        ObservationId observation,
        CancellationToken cancellationToken,
        CollectionInstanceHeader? header = null)
    {
        var resolvedHeader = header ?? await records.FindHeaderAsync(context.TenantId, form, cancellationToken).ConfigureAwait(false);
        if (resolvedHeader is null)
        {
            return null;
        }

        await context.Work.ShareScopesAsync(
            LockScopes.Covering(resolvedHeader.StudyId, resolvedHeader.StudySiteId, resolvedHeader.EnrollmentId, observation, form),
            cancellationToken).ConfigureAwait(false);
        return await records.FindQueryTargetAsync(
            context.TenantId,
            form,
            observation: observation,
            checkLocks: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True when a Locked lock covers <paramref name="observation"/>: one on the form or a wider scope, or on the value itself.</summary>
    public static bool IsLocked(CollectionInstanceRecord record, ObservationId observation) =>
        IsLocked(record.Locks, observation);

    /// <summary>True when a Locked lock covers <paramref name="observation"/>: one on the form or a wider scope, or on the value itself.</summary>
    public static bool IsLocked(CollectionInstanceScopeState state, ObservationId observation) =>
        IsLocked(state.Locks, observation);

    private static bool IsLocked(IReadOnlyList<LockView> locks, ObservationId observation) =>
        locks.Any(l => l.Kind == LockKind.Locked && (l.Scope.Kind != NodeKinds.Observation || l.Scope.Id == observation.Value));

    /// <summary>
    /// Loads the query for update, checks it belongs to <paramref name="enrollment"/> and that no Locked lock covers its
    /// value, and applies <paramref name="step"/>.
    /// </summary>
    public static async Task<CommandResult<FormQueryState>> StepAsync(
        ICollectionInstanceRecords records,
        ICommandContext context,
        FormQueryId queryId,
        EnrollmentId enrollment,
        Func<FormQuery, CollectionQueryTarget?, (StepDecision? Allowed, KernelError? Error)> step,
        CancellationToken cancellationToken)
    {
        await context.Work.LockAsync<FormQuery>(queryId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.FindAsync<FormQuery>(queryId, cancellationToken).ConfigureAwait(false) is not { } query
            || query.EnrollmentId != enrollment)
        {
            return KernelErrors.NotFound;
        }

        var target = await ShareAsync(records, context, query.FormId, query.ObservationId, cancellationToken).ConfigureAwait(false);
        if (target is { IsLocked: true })
        {
            return AssertionErrors.Locked;
        }

        var (allowed, error) = step(query, target);
        if (allowed is null)
        {
            return error ?? InvalidTransitionError(query.Status);
        }

        FormQueryScopeContext? scope = null;
        if (target is not null)
        {
            context.Work.LinkInput(NodeRef.For(target.EnrollmentId, Math.Max(1L, target.EnrollmentVersion)), LineageEdgeKind.GovernedBy);
            scope = ToScopeContext(target, target.Revision);
        }

        return Changed(
            context,
            query,
            allowed.Steps,
            query.VersionIndex + 1,
            allowed.Revision,
            allowed.ActorId,
            allowed.ReasonForChange,
            scope);
    }

    internal static FormQueryScopeContext ToScopeContext(CollectionQueryTarget target, int? currentRevision) =>
        new(target.StudyId, target.StudySiteId, target.CollectionSpecId, target.PlannedActivityId, currentRevision);

    internal static FormQueryScopeContext ToScopeContext(CollectionInstanceScopeState state, int? currentRevision) =>
        new(state.StudyId, state.StudySiteId, state.CollectionSpecId, state.PlannedActivityId, currentRevision);

    public static (StepDecision? Allowed, KernelError? Error) StepAllowed(
        System.Collections.Generic.IReadOnlyList<KernelAssertionStep> steps,
        int revision,
        string actorId,
        string reasonForChange) =>
        (new StepDecision(steps, revision, actorId, reasonForChange), null);

    public static (StepDecision? Allowed, KernelError? Error) DeniedTransition(FormQueryStatus status) =>
        (null, InvalidTransitionError(status));

    public static KernelError InvalidTransitionError(FormQueryStatus status) =>
        FormQueryErrors.InvalidTransition.WithMessage($"A query that is {status.ToString().ToLowerInvariant()} does not allow that step.");

    /// <summary>Raises <see cref="FormQueryChanged"/> and returns the query's new state.</summary>
    public static CommandResult<FormQueryState> Changed(
        ICommandContext context,
        FormQuery query,
        IReadOnlyList<KernelAssertionStep> steps,
        long version,
        int revision,
        string actorId,
        string reasonForChange,
        FormQueryScopeContext? scope = null)
    {
        context.Work.Raise(new FormQueryChanged(
            context.TenantId,
            query.Id,
            query.FormId,
            query.EnrollmentId,
            query.ObservationId,
            revision,
            query.Status,
            query.Origin,
            query.OwnerRole,
            query.Priority,
            actorId,
            reasonForChange,
            steps));
        return CommandResult<FormQueryState>.Success(FormQueryState.Of(query, version, scope));
    }
}

/// <summary>Checks the shape of <see cref="RaiseFormQuery"/>.</summary>
internal sealed class RaiseFormQueryValidator : AbstractValidator<RaiseFormQuery>
{
    public RaiseFormQueryValidator()
    {
        RuleFor(c => c.QueryId).Must(id => !id.IsEmpty).WithMessage("Name the query.");
        RuleFor(c => c.FormId).Must(id => !id.IsEmpty).WithMessage("Name the form.");
        RuleFor(c => c.EnrollmentId).Must(id => !id.IsEmpty).WithMessage("Name the form's enrollment.");
        RuleFor(c => c.ObservationId).Must(id => !id.IsEmpty).WithMessage("Name the value's observation.");
        RuleFor(c => c.Revision).GreaterThanOrEqualTo(1).WithMessage("Name the revision queried, from 1.");
        RuleFor(c => c.Text).Must(FormQuery.IsText).WithMessage($"Write the query, at most {AssertionText.MaxLength} characters.");
        RuleFor(c => c.Origin).IsInEnum().WithMessage("Choose a valid query origin.");
        RuleFor(c => c.OwnerRole).IsInEnum().WithMessage("Choose a valid owner role.");
        RuleFor(c => c.Priority).IsInEnum().WithMessage("Choose a valid priority.");
        RuleFor(c => c.Assignee).Must(a => a is null || a.Trim().Length <= FormQuery.AssigneeMaxLength)
            .WithMessage($"Assignee may be at most {FormQuery.AssigneeMaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="AnswerFormQuery"/>.</summary>
internal sealed class AnswerFormQueryValidator : AbstractValidator<AnswerFormQuery>
{
    public AnswerFormQueryValidator()
    {
        RuleFor(c => c.QueryId).Must(id => !id.IsEmpty).WithMessage("Name the query.");
        RuleFor(c => c.Text).Must(FormQuery.IsText).WithMessage($"Write the answer, at most {AssertionText.MaxLength} characters.");
        RuleFor(c => c.Assignee).Must(a => a is null || a.Trim().Length <= FormQuery.AssigneeMaxLength)
            .WithMessage($"Assignee may be at most {FormQuery.AssigneeMaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="AutoAnswerFormQuery"/>.</summary>
internal sealed class AutoAnswerFormQueryValidator : AbstractValidator<AutoAnswerFormQuery>
{
    public AutoAnswerFormQueryValidator()
    {
        RuleFor(c => c.QueryId).Must(id => !id.IsEmpty).WithMessage("Name the query.");
        RuleFor(c => c.EnrollmentId).Must(id => !id.IsEmpty).WithMessage("Name the enrollment.");
        RuleFor(c => c.ObservationId).Must(id => !id.IsEmpty).WithMessage("Name the observation.");
        RuleFor(c => c.NewRevision).GreaterThanOrEqualTo(2).WithMessage("Edited observation revision must be at least 2.");
        RuleFor(c => c.AnsweredBy).NotEmpty().WithMessage("Name the user who edited the queried value.");
    }
}

/// <summary>Checks the shape of <see cref="ReopenFormQuery"/>.</summary>
internal sealed class ReopenFormQueryValidator : AbstractValidator<ReopenFormQuery>
{
    public ReopenFormQueryValidator()
    {
        RuleFor(c => c.QueryId).Must(id => !id.IsEmpty).WithMessage("Name the query.");
        RuleFor(c => c.Text).Must(FormQuery.IsText).WithMessage($"Say why the query is reopened, at most {AssertionText.MaxLength} characters.");
        RuleFor(c => c.Assignee).Must(a => a is null || a.Trim().Length <= FormQuery.AssigneeMaxLength)
            .WithMessage($"Assignee may be at most {FormQuery.AssigneeMaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="ReassignFormQuery"/>.</summary>
internal sealed class ReassignFormQueryValidator : AbstractValidator<ReassignFormQuery>
{
    public ReassignFormQueryValidator()
    {
        RuleFor(c => c.QueryId).Must(id => !id.IsEmpty).WithMessage("Name the query.");
        RuleFor(c => c.EnrollmentId).Must(id => !id.IsEmpty).WithMessage("Name the enrollment.");
        RuleFor(c => c.ReasonForChange).Must(FormQuery.IsText)
            .WithMessage($"Say why the query is reassigned, at most {AssertionText.MaxLength} characters.");
        RuleFor(c => c.Assignee).Must(a => a is null || a.Trim().Length <= FormQuery.AssigneeMaxLength)
            .WithMessage($"Assignee may be at most {FormQuery.AssigneeMaxLength} characters.");
        RuleFor(c => c.OwnerRole).Must(r => r is null || Enum.IsDefined(r.Value))
            .WithMessage("Choose a valid owner role.");
        RuleFor(c => c.Text).Must(t => t is null || FormQuery.IsText(t))
            .WithMessage($"Reassignment note may be at most {AssertionText.MaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="CloseFormQuery"/>.</summary>
internal sealed class CloseFormQueryValidator : AbstractValidator<CloseFormQuery>
{
    public CloseFormQueryValidator()
    {
        RuleFor(c => c.QueryId).Must(id => !id.IsEmpty).WithMessage("Name the query.");
        RuleFor(c => c.ReasonForChange).Must(FormQuery.IsText).WithMessage($"Say why the query is closed, at most {AssertionText.MaxLength} characters.");
        RuleFor(c => c.Text).Must(t => t is null || FormQuery.IsText(t))
            .WithMessage($"Closing note may be at most {AssertionText.MaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="CancelFormQuery"/>.</summary>
internal sealed class CancelFormQueryValidator : AbstractValidator<CancelFormQuery>
{
    public CancelFormQueryValidator()
    {
        RuleFor(c => c.QueryId).Must(id => !id.IsEmpty).WithMessage("Name the query.");
        RuleFor(c => c.ReasonForChange).Must(FormQuery.IsText).WithMessage($"Say why the query is cancelled, at most {AssertionText.MaxLength} characters.");
        RuleFor(c => c.Text).Must(t => t is null || FormQuery.IsText(t))
            .WithMessage($"Cancellation note may be at most {AssertionText.MaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="EscalateFormQuery"/>.</summary>
internal sealed class EscalateFormQueryValidator : AbstractValidator<EscalateFormQuery>
{
    public EscalateFormQueryValidator()
    {
        RuleFor(c => c.QueryId).Must(id => !id.IsEmpty).WithMessage("Name the query.");
        RuleFor(c => c.EnrollmentId).Must(id => !id.IsEmpty).WithMessage("Name the enrollment.");
    }
}

/// <summary>Checks the shape of <see cref="GenerateFormQueryAgingDigest"/>.</summary>
internal sealed class GenerateFormQueryAgingDigestValidator : AbstractValidator<GenerateFormQueryAgingDigest>
{
    public GenerateFormQueryAgingDigestValidator()
    {
        RuleFor(c => c.DigestId).NotEqual(Guid.Empty).WithMessage("Name the digest.");
        RuleFor(c => c.StudyId).Must(id => !id.IsEmpty).WithMessage("Name the study.");
        RuleFor(c => c.RecipientRole).NotEmpty().MaximumLength(64);
        RuleFor(c => c.StudyCode).NotEmpty().MaximumLength(128);
    }
}
