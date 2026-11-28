using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Events;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;

using FluentValidation;

namespace Cadence.Kernel.Application.Protocol;

/// <summary>Validates structural inputs on <see cref="ImportProtocolDraft"/> before authorization and persistence.</summary>
internal sealed class ImportProtocolDraftValidator : AbstractValidator<ImportProtocolDraft>
{
    public ImportProtocolDraftValidator()
    {
        RuleFor(x => x.StudyId)
            .Must(id => !id.IsEmpty)
            .WithMessage("StudyId is required.");

        RuleFor(x => x.TargetVersionId)
            .Must(id => !id.IsEmpty)
            .WithMessage("TargetVersionId is required.");

        RuleFor(x => x.Mode)
            .IsInEnum()
            .WithMessage("Mode must be a defined ProtocolDraftImportMode.");

        RuleFor(x => x.Content)
            .NotEmpty()
            .WithMessage("Content is required.");

        RuleFor(x => x.ParentVersionId)
            .Must(id => id is { IsEmpty: false })
            .When(x => x.Mode == ProtocolDraftImportMode.ForkApproved)
            .WithMessage("ParentVersionId is required when forking an approved protocol version.");

        RuleFor(x => x.Amendment)
            .NotNull()
            .When(x => x.Mode == ProtocolDraftImportMode.ForkApproved)
            .WithMessage("Amendment is required when forking an approved protocol version.");

        RuleFor(x => x.StagedConcepts)
            .NotNull()
            .WithMessage("StagedConcepts must not be null.");

        RuleForEach(x => x.StagedConcepts).ChildRules(concept =>
        {
            concept.RuleFor(c => c.ConceptId)
                .Must(id => !id.IsEmpty)
                .WithMessage("ConceptId is required.");
            concept.RuleFor(c => c.PreferredLabel)
                .NotEmpty()
                .MaximumLength(500)
                .WithMessage("PreferredLabel is required and at most 500 characters.");
            concept.RuleFor(c => c.Definition)
                .NotEmpty()
                .WithMessage("Definition is required.");
        });
    }
}

