using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Domain;

using FluentValidation;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Handles <see cref="RaiseFormQuery"/>: either the value must be one of the form's at its current revision, or the item
/// must be a blank item of the form's CollectionSpec, and no Locked lock may cover it. The form's scopes are shared first
/// (ADR-2621), so neither a concurrent save nor a lock can commit unnoticed between the check and the query.
/// </summary>
internal sealed class RaiseFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<RaiseFormQuery, FormQueryState>
{
    public async Task<CommandResult<FormQueryState>> HandleAsync(RaiseFormQuery command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<FormQuery>(command.QueryId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var itemId = command.ItemId.Trim();
        var (record, currentObservation) = await FormQueryScopes.ShareAsync(
            records, context, command.FormId, command.ObservationId, itemId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.EnrollmentId != command.EnrollmentId)
        {
            return FormErrors.CollectionInstanceUnknown;
        }

        if (command.ObservationId is { } targetObservation)
        {
            if (record.Values.FirstOrDefault(v => v.ObservationId == targetObservation) is not { } value
                || !string.Equals(value.ItemId, itemId, StringComparison.Ordinal))
            {
                return FormQueryErrors.ObservationUnknown;
            }

            if (FormQueryScopes.IsLocked(record, targetObservation))
            {
                return AssertionErrors.Locked;
            }

            if (value.Revision != command.Revision)
            {
                return FormQueryErrors.RevisionStale.WithMessage(
                    $"The value's current revision is {value.Revision}, not {command.Revision}; reload the form and query the current value.");
            }

            var (query, step) = FormQuery.Raise(
                command.QueryId,
                context.TenantId,
                command.FormId,
                command.EnrollmentId,
                value.ItemId,
                targetObservation,
                value.Revision,
                command.Text,
                AssertionId.New(),
                context.Actor.Id,
                context.RecordedAt);
            context.Work.Add(query);
            return FormQueryScopes.Changed(context, query, step, version: 1);
        }

        if (!record.Items.Any(i => string.Equals(i.Id, itemId, StringComparison.Ordinal)))
        {
            return CollectionErrors.ValuesInvalid with
            {
                Errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [itemId] = [$"CollectionSpec {record.CollectionSpecId} has no item {itemId}."],
                },
            };
        }

        if (FormQueryScopes.IsLocked(record, currentObservation))
        {
            return AssertionErrors.Locked;
        }

        if (record.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal)) is { } existing)
        {
            return FormQueryErrors.RevisionStale.WithMessage(
                $"Item {itemId} now holds a value at revision {existing.Revision}; reload the form and query the current value.");
        }

        var (blankQuery, blankStep) = FormQuery.Raise(
            command.QueryId,
            context.TenantId,
            command.FormId,
            command.EnrollmentId,
            itemId,
            observationId: null,
            revision: 0,
            command.Text,
            kernelQueryId: null,
            context.Actor.Id,
            context.RecordedAt);
        context.Work.Add(blankQuery);
        return FormQueryScopes.Changed(context, blankQuery, blankStep, version: 1);
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
            (FormQuery q, out KernelAssertionStep? step) => q.TryAnswer(command.Text, AssertionId.New(), context.Actor.Id, context.RecordedAt, out step),
            cancellationToken);
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
            (FormQuery q, out KernelAssertionStep? step) => q.TryReopen(command.Text, AssertionId.New(), context.Actor.Id, context.RecordedAt, out step),
            cancellationToken);
}

/// <summary>Handles <see cref="CloseFormQuery"/>: the reason for change is the closing note.</summary>
internal sealed class CloseFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<CloseFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(CloseFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records,
            context,
            command.QueryId,
            command.EnrollmentId,
            (FormQuery q, out KernelAssertionStep? step) => q.TryClose(command.ReasonForChange, AssertionId.New(), context.Actor.Id, context.RecordedAt, out step),
            cancellationToken);
}

