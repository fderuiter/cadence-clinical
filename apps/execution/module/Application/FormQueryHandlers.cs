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
/// Handles <see cref="RaiseFormQuery"/>: either the value must be one of the form's at its current revision, or the item
/// must be a blank item of the form's CollectionSpec, and no Locked lock may cover it. Links the new derived
/// <see cref="FormQuery"/> aggregate to its governing enrollment, form instance, and (when present) target observation
/// revision (<c>K-LIN-1</c>).
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
                context.RecordedAt,
                command.ReasonForChange,
                command.Origin,
                command.OwnerRole,
                command.Priority,
                command.Assignee);
            context.Work.Add(query);
            context.Work.LinkInput(NodeRef.For(record.EnrollmentId, Math.Max(1L, record.EnrollmentVersion)), LineageEdgeKind.GovernedBy);
            context.Work.LinkInput(NodeRef.For(record.CollectionInstanceId, Math.Max(1L, record.Version)), LineageEdgeKind.CollectedBy);
            context.Work.LinkInput(NodeRef.For(value.ObservationId, value.Revision), LineageEdgeKind.DerivedFrom);
            return FormQueryScopes.Changed(
                context,
                query,
                step is null ? [] : [step],
                version: 1,
                revision: value.Revision,
                actorId: context.Actor.Id,
                reasonForChange: command.ReasonForChange);
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
            context.RecordedAt,
            command.ReasonForChange,
            command.Origin,
            command.OwnerRole,
            command.Priority,
            command.Assignee);
        context.Work.Add(blankQuery);
        context.Work.LinkInput(NodeRef.For(record.EnrollmentId, Math.Max(1L, record.EnrollmentVersion)), LineageEdgeKind.GovernedBy);
        context.Work.LinkInput(NodeRef.For(record.CollectionInstanceId, Math.Max(1L, record.Version)), LineageEdgeKind.CollectedBy);
        return FormQueryScopes.Changed(
            context,
            blankQuery,
            blankStep is null ? [] : [blankStep],
            version: 1,
            revision: 0,
            actorId: context.Actor.Id,
            reasonForChange: command.ReasonForChange);
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
            (query, record) =>
            {
                if (!query.TryAnswer(command.Text, AssertionId.New(), context.Actor.Id, context.RecordedAt, out var step, command.ReasonForChange))
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                int revision = FormQueryScopes.CurrentRevision(query, record);
                return FormQueryScopes.StepAllowed(step is null ? [] : [step], revision, context.Actor.Id, command.ReasonForChange);
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
            (query, record) =>
            {
                var matchesTarget = query.ObservationId == command.ObservationId
                    || (query.ObservationId is null
                        && record is not null
                        && record.Values.Any(v => v.ObservationId == command.ObservationId && string.Equals(v.ItemId, query.ItemId, StringComparison.Ordinal)));
                if (query.Origin != FormQueryOrigin.Manual || !matchesTarget)
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                var autoText = $"Value updated to revision {command.NewRevision}.";
                if (!query.TryAnswer(autoText, AssertionId.New(), command.AnsweredBy, context.RecordedAt, out var step, command.ReasonForChange))
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                context.Work.LinkInput(NodeRef.For(command.ObservationId, command.NewRevision), LineageEdgeKind.DerivedFrom);
                return FormQueryScopes.StepAllowed(step is null ? [] : [step], command.NewRevision, command.AnsweredBy, command.ReasonForChange);
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
            (query, record) =>
            {
                if (!query.TryReopen(command.Text, AssertionId.New(), context.Actor.Id, context.RecordedAt, out var step, command.ReasonForChange))
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                int revision = FormQueryScopes.CurrentRevision(query, record);
                return FormQueryScopes.StepAllowed(step is null ? [] : [step], revision, context.Actor.Id, command.ReasonForChange);
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
            (query, record) =>
            {
                var note = string.IsNullOrWhiteSpace(command.Text) ? command.ReasonForChange : command.Text;
                var outcome = query.Close(note, AssertionId.New(), context.Actor.Id, context.RecordedAt, command.ReasonForChange);
                if (!outcome.Succeeded)
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

                int revision = FormQueryScopes.CurrentRevision(query, record);
                return FormQueryScopes.StepAllowed(outcome.Step is null ? [] : [outcome.Step], revision, context.Actor.Id, command.ReasonForChange);
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
            (query, record) =>
            {
                var note = string.IsNullOrWhiteSpace(command.Text) ? command.ReasonForChange : command.Text;
                if (!query.TryCancel(note, AssertionId.New(), context.Actor.Id, context.RecordedAt, out var step, command.ReasonForChange))
                {
                    return FormQueryScopes.DeniedTransition(query.Status);
                }

                int revision = FormQueryScopes.CurrentRevision(query, record);
                return FormQueryScopes.StepAllowed(step is null ? [] : [step], revision, context.Actor.Id, command.ReasonForChange);
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
            (query, record) =>
            {
                if (!query.Escalate(context.Actor.Id, context.RecordedAt, command.ReasonForChange))
                {
                    return (null, FormQueryErrors.InvalidTransition.WithMessage("Only an unescalated open query past its 14-day due date can be escalated."));
                }

                int revision = FormQueryScopes.CurrentRevision(query, record);
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
        IReadOnlyList<KernelAssertionStep> Steps,
        int Revision,
        string ActorId,
        string ReasonForChange);

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

    /// <summary>Current revision of the queried item on <paramref name="record"/>, or the query's raised revision.</summary>
    public static int CurrentRevision(FormQuery query, CollectionInstanceRecord? record) =>
        query.ObservationId is { } obsId
            ? record?.Values.FirstOrDefault(v => v.ObservationId == obsId)?.Revision ?? query.Revision
            : record?.Values.FirstOrDefault(v => string.Equals(v.ItemId, query.ItemId, StringComparison.Ordinal))?.Revision ?? query.Revision;

    /// <summary>
    /// Loads the query for update, checks it belongs to <paramref name="enrollment"/> and that no Locked lock covers its
    /// value (including an observation recorded for <see cref="FormQuery.ItemId"/> after a blank-item query was raised),
    /// and applies <paramref name="step"/>.
    /// </summary>
    public static async Task<CommandResult<FormQueryState>> StepAsync(
        ICollectionInstanceRecords records,
        ICommandContext context,
        FormQueryId queryId,
        EnrollmentId enrollment,
        Func<FormQuery, CollectionInstanceRecord?, (StepDecision? Allowed, KernelError? Error)> step,
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

        var (allowed, error) = step(query, record);
        if (allowed is null)
        {
            return error ?? InvalidTransitionError(query.Status);
        }

        if (record is not null)
        {
            context.Work.LinkInput(NodeRef.For(record.EnrollmentId, Math.Max(1L, record.EnrollmentVersion)), LineageEdgeKind.GovernedBy);
        }

        return Changed(
            context,
            query,
            allowed.Steps,
            query.VersionIndex + 1,
            allowed.Revision,
            allowed.ActorId,
            allowed.ReasonForChange);
    }

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
        string reasonForChange)
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
        return CommandResult<FormQueryState>.Success(FormQueryState.Of(query, version));
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