/// <summary>
/// Handles <see cref="ImportProtocolDraft"/>: acquires the study scope lock, validates protocol version lifecycle
/// rules, and atomically commits staged <see cref="Concept"/> aggregates alongside the <see cref="ProtocolVersion"/>
/// draft creation, revision, reopening, or fork.
/// </summary>
internal sealed class ImportProtocolDraftHandler : ICommandHandler<ImportProtocolDraft, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ImportProtocolDraft command, ICommandContext context, CancellationToken cancellationToken)
    {
        var studyId = command.StudyId;
        if (!await context.Work.ExistsAsync<Study>(studyId, cancellationToken).ConfigureAwait(false))
        {
            return ProtocolErrors.StudyUnknown;
        }

        await context.Work.LockScopeAsync(NodeRef.For(studyId), cancellationToken).ConfigureAwait(false);

        var preparedConcepts = new List<Concept>();
        if (command.StagedConcepts.Count > 0)
        {
            var requestedCodes = command.StagedConcepts
                .Where(c => c.Origin == ConceptOrigin.CdiscBiomedicalConcept && !string.IsNullOrWhiteSpace(c.ExternalCode))
                .Select(c => c.ExternalCode!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var existingByCode = requestedCodes.Count == 0
                ? []
                : await context.Work.ReadAllAsync<Concept>(
                    c => c.Origin == ConceptOrigin.CdiscBiomedicalConcept && c.ExternalCode != null && requestedCodes.Contains(c.ExternalCode),
                    cancellationToken).ConfigureAwait(false);
            var existingCodes = existingByCode
                .Where(c => c.ExternalCode is not null)
                .Select(c => c.ExternalCode!)
                .ToHashSet(StringComparer.Ordinal);
            var seenIds = new HashSet<ConceptId>();
            var seenCodes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var staged in command.StagedConcepts.OrderBy(c => c.ConceptId.Value))
            {
                if (!seenIds.Add(staged.ConceptId))
                {
                    continue;
                }

                if (staged.Origin == ConceptOrigin.CdiscBiomedicalConcept && !string.IsNullOrWhiteSpace(staged.ExternalCode))
                {
                    var normalizedCode = staged.ExternalCode.Trim();
                    if (!seenCodes.Add(normalizedCode) || existingCodes.Contains(normalizedCode))
                    {
                        continue;
                    }
                }

                if (await context.Work.ExistsAsync<Concept>(staged.ConceptId, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var defined = Concept.Define(
                    staged.ConceptId,
                    command.TenantId,
                    staged.Origin,
                    staged.ExternalCode,
                    staged.Kind,
                    staged.PreferredLabel,
                    staged.Definition,
                    context.RecordedAt);
                if (!defined.IsSuccess)
                {
                    return defined.Error!;
                }

                preparedConcepts.Add(defined.Value!);
            }
        }

        ProtocolVersion targetVersion;
        switch (command.Mode)
        {
            case ProtocolDraftImportMode.CreateDraft:
                {
                    if (await context.Work.ExistsAsync<ProtocolVersion>(command.TargetVersionId, cancellationToken).ConfigureAwait(false))
                    {
                        return KernelErrors.AlreadyExists;
                    }

                    var existing = await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false);
                    CommandResult<ProtocolVersion> created;
                    if (existing.Count == 0)
                    {
                        created = ProtocolVersion.CreateFirst(command.TargetVersionId, command.TenantId, studyId, command.Content);
                    }
                    else if (existing.All(v => v.Status is ProtocolVersionStatus.Withdrawn or ProtocolVersionStatus.Superseded))
                    {
                        var nextNumber = existing.Max(v => v.Number) + 1;
                        created = ProtocolVersion.CreateRecoveryDraft(command.TargetVersionId, command.TenantId, studyId, nextNumber, command.Content);
                    }
                    else
                    {
                        return ProtocolErrors.FirstVersionExists;
                    }

                    if (created.Value is not { } draft)
                    {
                        return created.Error!;
                    }

                    if (command.Amendment is not null)
                    {
                        return ProtocolErrors.AmendmentNotAllowed;
                    }

                    context.Work.Add(draft);
                    context.Work.Raise(new ProtocolVersionDrafted(draft.TenantId, draft.StudyId, draft.Id, draft.Number, null, null));
                    targetVersion = draft;
                    break;
                }

            case ProtocolDraftImportMode.UpdateExisting:
            case ProtocolDraftImportMode.ReopenInReview:
                {
                    if (await context.Work.FindAsync<ProtocolVersion>(command.TargetVersionId, cancellationToken).ConfigureAwait(false) is not { } existing
                        || existing.StudyId != studyId)
                    {
                        return KernelErrors.NotFound;
                    }

                    if (command.Mode == ProtocolDraftImportMode.ReopenInReview && existing.Status != ProtocolVersionStatus.InReview)
                    {
                        return existing.ReturnToDraft()!;
                    }

                    var reopened = false;
                    if (existing.Status == ProtocolVersionStatus.InReview)
                    {
                        if (existing.ReturnToDraft() is { } reopenError)
                        {
                            return reopenError;
                        }

                        reopened = true;
                    }

                    if (existing.Revise(command.Content, command.Amendment) is { } reviseError)
                    {
                        return reviseError;
                    }

                    if (reopened)
                    {
                        context.Work.Raise(new ProtocolVersionReturnedToDraft(existing.TenantId, existing.StudyId, existing.Id));
                    }

                    context.Work.Raise(new ProtocolVersionRevised(existing.TenantId, existing.StudyId, existing.Id));
                    targetVersion = existing;
                    break;
                }

            case ProtocolDraftImportMode.ForkApproved:
                {
                    if (await context.Work.ExistsAsync<ProtocolVersion>(command.TargetVersionId, cancellationToken).ConfigureAwait(false))
                    {
                        return KernelErrors.AlreadyExists;
                    }

                    if (command.ParentVersionId is not { IsEmpty: false } parentId
                        || await context.Work.ReadAsync<ProtocolVersion>(parentId, cancellationToken).ConfigureAwait(false) is not { } parent
                        || parent.StudyId != studyId)
                    {
                        return KernelErrors.NotFound.WithMessage("The parent protocol version does not exist.");
                    }

                    var siblings = await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false);
                    var number = siblings.Max(v => v.Number) + 1;
                    var forked = ProtocolVersion.Fork(parent, command.TargetVersionId, number, command.Amendment!);
                    if (forked.Value is not { } draft)
                    {
                        return forked.Error!;
                    }

                    if (draft.Revise(command.Content, command.Amendment) is { } reviseError)
                    {
                        return reviseError;
                    }

                    context.Work.Add(draft);
                    context.Work.LinkInput(NodeRef.For(parent.Id, parent.VersionIndex), LineageEdgeKind.DerivedFrom);
                    context.Work.Raise(new ProtocolVersionDrafted(
                        draft.TenantId, draft.StudyId, draft.Id, draft.Number, draft.ParentVersionId, draft.Amendment));
                    context.Work.Raise(new ProtocolVersionRevised(draft.TenantId, draft.StudyId, draft.Id));
                    targetVersion = draft;
                    break;
                }

            default:
                return ProtocolErrors.InvalidTransition;
        }

        foreach (var concept in preparedConcepts)
        {
            context.Work.Add(concept);
            context.Work.Raise(new ConceptDefined(
                command.TenantId,
                concept.Current,
                concept.Origin,
                concept.ExternalCode,
                concept.Kind,
                concept.PreferredLabel));
        }

        return ProtocolVersionStates.Of(targetVersion);
    }
}