/// <summary>Handles <see cref="CancelFormQuery"/>: the reason for change says why.</summary>
internal sealed class CancelFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<CancelFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(CancelFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records,
            context,
            command.QueryId,
            command.EnrollmentId,
            (FormQuery q, out KernelAssertionStep? step) => q.TryCancel(command.ReasonForChange, AssertionId.New(), context.Actor.Id, context.RecordedAt, out step),
            cancellationToken);
}

/// <summary>The steps every query command shares.</summary>
internal static class FormQueryScopes
{
    public delegate bool QueryTransition(FormQuery query, out KernelAssertionStep? step);

    /// <summary>
    /// Takes the shared locks on the scopes covering <paramref name="observation"/> (or the observation currently recorded
    /// for <paramref name="itemId"/>) of <paramref name="form"/>, widest first (ADR-2621), and reads the form again under
    /// them, so its locks and values are the ones in force for this command.
    /// </summary>
    public static async Task<(CollectionInstanceRecord? Record, ObservationId? CurrentObservation)> ShareAsync(
        ICollectionInstanceRecords records,
        ICommandContext context,
        CollectionInstanceId form,
        ObservationId? observation,
        string itemId,
        CancellationToken cancellationToken)
    {
        if (await records.FindAsync(context.TenantId, form, cancellationToken).ConfigureAwait(false) is not { } initial)
        {
            return (null, null);
        }

        var resolved = observation
            ?? initial.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal))?.ObservationId;
        await context.Work.ShareScopesAsync(
            LockScopes.Covering(initial.StudyId, initial.StudySiteId, initial.EnrollmentId, resolved, form), cancellationToken).ConfigureAwait(false);
        var locked = await records.FindAsync(context.TenantId, form, cancellationToken).ConfigureAwait(false);
        if (locked is null)
        {
            return (null, null);
        }

        var current = observation
            ?? locked.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal))?.ObservationId;
        if (resolved is null && current is { } newlySaved)
        {
            await context.Work.ShareScopesAsync([NodeRef.For(newlySaved)], cancellationToken).ConfigureAwait(false);
            locked = await records.FindAsync(context.TenantId, form, cancellationToken).ConfigureAwait(false);
        }

        return (locked, current);
    }

    /// <summary>True when a Locked lock covers <paramref name="observation"/>: one on the form or a wider scope, or on the value itself.</summary>
    public static bool IsLocked(CollectionInstanceRecord record, ObservationId? observation) =>
        record.Locks.Any(l =>
            l.Kind == LockKind.Locked
            && (l.Scope.Kind != NodeKinds.Observation || (observation is { } obs && l.Scope.Id == obs.Value)));

    /// <summary>
    /// Loads the query for update, checks it belongs to <paramref name="enrollment"/> and that no Locked lock covers its
    /// value (including an observation recorded for <see cref="FormQuery.ItemId"/> after a blank-item query was raised),
    /// and applies <paramref name="step"/>.
    /// </summary>
    public static async Task<CommandResult<FormQueryState>> StepAsync(
        ICollectionInstanceRecords records,
        ICommandContext context,
        Guid queryId,
        EnrollmentId enrollment,
        QueryTransition step,
        CancellationToken cancellationToken)
    {
        await context.Work.LockAsync<FormQuery>(queryId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.FindAsync<FormQuery>(queryId, cancellationToken).ConfigureAwait(false) is not { } query
            || query.EnrollmentId != enrollment)
        {
            return KernelErrors.NotFound;
        }

        var (record, currentObservation) = await ShareAsync(
            records, context, query.FormId, query.ObservationId, query.ItemId, cancellationToken).ConfigureAwait(false);
        if (record is not null && IsLocked(record, currentObservation))
        {
            return AssertionErrors.Locked;
        }

        var status = query.Status;
        return step(query, out var kernelStep)
            ? Changed(context, query, kernelStep, query.VersionIndex + 1)
            : FormQueryErrors.InvalidTransition.WithMessage($"A query that is {status.ToString().ToLowerInvariant()} does not allow that step.");
    }

    /// <summary>Raises <see cref="FormQueryChanged"/> for <paramref name="step"/> and returns the query's new state.</summary>
    public static CommandResult<FormQueryState> Changed(ICommandContext context, FormQuery query, KernelAssertionStep? step, long version)
    {
        context.Work.Raise(new FormQueryChanged(
            context.TenantId,
            query.Id,
            query.FormId,
            query.EnrollmentId,
            query.ObservationId,
            query.Status,
            step is null ? [] : [step]));
        return CommandResult<FormQueryState>.Success(FormQueryState.Of(query, version));
    }
}

