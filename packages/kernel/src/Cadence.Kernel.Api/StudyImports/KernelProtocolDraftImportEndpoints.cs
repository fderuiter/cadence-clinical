using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cadence.Kernel.Api.StudyImports;

internal static class KernelProtocolDraftImportEndpoints
{
    internal static async Task<Results<Created<KernelProtocolDraftSummaryResponse>, Ok<KernelProtocolDraftSummaryResponse>, ProblemHttpResult>> ImportProtocolDraftAsync(
        string studyRef,
        HttpContext context,
        IStudyDirectory directory,
        IProtocolVersionReader versionReader,
        IProtocolDesignImporter importer,
        IConceptRegistry concepts,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is > (IProtocolDesignImporter.MaxPayloadBytes + 262_144))
        {
            return KernelProtocolImportInputReader.PayloadTooLargeProblem();
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var idempotencyKey = KernelStudyDirectoryResolver.IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var tenant = KernelRequestTenant.Get(context);
        var studyLookup = await KernelStudyDirectoryResolver.ResolveTargetStudyAsync(tenant, studyRef, directory, cancellationToken).ConfigureAwait(false);
        if (studyLookup.Problem is { } studyProblem)
        {
            return studyProblem;
        }

        var study = studyLookup.Study!;
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [NodeRef.For(study.StudyId)], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(NodeRef.For(study.StudyId)) is { } mask)
        {
            return KernelProblems.NotVisible($"You may not access study '{study.StudyCode}' ({mask}).");
        }

        var parsedPayload = await KernelProtocolImportInputReader.ReadImportInputAsync(context, cancellationToken).ConfigureAwait(false);
        if (parsedPayload.Problem is { } payloadProblem)
        {
            return payloadProblem;
        }

