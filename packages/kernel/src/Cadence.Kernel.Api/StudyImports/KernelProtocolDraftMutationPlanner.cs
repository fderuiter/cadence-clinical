using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api.StudyImports;

internal static class KernelProtocolDraftMutationPlanner
{
    internal static async Task<DraftMutationPlan> PlanDraftMutationAsync(
        TenantId tenant,
        StudyDirectoryEntry study,
        IReadOnlyList<ProtocolVersionSummary> existingVersions,
        ProtocolVersionId derivedVersionId,
        IProtocolVersionReader versionReader,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        CancellationToken cancellationToken)
    {
        var ordered = existingVersions
            .OrderByDescending(v => v.Number)
            .ToList();

        // Check if a previous request with this exact Idempotency-Key already created derivedVersionId.
        var matchingReplayVersion = ordered.FirstOrDefault(v => v.Id == derivedVersionId);
        if (matchingReplayVersion is not null)
        {
            if (matchingReplayVersion.ParentVersionId is null)
            {
                if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(CreateProtocolDraft), NodeRef.For(study.StudyId), probe, catalog, cancellationToken).ConfigureAwait(false))
                {
                    return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
                }

                var priorSuperseded = ordered
                    .FirstOrDefault(v => v.Id != derivedVersionId && v.Status == ProtocolVersionStatus.Superseded);
                var replayRecoveryBase = priorSuperseded is not null
                    ? await versionReader.GetDesignAsync(tenant, priorSuperseded.Id, cancellationToken).ConfigureAwait(false)
                    : null;

                return new DraftMutationPlan(
                    ProtocolDraftImportMode.CreateDraft,
                    derivedVersionId,
                    ParentVersionId: null,
                    BaseDesign: replayRecoveryBase,
                    ParentContent: null,
                    ExistingAmendment: null,
                    CreatedNewVersion: true,
                    AmendmentIntent: null,
                    Problem: null);
            }

