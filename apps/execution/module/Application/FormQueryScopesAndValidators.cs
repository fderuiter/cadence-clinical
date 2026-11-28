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

/// <summary>The steps every query command shares.</summary>
internal static class FormQueryScopes
{
    internal sealed record StepDecision(
        IReadOnlyList<KernelAssertionStep> Steps,
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
        IReadOnlyList<KernelAssertionStep> steps,
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
