using System;
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
/// Handles <see cref="RaiseFormQuery"/>: the value must be one of the form's, at its current revision, and no Locked
/// lock may cover it. The form's scopes are shared first (ADR-2621), so a lock cannot commit between the check and the
/// query.
/// </summary>
internal sealed class RaiseFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<RaiseFormQuery, FormQueryState>
{
    public async Task<CommandResult<FormQueryState>> HandleAsync(RaiseFormQuery command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<FormQuery>(command.QueryId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var record = await FormQueryScopes.ShareAsync(records, context, command.FormId, command.ObservationId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.EnrollmentId != command.EnrollmentId)
        {
            return FormErrors.CollectionInstanceUnknown;
        }

        if (record.Values.FirstOrDefault(v => v.ObservationId == command.ObservationId) is not { } value)
        {
            return FormQueryErrors.ObservationUnknown;
        }

        if (value.Revision != command.Revision)
        {
            return FormQueryErrors.RevisionStale.WithMessage(
                $"The value's current revision is {value.Revision}, not {command.Revision}; reload the form and query the current value.");
        }

        if (FormQueryScopes.IsLocked(record, command.ObservationId))
        {
            return AssertionErrors.Locked;
        }

        var (query, step) = FormQuery.Raise(
            command.QueryId,
            context.TenantId,
            command.FormId,
            command.EnrollmentId,
            value.ItemId,
            command.ObservationId,
            value.Revision,
            command.Text,
            AssertionId.New(),
            context.Actor.Id,
            context.RecordedAt);
        context.Work.Add(query);
        return FormQueryScopes.Changed(context, query, step, version: 1);
    }
}

/// <summary>Handles <see cref="AnswerFormQuery"/>.</summary>
internal sealed class AnswerFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<AnswerFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(AnswerFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records, context, command.QueryId, command.EnrollmentId, q => q.Answer(command.Text, AssertionId.New(), context.Actor.Id, context.RecordedAt), cancellationToken);
}

/// <summary>Handles <see cref="ReopenFormQuery"/>.</summary>
internal sealed class ReopenFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<ReopenFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(ReopenFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records, context, command.QueryId, command.EnrollmentId, q => q.Reopen(command.Text, AssertionId.New(), context.Actor.Id, context.RecordedAt), cancellationToken);
}

/// <summary>Handles <see cref="CloseFormQuery"/>: the reason for change is the closing note.</summary>
internal sealed class CloseFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<CloseFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(CloseFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records, context, command.QueryId, command.EnrollmentId, q => q.Close(command.ReasonForChange, AssertionId.New(), context.Actor.Id, context.RecordedAt), cancellationToken);
}

/// <summary>Handles <see cref="CancelFormQuery"/>: the reason for change says why.</summary>
internal sealed class CancelFormQueryHandler(ICollectionInstanceRecords records) : ICommandHandler<CancelFormQuery, FormQueryState>
{
    public Task<CommandResult<FormQueryState>> HandleAsync(CancelFormQuery command, ICommandContext context, CancellationToken cancellationToken) =>
        FormQueryScopes.StepAsync(
            records, context, command.QueryId, command.EnrollmentId, q => q.Cancel(command.ReasonForChange, AssertionId.New(), context.Actor.Id, context.RecordedAt), cancellationToken);
}

/// <summary>The steps every query command shares.</summary>
internal static class FormQueryScopes
{
    /// <summary>
    /// Takes the shared locks on the scopes covering <paramref name="observation"/> of <paramref name="form"/>, widest
    /// first (ADR-2621), and reads the form again under them, so its locks are the ones in force for this command.
    /// </summary>
    /// <returns>The form's kernel record, or null when the kernel has no such form.</returns>
    public static async Task<CollectionInstanceRecord?> ShareAsync(
        ICollectionInstanceRecords records, ICommandContext context, CollectionInstanceId form, ObservationId observation, CancellationToken cancellationToken)
    {
        if (await records.FindAsync(context.TenantId, form, cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return null;
        }

        await context.Work.ShareScopesAsync(
            LockScopes.Covering(record.StudyId, record.StudySiteId, record.EnrollmentId, observation, form), cancellationToken).ConfigureAwait(false);
        return await records.FindAsync(context.TenantId, form, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True when a Locked lock covers <paramref name="observation"/>: one on the form or a wider scope, or on the value itself.</summary>
    public static bool IsLocked(CollectionInstanceRecord record, ObservationId observation) =>
        record.Locks.Any(l => l.Kind == LockKind.Locked && (l.Scope.Kind != NodeKinds.Observation || l.Scope.Id == observation.Value));

    /// <summary>
    /// Loads the query for update, checks it belongs to <paramref name="enrollment"/> and that no Locked lock covers its
    /// value, and applies <paramref name="step"/>; a step the query's status does not allow returns null.
    /// </summary>
    public static async Task<CommandResult<FormQueryState>> StepAsync(
        ICollectionInstanceRecords records,
        ICommandContext context,
        Guid queryId,
        EnrollmentId enrollment,
        Func<FormQuery, KernelAssertionStep?> step,
        CancellationToken cancellationToken)
    {
        await context.Work.LockAsync<FormQuery>(queryId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.FindAsync<FormQuery>(queryId, cancellationToken).ConfigureAwait(false) is not { } query
            || query.EnrollmentId != enrollment)
        {
            return KernelErrors.NotFound;
        }

        var record = await ShareAsync(records, context, query.FormId, query.ObservationId, cancellationToken).ConfigureAwait(false);
        if (record is not null && IsLocked(record, query.ObservationId))
        {
            return AssertionErrors.Locked;
        }

        var status = query.Status;
        return step(query) is { } kernelStep
            ? Changed(context, query, kernelStep, query.VersionIndex + 1)
            : FormQueryErrors.InvalidTransition.WithMessage($"A query that is {status.ToString().ToLowerInvariant()} does not allow that step.");
    }

    /// <summary>Raises <see cref="FormQueryChanged"/> for <paramref name="step"/> and returns the query's new state.</summary>
    public static CommandResult<FormQueryState> Changed(ICommandContext context, FormQuery query, KernelAssertionStep step, long version)
    {
        context.Work.Raise(new FormQueryChanged(
            context.TenantId, query.Id, query.FormId, query.EnrollmentId, query.ObservationId, query.Status, [step]));
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
        RuleFor(c => c.ObservationId).Must(id => !id.IsEmpty).WithMessage("Name the value's observation.");
        RuleFor(c => c.Revision).GreaterThanOrEqualTo(1).WithMessage("Name the revision queried, from 1.");
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