        var existingVersions = await versionReader.ListForStudyAsync(tenant, study.StudyId, cancellationToken).ConfigureAwait(false);
        var derivedVersionId = ProtocolVersionId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "protocol-version", $"{study.StudyId}:{idempotencyKey}"));
        var plan = await KernelProtocolDraftMutationPlanner.PlanDraftMutationAsync(
            tenant,
            study,
            existingVersions,
            derivedVersionId,
            versionReader,
            probe,
            catalog,
            cancellationToken).ConfigureAwait(false);
        if (plan.Problem is { } planProblem)
        {
            return planProblem;
        }

        var conceptStager = new KernelStudyImportConceptStager(tenant, concepts);
        var importOutcome = await importer.ImportAsync(
            new ProtocolDesignImportRequest(
                RawBytes: parsedPayload.RawBytes,
                FileName: parsedPayload.FileName,
                Format: parsedPayload.Format,
                TemplateIds: parsedPayload.TemplateIds,
                BaseDesign: plan.BaseDesign,
                FallbackTitle: study.Title,
                ReplaceForms: parsedPayload.ReplaceForms,
                TemplateAssignments: parsedPayload.TemplateAssignments),
            conceptStager.ResolveOrDefineConceptAsync,
            cancellationToken,
            conceptStager.ResolveOrRegisterTerminologyAsync).ConfigureAwait(false);

        if (importOutcome.PayloadTooLarge)
        {
            return KernelProtocolImportInputReader.PayloadTooLargeProblem(importOutcome.Problems.FirstOrDefault());
        }

        if (!importOutcome.IsSuccess || importOutcome.Design is null)
        {
            return KernelProtocolImportInputReader.ValidationFailedProblem(importOutcome.Problems);
        }

        if (conceptStager.StagedConcepts.Count > 0
            && !await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(DefineConcept), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        if (conceptStager.StagedCodeSystemsById.Count > 0
            && !await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(RegisterCodeSystem), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        foreach (var targetCodeSystemId in conceptStager.StagedCodeSystemVersionsById.Values.Select(v => v.CodeSystemId).Distinct())
        {
            if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(ImportCodeSystemVersion), NodeRef.For(targetCodeSystemId), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
            }
        }

        if (importOutcome.RulePack is not null
            && !await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(DraftLibraryItem), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        foreach (var stagedCodeSystem in conceptStager.StagedCodeSystemsById.Values)
        {
            var registered = await sender.SendAsync(
                new RegisterCodeSystem(
                    tenant,
                    stagedCodeSystem.CodeSystemId,
                    stagedCodeSystem.SystemUri,
                    "Protocol Imported Codelists",
                    "Cadence Protocol Import",
                    stagedCodeSystem.License,
                    KernelStudyDirectoryResolver.DeriveSubIdempotencyKey(idempotencyKey, "codesystem", stagedCodeSystem.CodeSystemId.Value),
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!registered.IsSuccess && !string.Equals(registered.Error?.Code, KernelErrors.AlreadyExists.Code, StringComparison.Ordinal))
            {
                return KernelErrorEndpoints.Problem(registered.Error!);
            }
        }

        foreach (var stagedVersion in conceptStager.StagedCodeSystemVersionsById.Values)
        {
            var imported = await sender.SendAsync(
                new ImportCodeSystemVersion(
                    tenant,
                    stagedVersion.VersionId,
                    stagedVersion.CodeSystemId,
                    stagedVersion.VersionLabel,
                    stagedVersion.ReleasedOn,
                    stagedVersion.Package,
                    stagedVersion.Entries,
                    KernelStudyDirectoryResolver.DeriveSubIdempotencyKey(idempotencyKey, "codesystem-version", stagedVersion.VersionId.Value),
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!imported.IsSuccess && !string.Equals(imported.Error?.Code, KernelErrors.AlreadyExists.Code, StringComparison.Ordinal))
            {
                return KernelErrorEndpoints.Problem(imported.Error!);
            }
        }

        var serializedDesign = ProtocolDesignDocument.Write(importOutcome.Design);
        var amendment = KernelProtocolDraftMutationPlanner.BuildProtocolAmendment(
            plan,
            importOutcome.Design,
            serializedDesign,
            reason,
            parsedPayload.AmendmentClassification,
            parsedPayload.TransitionPolicy);

        var requestFingerprint = KernelProtocolImportInputReader.ComputeImportRequestFingerprint(study.StudyId, parsedPayload);
        var persistResult = await sender.SendAsync(
            new ImportProtocolDraft(
                tenant,
                study.StudyId,
                plan.Mode,
                plan.TargetVersionId,
                plan.ParentVersionId,
                serializedDesign,
                amendment,
                conceptStager.StagedConcepts,
                idempotencyKey,
                reason,
                requestFingerprint),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!persistResult.IsSuccess || persistResult.Value is null)
        {
            return KernelErrorEndpoints.Problem(persistResult.Error!);
        }

        var persistedState = persistResult.Value;
        var persistedSummary = await versionReader.GetAsync(tenant, persistedState.ProtocolVersionId, cancellationToken).ConfigureAwait(false)
            ?? new ProtocolVersionSummary(
                persistedState.ProtocolVersionId,
                persistedState.StudyId,
                persistedState.Number,
                plan.ParentVersionId,
                persistedState.Status,
                persistedState.ContentHash,
                amendment?.Intent ?? plan.AmendmentIntent,
                null,
                null);
        var effectiveDesign = persistResult.Replayed
            ? await versionReader.GetDesignAsync(tenant, persistedState.ProtocolVersionId, cancellationToken).ConfigureAwait(false) ?? importOutcome.Design
            : importOutcome.Design;
        var effectiveAmendment = persistResult.Replayed
            ? await versionReader.GetAmendmentAsync(tenant, persistedState.ProtocolVersionId, cancellationToken).ConfigureAwait(false) ?? amendment
            : amendment;

        KernelImportedRulePackSummaryResponse? draftedRulePack = null;
        if (importOutcome.RulePack is { } rulePack)
        {
            var libraryItemId = LibraryItemId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "import-rule-pack", $"{study.StudyId}:{idempotencyKey}"));
            var draftItemResult = await sender.SendAsync(
                new DraftLibraryItem(
                    tenant,
                    libraryItemId,
                    LibraryItemKind.RulePack,
                    rulePack.Key,
                    rulePack.Title,
                    rulePack.Content,
                    [],
                    $"{idempotencyKey}:rule-pack",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!draftItemResult.IsSuccess)
            {
                return KernelErrorEndpoints.Problem(draftItemResult.Error!);
            }

            draftedRulePack = KernelImportedRulePackSummaryResponse.From(draftItemResult.Value!, rulePack.Title, rulePack.RuleCount);
        }

        var conceptLabels = await KernelProtocolTemplateEndpoints.ResolveConceptLabelsAsync(
            tenant,
            effectiveDesign,
            concepts,
            conceptStager.StagedLabelsByRef,
            cancellationToken).ConfigureAwait(false);
        var responseBody = KernelProtocolDraftSummaryResponse.From(
            persistedSummary,
            effectiveDesign,
            conceptLabels,
            draftedRulePack,
            effectiveAmendment);

        return plan.CreatedNewVersion
            ? TypedResults.Created(
                $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{NodeRef.For(persistedSummary.Id)}",
                responseBody)
            : TypedResults.Ok(responseBody);
    }
}
