using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Kernel.Api;

/// <summary>
/// Study browsing, self-service study registration, bundled eCRF template discovery, and synchronous ODM v1.3.2 XML /
/// USDM v4 JSON protocol draft import endpoints. Every mutating operation executes through
/// <see cref="IKernelCommandSender"/> with a signed reason for change (<c>X-Change-Reason</c>) and an
/// <c>Idempotency-Key</c>. Imported protocol definitions always enter <see cref="ProtocolVersionStatus.Draft"/>.
/// </summary>
public static class KernelStudyImportEndpoints
{
    private static readonly FormOptions MultipartLimits = new()
    {
        MultipartBodyLengthLimit = IProtocolDesignImporter.MaxPayloadBytes + 262_144,
        ValueLengthLimit = IProtocolDesignImporter.MaxPayloadBytes,
    };

    /// <summary>Maps study directory, study creation, template listing, and synchronous protocol draft import routes.</summary>
    public static RouteGroupBuilder MapKernelStudyImports(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>();

        group.MapGet("/studies", ListStudiesAsync)
            .WithName("KernelListStudies")
            .WithSummary("Lists visible studies in the caller's tenant.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/organizations", ListOrganizationsAsync)
            .WithName("KernelListOrganizations")
            .WithSummary("Lists visible organizations in the caller's tenant.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/studies", CreateStudyAsync)
            .WithName("KernelCreateStudy")
            .WithSummary("Registers a new study in the caller's tenant with a signed reason for change (study.register).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesKernelCommand<RegisterStudy>();

        group.MapGet("/protocol-templates", ListProtocolTemplates)
            .WithName("KernelListProtocolTemplates")
            .WithSummary("Lists bundled standard CDISC Library eCRF templates available for protocol draft import.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/protocol-versions/{versionRef}/design-summary", GetProtocolDesignSummaryAsync)
            .WithName("KernelGetProtocolDesignSummary")
            .WithSummary("Returns a viewable summary of a protocol version's forms, ordered items, and import notes.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/studies/{studyRef}/protocol-imports", ImportProtocolDraftAsync)
            .WithName("KernelImportProtocolDraft")
            .WithSummary("Synchronously imports ODM v1.3.2 XML, USDM v4 JSON, or bundled eCRF templates into a protocol draft.")
            .Accepts<KernelImportProtocolJsonRequest>("application/json", "multipart/form-data")
            .Produces<KernelProtocolDraftSummaryResponse>(StatusCodes.Status200OK)
            .Produces<KernelProtocolDraftSummaryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesKernelErrors(
            [
                .. KernelErrors.CommandPipeline,
                KernelErrors.AlreadyExists,
                KernelErrors.NotFound,
                ProtocolErrors.StudyUnknown,
                ProtocolErrors.FirstVersionExists,
                ProtocolErrors.InvalidTransition,
                ProtocolErrors.ContentRequired,
                ProtocolErrors.ContentInvalid,
                ProtocolErrors.AmendmentInvalid,
                ProtocolErrors.AmendmentNotAllowed,
            ]);

        return kernel;
    }

    internal static async Task<Ok<IReadOnlyList<KernelStudySummaryResponse>>> ListStudiesAsync(
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var directory = context.RequestServices.GetService<IStudyDirectory>();
        if (directory is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelStudySummaryResponse>>([]);
        }

        var tenant = KernelRequestTenant.Get(context);
        var entries = await directory.ListStudiesAsync(tenant, cancellationToken).ConfigureAwait(false);
        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            entries.Select(e => NodeRef.For(e.StudyId)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<KernelStudySummaryResponse>>(
            [.. entries.Where(e => masks.For(NodeRef.For(e.StudyId)) is null).Select(KernelStudySummaryResponse.From)]);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelOrganizationSummaryResponse>>, ProblemHttpResult>> ListOrganizationsAsync(
        string? kind,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        OrganizationKind? wantedKind = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<OrganizationKind>(kind, ignoreCase: true, out var parsedKind) || !Enum.IsDefined(parsedKind))
            {
                return KernelProblems.BadRequest("Invalid organization kind", $"'{kind}' is not a valid organization kind (Sponsor, Cro, SiteInstitution).");
            }

            wantedKind = parsedKind;
        }

        var directory = context.RequestServices.GetService<IStudyDirectory>();
        if (directory is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelOrganizationSummaryResponse>>([]);
        }

        var tenant = KernelRequestTenant.Get(context);
        var entries = await directory.ListOrganizationsAsync(tenant, cancellationToken).ConfigureAwait(false);
        if (wantedKind is { } filter)
        {
            entries = [.. entries.Where(e => e.Kind == filter)];
        }

        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            entries.Select(e => NodeRef.For(e.OrganizationId)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<KernelOrganizationSummaryResponse>>(
            [.. entries.Where(e => masks.For(NodeRef.For(e.OrganizationId)) is null).Select(KernelOrganizationSummaryResponse.From)]);
    }

    internal static async Task<Results<Created<KernelStudySummaryResponse>, ProblemHttpResult>> CreateStudyAsync(
        KernelCreateStudyRequest? request,
        HttpContext context,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid study request", "Provide study_code, title, phase, and sponsor details.");
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var studyCode = (request.StudyCode ?? string.Empty).Trim();
        var title = (request.Title ?? string.Empty).Trim();
        var phase = (request.Phase ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return KernelErrorEndpoints.Problem(StudyErrors.CodeRequired);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return KernelErrorEndpoints.Problem(StudyErrors.TitleRequired);
        }

        if (string.IsNullOrWhiteSpace(phase))
        {
            return TypedResults.Problem(
                title: "Phase required",
                detail: "A clinical phase (for example Phase 1, Phase 2, or Phase 3) is required.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (catalog.Find(typeof(RegisterStudy)) is { } registerEntry)
        {
            var canRegister = await probe.CanIAsync(tenant, registerEntry.Capability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false);
            if (!canRegister.Permitted)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
            }
        }

        var sponsorResult = await ResolveOrRegisterSponsorAsync(
            tenant,
            request,
            idempotencyKey,
            reason,
            directory,
            sender,
            cancellationToken).ConfigureAwait(false);
        if (sponsorResult.Problem is { } sponsorProblem)
        {
            return sponsorProblem;
        }

        var sponsor = sponsorResult.Sponsor!;
        var derivedStudyId = StudyId.FromGuid(DeriveGuid(tenant, "study", idempotencyKey));
        var registerResult = await sender.SendAsync(
            new RegisterStudy(tenant, derivedStudyId, sponsor.OrganizationId, studyCode, title, phase, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!registerResult.IsSuccess || registerResult.Value is var createdId && createdId.IsEmpty)
        {
            return KernelErrorEndpoints.Problem(registerResult.Error!);
        }

        var persisted = await directory.FindStudyAsync(tenant, registerResult.Value, cancellationToken).ConfigureAwait(false)
            ?? new StudyDirectoryEntry(
                registerResult.Value,
                1,
                studyCode,
                title,
                phase,
                sponsor.OrganizationId,
                sponsor.VersionIndex,
                sponsor.Code,
                sponsor.Name,
                0);

        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(persisted.StudyId)}",
            KernelStudySummaryResponse.From(persisted));
    }

    internal static Ok<IReadOnlyList<KernelProtocolTemplateResponse>> ListProtocolTemplates(HttpContext context)
    {
        var importer = context.RequestServices.GetService<IProtocolDesignImporter>();
        if (importer is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelProtocolTemplateResponse>>([]);
        }

        return TypedResults.Ok<IReadOnlyList<KernelProtocolTemplateResponse>>(
            [.. importer.ListBundledTemplates().Select(KernelProtocolTemplateResponse.From)]);
    }

    internal static async Task<Results<Ok<KernelProtocolDraftSummaryResponse>, ProblemHttpResult>> GetProtocolDesignSummaryAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(versionRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(versionRef);
        }

        if (node.Kind != NodeKinds.ProtocolVersion)
        {
            return KernelProblems.WrongKind(node, NodeKinds.ProtocolVersion);
        }

        var tenant = KernelRequestTenant.Get(context);
        var versionId = ProtocolVersionId.FromGuid(node.Id);
        if (await reader.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelProblems.NotFound(node);
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [NodeRef.For(version.Id)], cancellationToken).ConfigureAwait(false);
        if (masks.For(NodeRef.For(version.Id)) is { } mask)
        {
            return KernelProblems.NotVisible($"You can't see this protocol version ({mask}), so its design summary is refused.");
        }

        var design = await reader.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        var labels = await ResolveConceptLabelsAsync(
            tenant,
            design,
            context.RequestServices.GetService<IConceptRegistry>(),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(KernelProtocolDraftSummaryResponse.From(version, design, labels));
    }

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
            return PayloadTooLargeProblem();
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var tenant = KernelRequestTenant.Get(context);
        var studyLookup = await ResolveTargetStudyAsync(tenant, studyRef, directory, cancellationToken).ConfigureAwait(false);
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

        var parsedPayload = await ReadImportInputAsync(context, cancellationToken).ConfigureAwait(false);
        if (parsedPayload.Problem is { } payloadProblem)
        {
            return payloadProblem;
        }

        var existingVersions = await versionReader.ListForStudyAsync(tenant, study.StudyId, cancellationToken).ConfigureAwait(false);
        var derivedVersionId = ProtocolVersionId.FromGuid(DeriveGuid(tenant, "protocol-version", $"{study.StudyId}:{idempotencyKey}"));
        var plan = await PlanDraftMutationAsync(
            tenant,
            study,
            existingVersions,
            derivedVersionId,
            parsedPayload.ReplaceForms,
            versionReader,
            probe,
            catalog,
            cancellationToken).ConfigureAwait(false);
        if (plan.Problem is { } planProblem)
        {
            return planProblem;
        }

        KernelError? conceptAuthOrCommandError = null;
        var resolvedConceptsByKey = new Dictionary<string, ProtocolConceptResolution>(StringComparer.Ordinal);

        async ValueTask<ProtocolConceptResolution> ResolveOrDefineConceptAsync(
            ProtocolConceptCandidate candidate,
            CancellationToken ct)
        {
            if (resolvedConceptsByKey.TryGetValue(candidate.StableKey, out var cached))
            {
                return cached;
            }

            if (!string.IsNullOrWhiteSpace(candidate.BiomedicalConceptCode))
            {
                var foundBc = await concepts.FindBiomedicalConceptAsync(tenant, candidate.BiomedicalConceptCode, ct).ConfigureAwait(false);
                if (foundBc is not null && foundBc.Status != ConceptStatus.Deprecated)
                {
                    var hit = new ProtocolConceptResolution(foundBc.Concept, UsedFallbackSponsorConcept: false);
                    resolvedConceptsByKey[candidate.StableKey] = hit;
                    return hit;
                }
            }

            var usedFallback = !string.IsNullOrWhiteSpace(candidate.BiomedicalConceptCode);
            var conceptId = ConceptId.FromGuid(DeriveGuid(tenant, "import-concept", candidate.StableKey));
            var existingConcept = await concepts.GetCurrentAsync(tenant, conceptId, ct).ConfigureAwait(false);
            if (existingConcept is not null && existingConcept.Status != ConceptStatus.Deprecated)
            {
                var reused = new ProtocolConceptResolution(existingConcept.Concept, UsedFallbackSponsorConcept: usedFallback);
                resolvedConceptsByKey[candidate.StableKey] = reused;
                return reused;
            }

            var label = string.IsNullOrWhiteSpace(candidate.PreferredLabel) ? "Protocol Item" : candidate.PreferredLabel.Trim();
            var definition = string.IsNullOrWhiteSpace(candidate.Definition) ? label : candidate.Definition.Trim();
            var defined = await sender.SendAsync(
                new DefineConcept(
                    tenant,
                    conceptId,
                    ConceptOrigin.Sponsor,
                    ExternalCode: null,
                    candidate.Kind,
                    label,
                    definition,
                    $"import-concept:{conceptId}",
                    reason),
                cancellationToken: ct).ConfigureAwait(false);

            if (!defined.IsSuccess)
            {
                conceptAuthOrCommandError ??= defined.Error;
                return new ProtocolConceptResolution(null, UsedFallbackSponsorConcept: usedFallback, ErrorMessage: defined.Error?.Message);
            }

            var created = new ProtocolConceptResolution(defined.Value, UsedFallbackSponsorConcept: usedFallback);
            resolvedConceptsByKey[candidate.StableKey] = created;
            return created;
        }

        var importOutcome = await importer.ImportAsync(
            new ProtocolDesignImportRequest(
                RawBytes: parsedPayload.RawBytes,
                FileName: parsedPayload.FileName,
                Format: parsedPayload.Format,
                TemplateIds: parsedPayload.TemplateIds,
                BaseDesign: plan.BaseDesign,
                FallbackTitle: study.Title),
            ResolveOrDefineConceptAsync,
            cancellationToken).ConfigureAwait(false);

        if (importOutcome.PayloadTooLarge)
        {
            return PayloadTooLargeProblem(importOutcome.Problems.FirstOrDefault());
        }

        if (conceptAuthOrCommandError is { HttpStatus: StatusCodes.Status403Forbidden })
        {
            return KernelErrorEndpoints.Problem(conceptAuthOrCommandError);
        }

        if (!importOutcome.IsSuccess || importOutcome.Design is null)
        {
            return ValidationFailedProblem(importOutcome.Problems);
        }

        var serializedDesign = ProtocolDesignDocument.Write(importOutcome.Design);
        var persistOutcome = await ExecuteDraftMutationAsync(
            tenant,
            study.StudyId,
            plan,
            importOutcome.Design,
            serializedDesign,
            idempotencyKey,
            reason,
            sender,
            cancellationToken).ConfigureAwait(false);
        if (persistOutcome.Problem is { } persistProblem)
        {
            return persistProblem;
        }

        var persistedState = persistOutcome.State!;
        var persistedSummary = await versionReader.GetAsync(tenant, persistedState.ProtocolVersionId, cancellationToken).ConfigureAwait(false)
            ?? new ProtocolVersionSummary(
                persistedState.ProtocolVersionId,
                persistedState.StudyId,
                persistedState.Number,
                plan.ParentVersionId,
                persistedState.Status,
                persistedState.ContentHash,
                plan.AmendmentIntent,
                null,
                null);

        var conceptLabels = await ResolveConceptLabelsAsync(tenant, importOutcome.Design, concepts, cancellationToken).ConfigureAwait(false);
        var responseBody = KernelProtocolDraftSummaryResponse.From(persistedSummary, importOutcome.Design, conceptLabels);

        return plan.CreatedNewVersion
            ? TypedResults.Created(
                $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{NodeRef.For(persistedSummary.Id)}",
                responseBody)
            : TypedResults.Ok(responseBody);
    }

    private static async Task<(OrganizationDirectoryEntry? Sponsor, ProblemHttpResult? Problem)> ResolveOrRegisterSponsorAsync(
        TenantId tenant,
        KernelCreateStudyRequest request,
        string idempotencyKey,
        string reason,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SponsorRef))
        {
            if (!NodeRef.TryParse(request.SponsorRef.Trim(), out var orgNode))
            {
                return (null, KernelProblems.InvalidNodeRef(request.SponsorRef));
            }

            if (orgNode.Kind != NodeKinds.Organization)
            {
                return (null, KernelProblems.WrongKind(orgNode, NodeKinds.Organization));
            }

            var foundById = await directory.FindOrganizationAsync(tenant, OrganizationId.FromGuid(orgNode.Id), cancellationToken).ConfigureAwait(false);
            return foundById is not null
                ? (foundById, null)
                : (null, KernelErrorEndpoints.Problem(StudyErrors.SponsorUnknown));
        }

