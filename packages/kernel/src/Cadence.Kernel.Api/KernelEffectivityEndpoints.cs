using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

/// <summary>
/// Kernel effectivity, site activation, and consent governance endpoints (spec §6.2, §7.4, §7.5, §9.4).
/// Every write runs through <see cref="IKernelCommandSender"/> with the signed caller identity,
/// <c>Idempotency-Key</c> header, and <see cref="IKernelChangeReason"/>.
/// </summary>
public static class KernelEffectivityEndpoints
{
    /// <summary>The header carrying the command's idempotency key (K-TX-3).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly Actor FormsServiceActor = Actor.ServiceAccount("execution-forms");
    private static readonly Actor EConsentConsumerActor = Actor.ServiceAccount("econsent-consumer");

    /// <summary>Maps effectivity, study-site activation, and consent endpoints on the kernel route group.</summary>
    public static RouteGroupBuilder MapKernelEffectivity(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var effectivities = kernel.MapGroup("/effectivities")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Kernel effectivity");

        effectivities.MapPost(string.Empty, DeclareAsync)
            .WithName("KernelDeclareEffectivity")
            .WithSummary("Declares an approved protocol version's effectivity at study, country, or study-site scope (effectivity.declare).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<DeclareEffectivity>();

        effectivities.MapPost("/{effectivityRef}/status", ChangeStatusAsync)
            .WithName("KernelChangeEffectivityStatus")
            .WithSummary("Transitions an effectivity record's status and evaluates TransitionPolicy for open enrollments (effectivity.change_status).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ChangeEffectivityStatus>();

        var studySites = kernel.MapGroup("/study-sites")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Kernel effectivity");

        studySites.MapPost("/{studySiteRef}/activate", ActivateStudySiteAsync)
            .WithName("KernelActivateStudySite")
            .WithSummary("Activates a study site for screening and enrollment once regulatory and greenlight prerequisites hold (study_site.activate).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ActivateStudySite>();

        studySites.MapPost("/activate", ActivateStudySiteByCodeAsync)
            .WithName("KernelActivateStudySiteByCode")
            .WithSummary("Idempotently activates a linked study site resolved by study code and site code (study_site.activate).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ActivateStudySite>();

        var enrollments = kernel.MapGroup("/enrollments")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Kernel effectivity");

        enrollments.MapPost("/{enrollmentRef}/consents", RecordConsentAsync)
            .WithName("KernelRecordConsentStatus")
            .WithSummary("Records bitemporal participant consent at SignedAt and resolves covered pending re-consent transitions (consent.record).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RecordConsentStatus>();

        var consents = kernel.MapGroup("/consents")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Kernel effectivity");

        consents.MapPost("/sync", SyncConsentAsync)
            .WithName("KernelSyncConsentStatus")
            .WithSummary("Synchronizes signed coordinator or electronic consent from Execution into Kernel bitemporal ConsentStatus and Applicability (consent.record).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RecordConsentStatus>();

        return kernel;
    }

    internal static async Task<Results<Created<KernelEffectivityStateResponse>, ProblemHttpResult>> DeclareAsync(
        KernelDeclareEffectivityRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        string? scopeError = null;
        if (request is null
            || !NodeRef.TryParse(request.ProtocolVersionRef, out var versionNode)
            || versionNode.Kind != NodeKinds.ProtocolVersion
            || !TryParseScope(request.ScopeKind, request.ScopeTargetRef, request.CountryCode, out var scope, out scopeError))
        {
            return KernelProblems.BadRequest(
                "Invalid effectivity declaration",
                scopeError ?? "Provide a protocol_version NodeRef and a valid scope (Study, Country with ISO-3166 alpha-2 code, or StudySite with study_site NodeRef).");
        }

        var tenant = KernelRequestTenant.Get(context);
        var effectivityId = request.EffectivityId is { } given && given != Guid.Empty
            ? EffectivityId.FromGuid(given)
            : EffectivityId.New();

        var result = await sender.SendAsync(
            new DeclareEffectivity(
                tenant,
                effectivityId,
                ProtocolVersionId.FromGuid(versionNode.Id),
                scope!,
                request.ValidFrom,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Value is { } state
            ? TypedResults.Created(
                $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(state.EffectivityId)}",
                KernelEffectivityStateResponse.From(state))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    internal static async Task<Results<Ok<KernelEffectivityStateResponse>, ProblemHttpResult>> ChangeStatusAsync(
        string effectivityRef,
        KernelChangeEffectivityStatusRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(effectivityRef, out var effectivityNode) || effectivityNode.Kind != NodeKinds.Effectivity)
        {
            return !NodeRef.TryParse(effectivityRef, out var wrong)
                ? KernelProblems.InvalidNodeRef(effectivityRef)
                : KernelProblems.WrongKind(wrong, NodeKinds.Effectivity);
        }

        if (request is null || !Enum.TryParse<EffectivityStatus>(request.TargetStatus?.Trim(), ignoreCase: true, out var targetStatus))
        {
            return KernelProblems.BadRequest(
                "Invalid effectivity status transition",
                "Target status must be Pending, Effective, Superseded, or Suspended.");
        }

        if (!TryParseArtifactVersions(request.EvidenceArtifactVersionRefs, out var evidenceIds, out var evidenceError))
        {
            return KernelProblems.BadRequest("Invalid evidence artifact reference", evidenceError!);
        }

        var result = await sender.SendAsync(
            new ChangeEffectivityStatus(
                KernelRequestTenant.Get(context),
                EffectivityId.FromGuid(effectivityNode.Id),
                targetStatus,
                request.EffectiveAt,
                evidenceIds,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Value is { } state
            ? TypedResults.Ok(KernelEffectivityStateResponse.From(state))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    internal static async Task<Results<Ok<KernelStudySiteStateResponse>, ProblemHttpResult>> ActivateStudySiteAsync(
        string studySiteRef,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(studySiteRef, out var siteNode) || siteNode.Kind != NodeKinds.StudySite)
        {
            return !NodeRef.TryParse(studySiteRef, out var wrong)
                ? KernelProblems.InvalidNodeRef(studySiteRef)
                : KernelProblems.WrongKind(wrong, NodeKinds.StudySite);
        }

        var tenant = KernelRequestTenant.Get(context);
        var studySiteId = StudySiteId.FromGuid(siteNode.Id);
        return await ActivateResolvedStudySiteAsync(
            tenant,
            studySiteId,
            context,
            enrollments,
            probe,
            catalog,
            sender,
            changeReason,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelStudySiteStateResponse>, ProblemHttpResult>> ActivateStudySiteByCodeAsync(
        KernelActivateStudySiteByCodeRequest request,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.StudyCode)
            || string.IsNullOrWhiteSpace(request.SiteCode))
        {
            return KernelProblems.BadRequest(
                "Invalid study-site activation request",
                "Name both study_code and site_code.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await enrollments.FindStudySiteInfoAsync(tenant, request.StudyCode.Trim(), request.SiteCode.Trim(), cancellationToken).ConfigureAwait(false)
            is not { } siteInfo)
        {
            return KernelErrorEndpoints.Problem(ExecutionErrors.StudySiteUnknown);
        }

        return await ActivateResolvedStudySiteAsync(
            tenant,
            siteInfo.StudySiteId,
            context,
            enrollments,
            probe,
            catalog,
            sender,
            changeReason,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Created<KernelConsentStatusStateResponse>, ProblemHttpResult>> RecordConsentAsync(
        string enrollmentRef,
        KernelRecordConsentStatusRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(enrollmentRef, out var enrollmentNode) || enrollmentNode.Kind != NodeKinds.Enrollment)
        {
            return !NodeRef.TryParse(enrollmentRef, out var wrong)
                ? KernelProblems.InvalidNodeRef(enrollmentRef)
                : KernelProblems.WrongKind(wrong, NodeKinds.Enrollment);
        }

        if (request is null
            || !NodeRef.TryParse(request.ProtocolVersionRef, out var versionNode)
            || versionNode.Kind != NodeKinds.ProtocolVersion
            || !Enum.TryParse<ConsentBasisKind>(request.BasisKind?.Trim(), ignoreCase: true, out var basis))
        {
            return KernelProblems.BadRequest(
                "Invalid consent status request",
                "Provide a protocol_version NodeRef and a valid basis_kind (ElectronicAttestation or PaperSourceDocument).");
        }

        ArtifactVersionId? artifactVersionId = null;
        if (!string.IsNullOrWhiteSpace(request.ArtifactVersionRef))
        {
            if (!TryParseSingleArtifactVersion(request.ArtifactVersionRef, out var parsedArtifact))
            {
                return KernelProblems.BadRequest("Invalid artifact version reference", "Expected artifact_version:<uuid> or a UUID.");
            }

            artifactVersionId = parsedArtifact;
        }

        AttestationId? attestationId = null;
        if (!string.IsNullOrWhiteSpace(request.AttestationRef))
        {
            if (NodeRef.TryParse(request.AttestationRef, out var attestationNode) && attestationNode.Kind == NodeKinds.Attestation)
            {
                attestationId = AttestationId.FromGuid(attestationNode.Id);
            }
            else if (Guid.TryParse(request.AttestationRef, out var rawAttestation) && rawAttestation != Guid.Empty)
            {
                attestationId = AttestationId.FromGuid(rawAttestation);
            }
            else
            {
                return KernelProblems.BadRequest("Invalid attestation reference", "Expected attestation:<uuid> or a UUID.");
            }
        }

        IReadOnlyList<ConsentScopeEntry> scopeEntries;
        if (request.Scopes is { Count: > 0 })
        {
            var list = new List<ConsentScopeEntry>(request.Scopes.Count);
            foreach (var entry in request.Scopes)
            {
                if (string.IsNullOrWhiteSpace(entry.Purpose)
                    || !Enum.TryParse<ConsentDecision>(entry.Decision?.Trim(), ignoreCase: true, out var entryDecision))
                {
                    return KernelProblems.BadRequest("Invalid consent scope entry", "Each scope entry requires a non-empty purpose and valid decision (Granted or Declined).");
                }

                list.Add(new ConsentScopeEntry(entry.Purpose.Trim(), entryDecision, entry.Conditions));
            }

            scopeEntries = list;
        }
        else
        {
            scopeEntries =
            [
                new ConsentScopeEntry(
                    ConsentPurposes.MainStudy,
                    request.Signed ? ConsentDecision.Granted : ConsentDecision.Declined),
            ];
        }

        var consentId = request.ConsentStatusId is { } given && given != Guid.Empty
            ? ConsentStatusId.FromGuid(given)
            : ConsentStatusId.New();

        var result = await sender.SendAsync(
            new RecordConsentStatus(
                KernelRequestTenant.Get(context),
                consentId,
                EnrollmentId.FromGuid(enrollmentNode.Id),
                ProtocolVersionId.FromGuid(versionNode.Id),
                request.Signed,
                request.SignedAt,
                basis,
                attestationId,
                artifactVersionId,
                scopeEntries,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Value is { } state
            ? TypedResults.Created(
                $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(state.ConsentStatusId)}",
                KernelConsentStatusStateResponse.From(state))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    internal static async Task<Results<Ok<KernelConsentStatusStateResponse>, ProblemHttpResult>> SyncConsentAsync(
        KernelSyncConsentStatusRequest request,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        IProtocolVersionReader protocolVersions,
        ICurrentActor currentActor,
        KernelCommandCatalog catalog,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.StudyCode)
            || !SubjectCode.TryFrom(request.SubjectCode?.Trim() ?? string.Empty, out var subjectCode))
        {
            return KernelProblems.BadRequest(
                "Invalid consent synchronization request",
                "Name a valid study_code and subject_code.");
        }

        var tenant = KernelRequestTenant.Get(context);
        StudyId studyId;
        StudySiteLookup? siteLookup = null;
        if (!string.IsNullOrWhiteSpace(request.SiteCode))
        {
            siteLookup = await enrollments.FindStudySiteInfoAsync(
                tenant, request.StudyCode.Trim(), request.SiteCode.Trim(), cancellationToken).ConfigureAwait(false);
            if (siteLookup is null)
            {
                return KernelErrorEndpoints.Problem(ExecutionErrors.StudySiteUnknown);
            }

            studyId = siteLookup.StudyId;
        }
        else if (await enrollments.FindStudyByCodeAsync(tenant, request.StudyCode.Trim(), cancellationToken).ConfigureAwait(false) is { } foundStudy)
        {
            studyId = foundStudy;
        }
        else
        {
            return KernelErrorEndpoints.Problem(ConsentErrors.ProtocolVersionUnknown);
        }

        ProtocolVersionId? resolvedVersionId = null;
        if (!string.IsNullOrWhiteSpace(request.ProtocolVersionRef)
            && NodeRef.TryParse(request.ProtocolVersionRef, out var explicitNode)
            && explicitNode.Kind == NodeKinds.ProtocolVersion)
        {
            resolvedVersionId = ProtocolVersionId.FromGuid(explicitNode.Id);
        }
        else
        {
            var versions = await protocolVersions.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
            var matched = versions.FirstOrDefault(v => v.Number == request.VersionIndex)
                ?? (TryExtractVersionNumberFromTag(request.VersionTag, out var tagNumber)
                    ? versions.FirstOrDefault(v => v.Number == tagNumber)
                    : null);
            if (matched is not null)
            {
                resolvedVersionId = matched.Id;
            }
        }

        if (resolvedVersionId is not { } protocolVersionId)
        {
            return KernelErrorEndpoints.Problem(ConsentErrors.ProtocolVersionUnknown);
        }

        string key = IdempotencyKey(context);
        string reason = changeReason.Read(context) ?? string.Empty;
        var serviceSender = await ResolveConsentSenderAsync(
            tenant,
            context,
            currentActor,
            catalog,
            scopes,
            cancellationToken).ConfigureAwait(false);

        var enrollment = await enrollments.ResolveParticipantAsync(tenant, studyId, subjectCode, cancellationToken).ConfigureAwait(false);
        if (enrollment is null && siteLookup is not null)
        {
            if (!string.Equals(siteLookup.Status, "Active", StringComparison.Ordinal))
            {
                return KernelErrorEndpoints.Problem(ExecutionErrors.StudySiteNotActive);
            }

            var participantId = ParticipantId.New();
            string participantKey = string.IsNullOrEmpty(key) ? string.Empty : $"{key}:participant";
            await using (var regScope = scopes.CreateAsyncScope())
            {
                regScope.ServiceProvider.GetRequiredService<ServiceActorScope>().Actor = FormsServiceActor;
                var regResult = await regScope.ServiceProvider.GetRequiredService<IKernelCommandSender>()
                    .SendAsync(new RegisterParticipant(tenant, participantId, participantKey, reason), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!regResult.IsSuccess && regResult.Error?.Code != KernelErrors.AlreadyExists.Code)
                {
                    return KernelErrorEndpoints.Problem(regResult.Error!);
                }
            }

            string screenKey = string.IsNullOrEmpty(key) ? string.Empty : $"{key}:screen";
            var screenResult = await SendWithResolvedSenderAsync(
                new ScreenParticipant(tenant, EnrollmentId.New(), participantId, siteLookup.StudySiteId, subjectCode, request.SignedAt, screenKey, reason),
                sender,
                serviceSender,
                scopes,
                cancellationToken).ConfigureAwait(false);
            if (screenResult.Value is { } created)
            {
                enrollment = created;
            }
            else if ((screenResult.Error?.Code == KernelErrors.AlreadyExists.Code
                    || screenResult.Error?.Code == ExecutionErrors.SubjectCodeTaken.Code)
                && await enrollments.ResolveParticipantAsync(tenant, studyId, subjectCode, cancellationToken).ConfigureAwait(false) is { } concurrent)
            {
                enrollment = concurrent;
            }
            else
            {
                return KernelErrorEndpoints.Problem(screenResult.Error!);
            }
        }

        if (enrollment is null)
        {
            return KernelErrorEndpoints.Problem(ObservationErrors.EnrollmentUnknown);
        }

        var consentCommand = new RecordConsentStatus(
            tenant,
            ConsentStatusId.New(),
            enrollment.EnrollmentId,
            protocolVersionId,
            Signed: true,
            request.SignedAt,
            request.IsPaperOverride ? ConsentBasisKind.PaperSourceDocument : ConsentBasisKind.ElectronicAttestation,
            AttestationId: null,
            ArtifactVersionId: null,
            [new ConsentScopeEntry(ConsentPurposes.MainStudy, ConsentDecision.Granted)],
            key,
            reason);

        var consentResult = await SendWithResolvedSenderAsync(
            consentCommand,
            sender,
            serviceSender,
            scopes,
            cancellationToken).ConfigureAwait(false);

        return consentResult.Value is { } consentState
            ? TypedResults.Ok(KernelConsentStatusStateResponse.From(consentState))
            : KernelErrorEndpoints.Problem(consentResult.Error!);
    }

    private static async Task<Actor?> ResolveConsentSenderAsync(
        TenantId tenant,
        HttpContext context,
        ICurrentActor currentActor,
        KernelCommandCatalog catalog,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        if (currentActor.Actor is not null)
        {
            return null;
        }

        string? signedSubject = context.User.FindFirst(CadenceClaimTypes.Subject)?.Value;
        if (!string.Equals(signedSubject, EConsentConsumerActor.Id, StringComparison.Ordinal)
            && !string.Equals(signedSubject, FormsServiceActor.Id, StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(signedSubject, EConsentConsumerActor.Id, StringComparison.Ordinal))
        {
            var entry = catalog.Find(typeof(RecordConsentStatus))!;
            await using var probeScope = scopes.CreateAsyncScope();
            probeScope.ServiceProvider.GetRequiredService<ServiceActorScope>().Actor = EConsentConsumerActor;
            var probe = probeScope.ServiceProvider.GetRequiredService<IAuthorizationProbe>();
            var decision = await probe.CanIAsync(tenant, entry.Capability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false);
            if (decision.Permitted)
            {
                return EConsentConsumerActor;
            }
        }

        return FormsServiceActor;
    }

    private static async Task<CommandResult<TResult>> SendWithResolvedSenderAsync<TResult>(
        ICommand<TResult> command,
        IKernelCommandSender requestSender,
        Actor? serviceActor,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        if (serviceActor is null)
        {
            return await requestSender.SendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await using var serviceScope = scopes.CreateAsyncScope();
        serviceScope.ServiceProvider.GetRequiredService<ServiceActorScope>().Actor = serviceActor;
        return await serviceScope.ServiceProvider.GetRequiredService<IKernelCommandSender>()
            .SendAsync(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Results<Ok<KernelStudySiteStateResponse>, ProblemHttpResult>> ActivateResolvedStudySiteAsync(
        TenantId tenant,
        StudySiteId studySiteId,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        string key = IdempotencyKey(context);
        string reason = changeReason.Read(context) ?? string.Empty;

        var entry = catalog.Find(typeof(ActivateStudySite))!;
        var decision = await probe.CanIAsync(tenant, entry.Capability, NodeRef.For(studySiteId), cancellationToken).ConfigureAwait(false);
        if (!decision.Permitted)
        {
            var denied = await sender.SendAsync(
                new ActivateStudySite(tenant, studySiteId, key, reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return KernelErrorEndpoints.Problem(denied.Error!);
        }

        if (await enrollments.FindStudySiteByIdAsync(tenant, studySiteId, cancellationToken).ConfigureAwait(false) is { Status: "Active" } existing)
        {
            return TypedResults.Ok(KernelStudySiteStateResponse.From(existing));
        }

        var result = await sender.SendAsync(
            new ActivateStudySite(tenant, studySiteId, key, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Value is { } state)
        {
            return TypedResults.Ok(KernelStudySiteStateResponse.From(state));
        }

        if (result.Error?.Code == EffectivityErrors.StudySiteAlreadyActive.Code
            && await enrollments.FindStudySiteByIdAsync(tenant, studySiteId, cancellationToken).ConfigureAwait(false) is { } concurrent)
        {
            return TypedResults.Ok(KernelStudySiteStateResponse.From(concurrent));
        }

        return KernelErrorEndpoints.Problem(result.Error!);
    }

    private static bool TryParseScope(
        string? scopeKind,
        string? scopeTargetRef,
        string? countryCode,
        out EffectivityScope? scope,
        out string? error)
    {
        scope = null;
        error = null;
        if (!Enum.TryParse<EffectivityLevel>(scopeKind?.Trim(), ignoreCase: true, out var level))
        {
            error = "scope_kind must be Study, Country, or StudySite.";
            return false;
        }

        switch (level)
        {
            case EffectivityLevel.Study:
                scope = EffectivityScope.Study();
                return true;

            case EffectivityLevel.Country:
                if (string.IsNullOrWhiteSpace(countryCode))
                {
                    error = "country_code (ISO-3166 alpha-2) is required when scope_kind is Country.";
                    return false;
                }

                var candidate = EffectivityScope.InCountry(countryCode.Trim().ToUpperInvariant());
                if (candidate.Problem() is { } problem)
                {
                    error = problem;
                    return false;
                }

                scope = candidate;
                return true;

            case EffectivityLevel.StudySite:
                if (!NodeRef.TryParse(scopeTargetRef ?? string.Empty, out var siteNode) || siteNode.Kind != NodeKinds.StudySite)
                {
                    error = "scope_target_ref must be a study_site NodeRef when scope_kind is StudySite.";
                    return false;
                }

                scope = EffectivityScope.At(StudySiteId.FromGuid(siteNode.Id));
                return true;

            default:
                error = "Unsupported scope_kind.";
                return false;
        }
    }

    private static bool TryExtractVersionNumberFromTag(string? versionTag, out int versionNumber)
    {
        versionNumber = 0;
        if (string.IsNullOrWhiteSpace(versionTag))
        {
            return false;
        }

        var trimmed = versionTag.Trim().TrimStart('v', 'V');
        var dot = trimmed.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            trimmed = trimmed[..dot];
        }

        return int.TryParse(trimmed, out versionNumber) && versionNumber >= 1;
    }

    private static bool TryParseArtifactVersions(
        IReadOnlyList<string>? refs,
        out IReadOnlyList<ArtifactVersionId> ids,
        out string? error)
    {
        error = null;
        if (refs is null || refs.Count == 0)
        {
            ids = [];
            return true;
        }

        var parsed = new List<ArtifactVersionId>(refs.Count);
        foreach (var raw in refs)
        {
            if (!TryParseSingleArtifactVersion(raw, out var id))
            {
                ids = [];
                error = $"'{raw}' is not an artifact_version NodeRef or UUID.";
                return false;
            }

            parsed.Add(id);
        }

        ids = parsed;
        return true;
    }

    private static bool TryParseSingleArtifactVersion(string? raw, out ArtifactVersionId id)
    {
        if (NodeRef.TryParse(raw ?? string.Empty, out var node) && node.Kind == NodeKinds.ArtifactVersion)
        {
            id = ArtifactVersionId.FromGuid(node.Id);
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = ArtifactVersionId.FromGuid(guid);
            return true;
        }

        id = default;
        return false;
    }

    private static string IdempotencyKey(HttpContext context) => context.Request.Headers[IdempotencyKeyHeader].ToString().Trim();
}

/// <summary>Body of <c>POST /api/v1/kernel/effectivities</c>.</summary>
/// <param name="EffectivityId">Optional effectivity id; assigned when absent.</param>
/// <param name="ProtocolVersionRef">The approved protocol version's NodeRef.</param>
/// <param name="ScopeKind"><c>Study</c>, <c>Country</c>, or <c>StudySite</c>.</param>
/// <param name="ScopeTargetRef">The <c>study_site</c> NodeRef when <paramref name="ScopeKind"/> is <c>StudySite</c>.</param>
/// <param name="CountryCode">The ISO-3166 alpha-2 country code when <paramref name="ScopeKind"/> is <c>Country</c>.</param>
/// <param name="ValidFrom">When the declaration takes effect in valid time.</param>
public sealed record KernelDeclareEffectivityRequest(
    Guid? EffectivityId,
    string ProtocolVersionRef,
    string ScopeKind,
    string? ScopeTargetRef,
    string? CountryCode,
    DateTimeOffset ValidFrom);

/// <summary>Body of <c>POST /api/v1/kernel/effectivities/{effectivityRef}/status</c>.</summary>
/// <param name="TargetStatus"><c>Pending</c>, <c>Effective</c>, <c>Superseded</c>, or <c>Suspended</c>.</param>
/// <param name="EffectiveAt">When the status takes effect in valid time.</param>
/// <param name="EvidenceArtifactVersionRefs">Registered ethics/regulatory <c>artifact_version</c> NodeRefs or UUIDs.</param>
public sealed record KernelChangeEffectivityStatusRequest(
    string TargetStatus,
    DateTimeOffset EffectiveAt,
    IReadOnlyList<string>? EvidenceArtifactVersionRefs = null);

/// <summary>Body of <c>POST /api/v1/kernel/study-sites/activate</c>.</summary>
/// <param name="StudyCode">The study code in the caller's tenant.</param>
/// <param name="SiteCode">The linked site code.</param>
public sealed record KernelActivateStudySiteByCodeRequest(
    string StudyCode,
    string SiteCode);

/// <summary>One consent purpose decision in <see cref="KernelRecordConsentStatusRequest"/>.</summary>
/// <param name="Purpose">The consent purpose token.</param>
/// <param name="Decision"><c>Granted</c> or <c>Declined</c>.</param>
/// <param name="Conditions">Optional conditions qualifying this scope entry.</param>
public sealed record KernelConsentScopeRequest(
    string Purpose,
    string Decision,
    string? Conditions = null);

/// <summary>Body of <c>POST /api/v1/kernel/enrollments/{enrollmentRef}/consents</c>.</summary>
/// <param name="ConsentStatusId">Optional consent status id; assigned when absent.</param>
/// <param name="ProtocolVersionRef">The protocol version covered by this consent record.</param>
/// <param name="Signed">True when consent was signed and granted; false when declined or unsigned.</param>
/// <param name="BasisKind"><c>ElectronicAttestation</c> or <c>PaperSourceDocument</c>.</param>
/// <param name="SignedAt">When the participant or representative signed in valid time.</param>
/// <param name="ArtifactVersionRef">Optional signed ICF <c>artifact_version</c> NodeRef.</param>
/// <param name="AttestationRef">Optional <c>attestation</c> NodeRef.</param>
/// <param name="Scopes">Optional granular purpose decisions; defaults to <c>MainStudy</c>.</param>
public sealed record KernelRecordConsentStatusRequest(
    Guid? ConsentStatusId,
    string ProtocolVersionRef,
    bool Signed,
    string BasisKind,
    DateTimeOffset SignedAt,
    string? ArtifactVersionRef = null,
    string? AttestationRef = null,
    IReadOnlyList<KernelConsentScopeRequest>? Scopes = null);

/// <summary>Body of <c>POST /api/v1/kernel/consents/sync</c>.</summary>
/// <param name="StudyCode">The study code in the caller's tenant.</param>
/// <param name="SiteCode">Optional site code when synchronizing from an auto-enrolling eConsent event.</param>
/// <param name="SubjectCode">The study-scoped subject code.</param>
/// <param name="ProtocolVersionRef">Optional explicit <c>protocol_version</c> NodeRef.</param>
/// <param name="VersionIndex">The protocol version number (1-indexed).</param>
/// <param name="VersionTag">Optional human version tag fallback.</param>
/// <param name="SignedAt">When consent was signed in valid time.</param>
/// <param name="IsPaperOverride">True when recorded from a verified paper ICF override.</param>
public sealed record KernelSyncConsentStatusRequest(
    string StudyCode,
    string? SiteCode,
    string SubjectCode,
    string? ProtocolVersionRef,
    int VersionIndex,
    string? VersionTag,
    DateTimeOffset SignedAt,
    bool IsPaperOverride = false);

/// <summary>A kernel effectivity record after declaration or status transition.</summary>
/// <param name="Ref">The effectivity's canonical NodeRef.</param>
/// <param name="EffectivityId">The effectivity id.</param>
/// <param name="ProtocolVersionRef">The governed protocol version's NodeRef.</param>
/// <param name="ScopeKind">The scope kind (<c>Study</c>, <c>Country</c>, or <c>StudySite</c>).</param>
/// <param name="CountryCode">The country code when <paramref name="ScopeKind"/> is <c>Country</c>.</param>
/// <param name="StudySiteRef">The <c>study_site</c> NodeRef when <paramref name="ScopeKind"/> is <c>StudySite</c>.</param>
/// <param name="Status">Current effectivity status.</param>
/// <param name="StatusSince">When the status took effect in valid time.</param>
/// <param name="EvidenceArtifactVersionRefs">Evidence <c>artifact_version</c> NodeRefs.</param>
public sealed record KernelEffectivityStateResponse(
    string Ref,
    Guid EffectivityId,
    string ProtocolVersionRef,
    string ScopeKind,
    string? CountryCode,
    string? StudySiteRef,
    string Status,
    DateTimeOffset StatusSince,
    IReadOnlyList<string> EvidenceArtifactVersionRefs)
{
    internal static KernelEffectivityStateResponse From(EffectivityState state) =>
        new(
            NodeRef.For(state.EffectivityId).ToString(),
            state.EffectivityId.Value,
            NodeRef.For(state.ProtocolVersionId).ToString(),
            state.Scope.Level.ToString(),
            state.Scope.Country,
            state.Scope.StudySiteId is { } siteId ? NodeRef.For(siteId).ToString() : null,
            state.Status.ToString(),
            state.StatusSince,
            [.. state.Evidence.Select(id => NodeRef.For(id).ToString())]);
}

/// <summary>A kernel study site after activation.</summary>
/// <param name="Ref">The study site's canonical NodeRef.</param>
/// <param name="StudySiteId">The study site id.</param>
/// <param name="Status">Operational status (<c>Active</c>).</param>
public sealed record KernelStudySiteStateResponse(
    string Ref,
    Guid StudySiteId,
    string Status)
{
    internal static KernelStudySiteStateResponse From(StudySiteState state) =>
        new(
            NodeRef.For(state.StudySiteId).ToString(),
            state.StudySiteId.Value,
            state.Status);

    internal static KernelStudySiteStateResponse From(StudySiteLookup lookup) =>
        new(
            NodeRef.For(lookup.StudySiteId).ToString(),
            lookup.StudySiteId.Value,
            lookup.Status);
}

/// <summary>A kernel consent status record after recording participant consent.</summary>
/// <param name="Ref">The consent status record's canonical NodeRef.</param>
/// <param name="ConsentStatusId">The consent status id.</param>
/// <param name="EnrollmentRef">The enrollment's canonical NodeRef.</param>
/// <param name="ProtocolVersionRef">The covered protocol version's NodeRef.</param>
/// <param name="ProtocolVersionNumber">The covered protocol version's 1-indexed number.</param>
/// <param name="Signed">Whether consent was signed.</param>
/// <param name="BasisKind">Basis of the consent record.</param>
/// <param name="SignedAt">When consent took effect in valid time.</param>
/// <param name="GoverningVersionRef">The enrollment's governing protocol version after recording consent, if any.</param>
/// <param name="RequiresReconsent">Whether any pending re-consent transition remains unresolved.</param>
public sealed record KernelConsentStatusStateResponse(
    string Ref,
    Guid ConsentStatusId,
    string EnrollmentRef,
    string ProtocolVersionRef,
    int ProtocolVersionNumber,
    bool Signed,
    string BasisKind,
    DateTimeOffset SignedAt,
    string? GoverningVersionRef,
    bool RequiresReconsent)
{
    internal static KernelConsentStatusStateResponse From(ConsentStatusState state) =>
        new(
            NodeRef.For(state.ConsentStatusId).ToString(),
            state.ConsentStatusId.Value,
            NodeRef.For(state.EnrollmentId).ToString(),
            NodeRef.For(state.ProtocolVersionId).ToString(),
            state.ProtocolVersionNumber,
            state.Signed,
            state.BasisKind.ToString(),
            state.SignedAt,
            state.Applicability?.GoverningVersionId is { } govId ? NodeRef.For(govId).ToString() : null,
            state.Applicability?.RequiresReconsent is true);
}