/// <summary>Checks the shape of <see cref="RaiseFormQuery"/>.</summary>
internal sealed class RaiseFormQueryValidator : AbstractValidator<RaiseFormQuery>
{
    public RaiseFormQueryValidator()
    {
        RuleFor(c => c.QueryId).NotEqual(Guid.Empty).WithMessage("Name the query.");
        RuleFor(c => c.FormId).Must(id => !id.IsEmpty).WithMessage("Name the form.");
        RuleFor(c => c.EnrollmentId).Must(id => !id.IsEmpty).WithMessage("Name the form's enrollment.");
        RuleFor(c => c.ItemId)
            .Must(id => !string.IsNullOrWhiteSpace(id) && id.Trim().Length <= FormInstance.ElementIdMaxLength)
            .WithMessage("Name the form item.");
        RuleFor(c => c.ObservationId)
            .Must(id => id is null || !id.Value.IsEmpty)
            .WithMessage("Name the value's observation, or leave it null for a blank item.");
        RuleFor(c => c.Revision)
            .Must((c, r) => c.ObservationId is null ? r == 0 : r >= 1)
            .WithMessage("Name revision 0 for a blank item, or a revision from 1 for a recorded observation.");
        RuleFor(c => c.Text).Must(FormQuery.IsText).WithMessage($"Write the query, at most {AssertionText.MaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="AnswerFormQuery"/>.</summary>
internal sealed class AnswerFormQueryValidator : AbstractValidator<AnswerFormQuery>
{
    public AnswerFormQueryValidator()
    {
        RuleFor(c => c.QueryId).NotEqual(Guid.Empty).WithMessage("Name the query.");
        RuleFor(c => c.Text).Must(FormQuery.IsText).WithMessage($"Write the answer, at most {AssertionText.MaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="ReopenFormQuery"/>.</summary>
internal sealed class ReopenFormQueryValidator : AbstractValidator<ReopenFormQuery>
{
    public ReopenFormQueryValidator()
    {
        RuleFor(c => c.QueryId).NotEqual(Guid.Empty).WithMessage("Name the query.");
        RuleFor(c => c.Text).Must(FormQuery.IsText).WithMessage($"Say why the query is reopened, at most {AssertionText.MaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="CloseFormQuery"/>: the reason is the closing note.</summary>
internal sealed class CloseFormQueryValidator : AbstractValidator<CloseFormQuery>
{
    public CloseFormQueryValidator()
    {
        RuleFor(c => c.QueryId).NotEqual(Guid.Empty).WithMessage("Name the query.");
        RuleFor(c => c.ReasonForChange).Must(FormQuery.IsText).WithMessage($"Say why the query is closed, at most {AssertionText.MaxLength} characters.");
    }
}

/// <summary>Checks the shape of <see cref="CancelFormQuery"/>.</summary>
internal sealed class CancelFormQueryValidator : AbstractValidator<CancelFormQuery>
{
    public CancelFormQueryValidator()
    {
        RuleFor(c => c.QueryId).NotEqual(Guid.Empty).WithMessage("Name the query.");
        RuleFor(c => c.ReasonForChange).Must(FormQuery.IsText).WithMessage($"Say why the query is cancelled, at most {AssertionText.MaxLength} characters.");
    }
}