        var rawSponsorCode = (request.SponsorCode ?? string.Empty).Trim();
        var rawSponsorName = (request.SponsorName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawSponsorCode) && string.IsNullOrWhiteSpace(rawSponsorName))
        {
            return (null, TypedResults.Problem(
                title: "Sponsor required",
                detail: "Select an existing sponsor organization or provide a sponsor name/code.",
                statusCode: StatusCodes.Status422UnprocessableEntity));
        }

        var normalizedCode = !string.IsNullOrWhiteSpace(rawSponsorCode)
            ? NormalizeOrganizationCode(rawSponsorCode)
            : NormalizeOrganizationCode(rawSponsorName);
        var displayName = !string.IsNullOrWhiteSpace(rawSponsorName) ? rawSponsorName : rawSponsorCode;

        var existingByCode = await directory.FindOrganizationByCodeAsync(tenant, normalizedCode, cancellationToken).ConfigureAwait(false);
        if (existingByCode is not null)
        {
            return (existingByCode, null);
        }

        var derivedOrgId = OrganizationId.FromGuid(DeriveGuid(tenant, "sponsor-org", normalizedCode));
        var regOrg = await sender.SendAsync(
            new RegisterOrganization(
                tenant,
                derivedOrgId,
                normalizedCode,
                displayName,
                OrganizationKind.Sponsor,
                $"{idempotencyKey}:org:{normalizedCode}",
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!regOrg.IsSuccess)
        {
            var recheck = await directory.FindOrganizationByCodeAsync(tenant, normalizedCode, cancellationToken).ConfigureAwait(false);
            if (recheck is not null)
            {
                return (recheck, null);
            }

            return (null, KernelErrorEndpoints.Problem(regOrg.Error!));
        }

        var createdOrg = await directory.FindOrganizationAsync(tenant, regOrg.Value, cancellationToken).ConfigureAwait(false)
            ?? new OrganizationDirectoryEntry(regOrg.Value, 1, normalizedCode, displayName, OrganizationKind.Sponsor);
        return (createdOrg, null);
    }

    private static async Task<(StudyDirectoryEntry? Study, ProblemHttpResult? Problem)> ResolveTargetStudyAsync(
        TenantId tenant,
        string studyRef,
        IStudyDirectory directory,
        CancellationToken cancellationToken)
    {
        var trimmed = (studyRef ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return (null, KernelProblems.BadRequest("Invalid study reference", "Provide a study NodeRef or study code."));
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            if (!NodeRef.TryParse(trimmed, out var node))
            {
                return (null, KernelProblems.InvalidNodeRef(trimmed));
            }

            if (node.Kind != NodeKinds.Study)
            {
                return (null, KernelProblems.WrongKind(node, NodeKinds.Study));
            }

            var byId = await directory.FindStudyAsync(tenant, StudyId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false);
            return byId is not null ? (byId, null) : (null, KernelProblems.NotFound(node));
        }

        var byCode = await directory.FindStudyByCodeAsync(tenant, trimmed, cancellationToken).ConfigureAwait(false);
        return byCode is not null
            ? (byCode, null)
            : (null, TypedResults.Problem(
                title: "Study not found",
                detail: $"Study '{trimmed}' does not exist in your tenant.",
                statusCode: StatusCodes.Status404NotFound));
    }

    private static async Task<ParsedImportInput> ReadImportInputAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.HasFormContentType)
        {
            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(MultipartLimits, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return ParsedImportInput.Fail(PayloadTooLargeProblem());
            }
            catch (IOException ex)
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid multipart upload", ex.Message));
            }

            byte[]? rawBytes = null;
            string? fileName = null;
            if (form.Files.Count > 0)
            {
                var file = form.Files[0];
                fileName = file.FileName;
                if (file.Length > IProtocolDesignImporter.MaxPayloadBytes)
                {
                    return ParsedImportInput.Fail(PayloadTooLargeProblem());
                }

                await using var stream = file.OpenReadStream();
                var (bytes, exceeded) = await ReadBoundedBytesAsync(stream, IProtocolDesignImporter.MaxPayloadBytes, cancellationToken).ConfigureAwait(false);
                if (exceeded)
                {
                    return ParsedImportInput.Fail(PayloadTooLargeProblem());
                }

                rawBytes = bytes;
            }

            if (!TryParseImportFormat(form["format"].ToString(), out var format))
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest(
                    "Invalid format",
                    "Supported format values are 'auto', 'odm_xml', and 'usdm_json'."));
            }

            var templateIds = ParseTemplateIds(form["template_ids"]);
            var replaceForms = bool.TryParse(form["replace_forms"].ToString(), out var parsedReplace) && parsedReplace;
            return new ParsedImportInput(rawBytes, fileName, format, templateIds, replaceForms, null);
        }

        var (bodyBytes, bodyExceeded) = await ReadBoundedBytesAsync(
            context.Request.Body,
            IProtocolDesignImporter.MaxPayloadBytes,
            cancellationToken).ConfigureAwait(false);
        if (bodyExceeded)
        {
            return ParsedImportInput.Fail(PayloadTooLargeProblem());
        }

        if (bodyBytes.Length == 0)
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Empty import request",
                "Upload an ODM v1.3.2 XML or USDM v4 JSON file, or specify bundled template_ids."));
        }

        var jsonOptions = context.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions;
        KernelImportProtocolJsonRequest? jsonRequest;
        try
        {
            jsonRequest = JsonSerializer.Deserialize<KernelImportProtocolJsonRequest>(bodyBytes, jsonOptions);
        }
        catch (JsonException ex)
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid JSON request", ex.Message));
        }

        if (jsonRequest is null)
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid JSON request", "Request body could not be parsed."));
        }

        if (!TryParseImportFormat(jsonRequest.Format, out var jsonFormat))
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Invalid format",
                "Supported format values are 'auto', 'odm_xml', and 'usdm_json'."));
        }

        byte[]? contentBytes = null;
        if (!string.IsNullOrEmpty(jsonRequest.Content))
        {
            contentBytes = Encoding.UTF8.GetBytes(jsonRequest.Content);
            if (contentBytes.Length > IProtocolDesignImporter.MaxPayloadBytes)
            {
                return ParsedImportInput.Fail(PayloadTooLargeProblem());
            }
        }

        return new ParsedImportInput(
            contentBytes,
            jsonRequest.FileName,
            jsonFormat,
            jsonRequest.TemplateIds,
            jsonRequest.ReplaceForms,
            null);
    }

    private static async Task<DraftMutationPlan> PlanDraftMutationAsync(
        TenantId tenant,
        StudyDirectoryEntry study,
        IReadOnlyList<ProtocolVersionSummary> existingVersions,
        ProtocolVersionId derivedVersionId,
        bool replaceForms,
        IProtocolVersionReader versionReader,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        CancellationToken cancellationToken)
    {
        // Check if a previous request with this exact Idempotency-Key already created derivedVersionId.
        var matchingReplayVersion = existingVersions.FirstOrDefault(v => v.Id == derivedVersionId);
        if (matchingReplayVersion is not null)
        {
            if (matchingReplayVersion.ParentVersionId is null)
            {
                return new DraftMutationPlan(
                    DraftMutationKind.CreateFirstDraft,
                    derivedVersionId,
                    ParentVersionId: null,
                    BaseDesign: null,
                    CreatedNewVersion: true,
                    AmendmentIntent: null,
                    Problem: null);
            }

            var parentDesign = replaceForms
                ? null
                : await versionReader.GetDesignAsync(tenant, matchingReplayVersion.ParentVersionId.Value, cancellationToken).ConfigureAwait(false);
            return new DraftMutationPlan(
                DraftMutationKind.ForkAndReviseApproved,
                derivedVersionId,
                ParentVersionId: matchingReplayVersion.ParentVersionId,
                BaseDesign: parentDesign,
                CreatedNewVersion: true,
                AmendmentIntent: matchingReplayVersion.AmendmentIntent,
                Problem: null);
        }

        var latestActive = existingVersions.FirstOrDefault(v => v.Status != ProtocolVersionStatus.Withdrawn);
        if (latestActive is null && existingVersions.Count == 0)
        {
            if (!await IsPermittedAsync(tenant, typeof(CreateProtocolDraft), NodeRef.For(study.StudyId), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            return new DraftMutationPlan(
                DraftMutationKind.CreateFirstDraft,
                derivedVersionId,
                ParentVersionId: null,
                BaseDesign: null,
                CreatedNewVersion: true,
                AmendmentIntent: null,
                Problem: null);
        }

        var current = latestActive ?? existingVersions[0];
        switch (current.Status)
        {
            case ProtocolVersionStatus.Draft:
                {
                    if (!await IsPermittedAsync(tenant, typeof(ReviseProtocolDraft), NodeRef.For(current.Id), probe, catalog, cancellationToken).ConfigureAwait(false))
                    {
                        return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
                    }

                    var currentDesign = replaceForms
                        ? null
                        : await versionReader.GetDesignAsync(tenant, current.Id, cancellationToken).ConfigureAwait(false);
                    return new DraftMutationPlan(
                        DraftMutationKind.ReviseExistingDraft,
                        current.Id,
                        ParentVersionId: current.ParentVersionId,
                        BaseDesign: currentDesign,
                        CreatedNewVersion: false,
                        AmendmentIntent: current.AmendmentIntent,
                        Problem: null);
                }

            case ProtocolVersionStatus.Approved:
                {
                    if (!await IsPermittedAsync(tenant, typeof(ForkProtocolVersion), NodeRef.For(current.Id), probe, catalog, cancellationToken).ConfigureAwait(false))
                    {
                        return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
                    }

                    var approvedDesign = replaceForms
                        ? null
                        : await versionReader.GetDesignAsync(tenant, current.Id, cancellationToken).ConfigureAwait(false);
                    return new DraftMutationPlan(
                        DraftMutationKind.ForkAndReviseApproved,
                        derivedVersionId,
                        ParentVersionId: current.Id,
                        BaseDesign: approvedDesign,
                        CreatedNewVersion: true,
                        AmendmentIntent: "Protocol design import",
                        Problem: null);
                }

            default:
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(ProtocolErrors.InvalidTransition));
        }
    }

    private static async Task<(ProtocolVersionState? State, ProblemHttpResult? Problem)> ExecuteDraftMutationAsync(
        TenantId tenant,
        StudyId studyId,
        DraftMutationPlan plan,
        ProtocolDesign design,
        string serializedDesign,
        string idempotencyKey,
        string reason,
        IKernelCommandSender sender,
        CancellationToken cancellationToken)
    {
        switch (plan.Kind)
        {
            case DraftMutationKind.CreateFirstDraft:
                {
                    var created = await sender.SendAsync(
                        new CreateProtocolDraft(tenant, studyId, plan.TargetVersionId, serializedDesign, idempotencyKey, reason),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    return created.IsSuccess
                        ? (created.Value, null)
                        : (null, KernelErrorEndpoints.Problem(created.Error!));
                }

            case DraftMutationKind.ReviseExistingDraft:
                {
                    var revised = await sender.SendAsync(
                        new ReviseProtocolDraft(tenant, plan.TargetVersionId, serializedDesign, Amendment: null, idempotencyKey, reason),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    return revised.IsSuccess
                        ? (revised.Value, null)
                        : (null, KernelErrorEndpoints.Problem(revised.Error!));
                }

            case DraftMutationKind.ForkAndReviseApproved:
                {
                    var impacted = design.CollectionSpecs.Select(s => s.Id).DefaultIfEmpty("protocol_design").ToList();
                    var amendment = new ProtocolAmendment(
                        Intent: plan.AmendmentIntent ?? "Protocol design import",
                        Rationale: reason,
                        Classification: AmendmentClassification.NonSubstantial,
                        SummaryOfChanges: $"Imported {design.CollectionSpecs.Count} protocol form specification(s).",
                        ImpactedElements: impacted,
                        TransitionPolicy: TransitionPolicy.NewParticipantsOnly);

                    var forked = await sender.SendAsync(
                        new ForkProtocolVersion(tenant, plan.ParentVersionId!.Value, plan.TargetVersionId, amendment, $"{idempotencyKey}:fork", reason),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (!forked.IsSuccess)
                    {
                        return (null, KernelErrorEndpoints.Problem(forked.Error!));
                    }

                    var revised = await sender.SendAsync(
                        new ReviseProtocolDraft(tenant, plan.TargetVersionId, serializedDesign, amendment, $"{idempotencyKey}:revise", reason),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    return revised.IsSuccess
                        ? (revised.Value, null)
                        : (null, KernelErrorEndpoints.Problem(revised.Error!));
                }

            default:
                return (null, KernelErrorEndpoints.Problem(ProtocolErrors.InvalidTransition));
        }
    }

    private static async Task<IReadOnlyDictionary<ConceptRef, string>> ResolveConceptLabelsAsync(
        TenantId tenant,
        ProtocolDesign? design,
        IConceptRegistry? concepts,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<ConceptRef, string>();
        if (design is null || concepts is null)
        {
            return map;
        }

        var needed = design.CollectionSpecs
            .SelectMany(s => s.Items)
            .Where(i => string.IsNullOrWhiteSpace(i.Prompt))
            .Select(i => i.Concept)
            .Distinct()
            .ToList();

        foreach (var conceptRef in needed)
        {
            var resolved = await concepts.ResolveAsync(tenant, conceptRef, cancellationToken).ConfigureAwait(false);
            if (resolved is not null && !string.IsNullOrWhiteSpace(resolved.PreferredLabel))
            {
                map[conceptRef] = resolved.PreferredLabel;
            }
        }

        return map;
    }

    private static async ValueTask<bool> IsPermittedAsync(
        TenantId tenant,
        Type commandType,
        NodeRef resource,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (catalog.Find(commandType) is not { } entry)
        {
            return false;
        }

        var decision = await probe.CanIAsync(tenant, entry.Capability, resource, cancellationToken).ConfigureAwait(false);
        return decision.Permitted;
    }

    private static async Task<(byte[] Bytes, bool ExceededLimit)> ReadBoundedBytesAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                return ([], true);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return (buffer.ToArray(), false);
    }

    private static bool TryParseImportFormat(string? raw, out ProtocolImportFormat format)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            format = ProtocolImportFormat.Auto;
            return true;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "odm":
            case "odm_xml":
            case "odmxml":
            case "xml":
                format = ProtocolImportFormat.OdmXml;
                return true;

            case "usdm":
            case "usdm_json":
            case "usdmjson":
            case "json":
                format = ProtocolImportFormat.UsdmJson;
                return true;

            default:
                format = ProtocolImportFormat.Auto;
                return false;
        }
    }

    private static List<string>? ParseTemplateIds(Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var entry in values)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var trimmed = entry.Trim();
            if (trimmed.StartsWith('['))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                    if (parsed is not null)
                    {
                        list.AddRange(parsed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
                        continue;
                    }
                }
                catch (JsonException)
                {
                    // Fall through to comma-splitting.
                }
            }

            foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                list.Add(part);
            }
        }

        return list.Count > 0 ? list : null;
    }

    private static string NormalizeOrganizationCode(string input)
    {
        var sb = new StringBuilder(input.Length);
        bool prevHyphen = false;
        foreach (char ch in input.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
                prevHyphen = false;
            }
            else if (!prevHyphen && sb.Length > 0)
            {
                sb.Append('-');
                prevHyphen = true;
            }
        }

        var normalized = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(normalized) ? "SPONSOR" : normalized[..Math.Min(normalized.Length, 48)];
    }

    private static ProblemHttpResult PayloadTooLargeProblem(string? detail = null) =>
        TypedResults.Problem(
            title: "Payload too large",
            detail: detail ?? $"Uploaded payload exceeds the 10 MB ({IProtocolDesignImporter.MaxPayloadBytes:N0} bytes) synchronous import limit.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    private static ProblemHttpResult ValidationFailedProblem(IReadOnlyList<string> problems)
    {
        var detail = problems.Count > 0
            ? string.Join(" ", problems)
            : "The uploaded protocol definition failed structural validation.";
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = ProtocolErrors.ContentInvalid.Code,
            ["errors"] = problems.ToArray(),
        };
        return TypedResults.Problem(
            title: "Protocol import validation failed",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: extensions);
    }

    private static string IdempotencyKey(HttpContext context) =>
        context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();

    private static Guid DeriveGuid(TenantId tenant, string kind, string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-study-import:{tenant}:{kind}:{key}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private enum DraftMutationKind
    {
        CreateFirstDraft,
        ReviseExistingDraft,
        ForkAndReviseApproved,
    }

    private sealed record ParsedImportInput(
        byte[]? RawBytes,
        string? FileName,
        ProtocolImportFormat Format,
        IReadOnlyList<string>? TemplateIds,
        bool ReplaceForms,
        ProblemHttpResult? Problem)
    {
        public static ParsedImportInput Fail(ProblemHttpResult problem) =>
            new(null, null, ProtocolImportFormat.Auto, null, false, problem);
    }

    private sealed record DraftMutationPlan(
        DraftMutationKind Kind,
        ProtocolVersionId TargetVersionId,
        ProtocolVersionId? ParentVersionId,
        ProtocolDesign? BaseDesign,
        bool CreatedNewVersion,
        string? AmendmentIntent,
        ProblemHttpResult? Problem)
    {
        public static DraftMutationPlan Fail(ProblemHttpResult problem) =>
            new(DraftMutationKind.CreateFirstDraft, default, null, null, false, null, problem);
    }
}