            var replayParentId = matchingReplayVersion.ParentVersionId.Value;
            if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(ForkProtocolVersion), NodeRef.For(replayParentId), probe, catalog, cancellationToken).ConfigureAwait(false)
                || !await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(ReviseProtocolDraft), NodeRef.For(replayParentId), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            var (replayParentDesign, replayParentContent) = await LoadDesignAndContentAsync(
                tenant,
                replayParentId,
                versionReader,
                cancellationToken).ConfigureAwait(false);
            var replayAmendment = await versionReader.GetAmendmentAsync(tenant, matchingReplayVersion.Id, cancellationToken).ConfigureAwait(false);
            return new DraftMutationPlan(
                ProtocolDraftImportMode.ForkApproved,
                derivedVersionId,
                ParentVersionId: replayParentId,
                BaseDesign: replayParentDesign,
                ParentContent: replayParentContent,
                ExistingAmendment: replayAmendment,
                CreatedNewVersion: true,
                AmendmentIntent: matchingReplayVersion.AmendmentIntent,
                Problem: null);
        }

        if (ordered.Count == 0)
        {
            if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(CreateProtocolDraft), NodeRef.For(study.StudyId), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            return new DraftMutationPlan(
                ProtocolDraftImportMode.CreateDraft,
                derivedVersionId,
                ParentVersionId: null,
                BaseDesign: null,
                ParentContent: null,
                ExistingAmendment: null,
                CreatedNewVersion: true,
                AmendmentIntent: null,
                Problem: null);
        }

        if (ordered.FirstOrDefault(v => v.Status == ProtocolVersionStatus.Draft) is { } openDraft)
        {
            if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(ReviseProtocolDraft), NodeRef.For(openDraft.Id), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            var currentDesign = await versionReader.GetDesignAsync(tenant, openDraft.Id, cancellationToken).ConfigureAwait(false);
            string? draftParentContent = null;
            ProtocolAmendment? existingAmendment = null;
            if (openDraft.ParentVersionId is { } draftParentId)
            {
                (_, draftParentContent) = await LoadDesignAndContentAsync(tenant, draftParentId, versionReader, cancellationToken).ConfigureAwait(false);
                existingAmendment = await versionReader.GetAmendmentAsync(tenant, openDraft.Id, cancellationToken).ConfigureAwait(false);
            }

            return new DraftMutationPlan(
                ProtocolDraftImportMode.UpdateExisting,
                openDraft.Id,
                ParentVersionId: openDraft.ParentVersionId,
                BaseDesign: currentDesign,
                ParentContent: draftParentContent,
                ExistingAmendment: existingAmendment,
                CreatedNewVersion: false,
                AmendmentIntent: openDraft.AmendmentIntent ?? existingAmendment?.Intent,
                Problem: null);
        }

        if (ordered.FirstOrDefault(v => v.Status == ProtocolVersionStatus.InReview) is { } inReview)
        {
            if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(ReturnProtocolVersionToDraft), NodeRef.For(inReview.Id), probe, catalog, cancellationToken).ConfigureAwait(false)
                || !await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(ReviseProtocolDraft), NodeRef.For(inReview.Id), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            var reviewDesign = await versionReader.GetDesignAsync(tenant, inReview.Id, cancellationToken).ConfigureAwait(false);
            string? reviewParentContent = null;
            ProtocolAmendment? reviewAmendment = null;
            if (inReview.ParentVersionId is { } reviewParentId)
            {
                (_, reviewParentContent) = await LoadDesignAndContentAsync(tenant, reviewParentId, versionReader, cancellationToken).ConfigureAwait(false);
                reviewAmendment = await versionReader.GetAmendmentAsync(tenant, inReview.Id, cancellationToken).ConfigureAwait(false);
            }

            return new DraftMutationPlan(
                ProtocolDraftImportMode.ReopenInReview,
                inReview.Id,
                ParentVersionId: inReview.ParentVersionId,
                BaseDesign: reviewDesign,
                ParentContent: reviewParentContent,
                ExistingAmendment: reviewAmendment,
                CreatedNewVersion: false,
                AmendmentIntent: inReview.AmendmentIntent ?? reviewAmendment?.Intent,
                Problem: null);
        }

        if (ordered.FirstOrDefault(v => v.Status == ProtocolVersionStatus.Approved) is { } approved)
        {
            if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(ForkProtocolVersion), NodeRef.For(approved.Id), probe, catalog, cancellationToken).ConfigureAwait(false)
                || !await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(ReviseProtocolDraft), NodeRef.For(approved.Id), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            var (approvedDesign, approvedContent) = await LoadDesignAndContentAsync(
                tenant,
                approved.Id,
                versionReader,
                cancellationToken).ConfigureAwait(false);
            return new DraftMutationPlan(
                ProtocolDraftImportMode.ForkApproved,
                derivedVersionId,
                ParentVersionId: approved.Id,
                BaseDesign: approvedDesign,
                ParentContent: approvedContent,
                ExistingAmendment: null,
                CreatedNewVersion: true,
                AmendmentIntent: "Protocol design import",
                Problem: null);
        }

        // All existing versions are terminal (Withdrawn and/or Superseded): create a recovery Draft at max(Number) + 1.
        if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(CreateProtocolDraft), NodeRef.For(study.StudyId), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
        }

        var latestSuperseded = ordered.FirstOrDefault(v => v.Status == ProtocolVersionStatus.Superseded);
        var recoveryBaseDesign = latestSuperseded is not null
            ? await versionReader.GetDesignAsync(tenant, latestSuperseded.Id, cancellationToken).ConfigureAwait(false)
            : null;
        return new DraftMutationPlan(
            ProtocolDraftImportMode.CreateDraft,
            derivedVersionId,
            ParentVersionId: null,
            BaseDesign: recoveryBaseDesign,
            ParentContent: null,
            ExistingAmendment: null,
            CreatedNewVersion: true,
            AmendmentIntent: null,
            Problem: null);
    }

    internal static ProtocolAmendment? BuildProtocolAmendment(
        DraftMutationPlan plan,
        ProtocolDesign design,
        string serializedDesign,
        string reason,
        AmendmentClassification? requestedClassification,
        TransitionPolicy? requestedTransitionPolicy)
    {
        if (plan.ParentVersionId is null)
        {
            return null;
        }

        List<string> impacted = [];
        var diff = ProtocolStructuralDiff.Compare(plan.ParentContent, serializedDesign);
        if (diff.IsSuccess && diff.Value is { Count: > 0 } changes)
        {
            impacted =
            [
                .. changes
                    .Select(c => c.ElementId)
                    .Where(id => !string.Equals(id, ProtocolStructuralDiff.RootId, StringComparison.Ordinal)),
            ];
            if (impacted.Count == 0)
            {
                impacted = [.. changes.Select(c => c.ElementId)];
            }
        }

        ProtocolDesign? parentDesign = null;
        if (plan.ParentContent is not null
            && ProtocolDesignDocument.Read(plan.ParentContent) is { IsValid: true, Design: { } parsedParent })
        {
            parentDesign = parsedParent;
        }
        else if (plan.Mode == ProtocolDraftImportMode.ForkApproved)
        {
            parentDesign = plan.BaseDesign;
        }

        if (parentDesign is not null)
        {
            var soaImpacted = ScheduleOfActivitiesDiff.Compare(parentDesign, design).ImpactedElements;
            if (soaImpacted.Count > 0)
            {
                impacted =
                [
                    .. impacted
                        .Where(id => !string.Equals(id, ProtocolStructuralDiff.RootId, StringComparison.Ordinal))
                        .Concat(soaImpacted)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal),
                ];
            }
        }

        if (impacted.Count == 0)
        {
            impacted = plan.ExistingAmendment?.ImpactedElements is { Count: > 0 } existingImpacted
                ? [.. existingImpacted]
                : [.. design.CollectionSpecs.Select(s => s.Id).DefaultIfEmpty("protocol_design")];
        }

        var classification = requestedClassification
            ?? plan.ExistingAmendment?.Classification
            ?? AmendmentClassification.NonSubstantial;
        var transitionPolicy = requestedTransitionPolicy
            ?? plan.ExistingAmendment?.TransitionPolicy
            ?? TransitionPolicy.NewParticipantsOnly;
        var intent = plan.ExistingAmendment?.Intent
            ?? plan.AmendmentIntent
            ?? "Protocol design import";

        return new ProtocolAmendment(
            Intent: intent,
            Rationale: reason,
            Classification: classification,
            SummaryOfChanges: $"Imported {design.CollectionSpecs.Count} protocol form specification(s) ({impacted.Count} impacted element(s)).",
            ImpactedElements: impacted,
            TransitionPolicy: transitionPolicy);
    }

    private static async Task<(ProtocolDesign? Design, string? Content)> LoadDesignAndContentAsync(
        TenantId tenant,
        ProtocolVersionId versionId,
        IProtocolVersionReader versionReader,
        CancellationToken cancellationToken)
    {
        var rawContent = await versionReader.GetContentAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        if (rawContent is not null)
        {
            var parsed = ProtocolDesignDocument.Read(rawContent);
            if (parsed.IsValid)
            {
                return (parsed.Design, rawContent);
            }

            var fallbackDesign = await versionReader.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
            return (fallbackDesign, rawContent);
        }

        var design = await versionReader.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        return (design, design is not null ? ProtocolDesignDocument.Write(design) : null);
    }
}
