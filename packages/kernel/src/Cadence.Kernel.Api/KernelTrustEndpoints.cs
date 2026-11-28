using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

/// <summary>
/// Canonical Kernel trust, authority, delegation, and qualification HTTP endpoints (spec §9.3, ADR-2455, ADR-2459,
/// K-AUTH-1, K-AUTH-3, K-AUTH-5, K-AUTH-6, K-TIME-1). Exposes all 8 commands in <see cref="DefineRole"/>,
/// <see cref="IssueGrant"/>, <see cref="DelegateGrant"/>, <see cref="RevokeGrant"/>, <see cref="RecordQualification"/>,
/// <see cref="RevokeQualification"/>, <see cref="DefineQualificationRequirement"/>, and
/// <see cref="RetireQualificationRequirement"/> alongside bitemporal read routes.
/// </summary>
public static class KernelTrustEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int MaxIdempotencyKeyLength = 255;

    /// <summary>Maps Kernel authority and qualification command and bitemporal read routes.</summary>
    public static RouteGroupBuilder MapKernelTrust(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel trust");

        group.MapGet("/roles", ListRolesAsync)
            .WithName("KernelListRoles")
            .WithSummary("Lists canonical roles defined in the caller's tenant.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/roles", DefineRoleAsync)
            .WithName("KernelDefineRole")
            .WithSummary("Defines an immutable capability bundle role in the caller's tenant (role.define).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<DefineRole>();

        group.MapGet("/grants", ListGrantsAsync)
            .WithName("KernelListGrants")
            .WithSummary("Queries canonical grants and delegations with bitemporal valid_at and known_at evaluation (K-TIME-1).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/grants", IssueGrantAsync)
            .WithName("KernelIssueGrant")
            .WithSummary("Issues a canonical bitemporal grant to an actor within a scope (grant.issue).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<IssueGrant>();

        group.MapPost("/grants/{grantRef}/delegate", DelegateGrantAsync)
            .WithName("KernelDelegateGrant")
            .WithSummary("Delegates authority bounded by a parent grant carrying withGrantOption (grant.delegate).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<DelegateGrant>();

        group.MapPost("/grants/{grantRef}/revoke", RevokeGrantAsync)
            .WithName("KernelRevokeGrant")
            .WithSummary("Ends a grant or delegation's bitemporal validity at effective_at without erasing prior periods (grant.revoke).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RevokeGrant>();

        group.MapGet("/qualifications", ListQualificationsAsync)
            .WithName("KernelListQualifications")
            .WithSummary("Queries canonical qualifications with bitemporal valid_at and known_at evaluation (K-TIME-1).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/qualifications", RecordQualificationAsync)
            .WithName("KernelRecordQualification")
            .WithSummary("Records an artifact-backed bitemporal qualification for an actor (qualification.record).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RecordQualification>();

        group.MapPost("/qualifications/{qualificationRef}/revoke", RevokeQualificationAsync)
            .WithName("KernelRevokeQualification")
            .WithSummary("Ends a qualification's bitemporal validity at effective_at (qualification.revoke).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RevokeQualification>();

        group.MapGet("/qualification-requirements", ListQualificationRequirementsAsync)
            .WithName("KernelListQualificationRequirements")
            .WithSummary("Queries canonical qualification requirements with bitemporal valid_at and known_at evaluation (K-TIME-1).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/qualification-requirements", DefineQualificationRequirementAsync)
            .WithName("KernelDefineQualificationRequirement")
            .WithSummary("Defines a bitemporal qualification requirement gating a capability within a scope (qualification_requirement.define).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<DefineQualificationRequirement>();

        group.MapPost("/qualification-requirements/{requirementRef}/retire", RetireQualificationRequirementAsync)
            .WithName("KernelRetireQualificationRequirement")
            .WithSummary("Retires a qualification requirement at effective_at while preserving earlier periods (qualification_requirement.retire).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RetireQualificationRequirement>();

        return kernel;
    }

    internal static async Task<Ok<IReadOnlyList<KernelRoleResponse>>> ListRolesAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var reader = context.RequestServices.GetService<IKernelTrustReader>();
        if (reader is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelRoleResponse>>([]);
        }

        var rows = await reader.ListRolesAsync(tenant, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelRoleResponse>>([.. rows.Select(KernelRoleResponse.From)]);
    }

    internal static async Task<Results<Created<KernelRoleResponse>, ProblemHttpResult>> DefineRoleAsync(
        KernelDefineRoleRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid role definition", "Supply role code, name, and capabilities.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var roleId = request.RoleId is { IsEmpty: false } given
            ? given
            : RoleId.FromGuid(DeriveGuid(tenant, "define-role", idempotencyKey));

        var result = await sender.SendAsync(
            new DefineRole(
                tenant,
                roleId,
                (request.Code ?? string.Empty).Trim(),
                request.Name ?? string.Empty,
                request.Capabilities ?? [],
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = KernelRoleResponse.From(result.Value!);
        return TypedResults.Created($"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/roles/{response.Ref}", response);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelGrantResponse>>, ProblemHttpResult>> ListGrantsAsync(
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "site_code")] string? siteCode,
        [FromQuery(Name = "grantee_id")] string? granteeId,
        [FromQuery(Name = "role_code")] string? roleCode,
        [FromQuery(Name = "include_inactive")] bool includeInactive,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var reader = context.RequestServices.GetService<IKernelTrustReader>();
        if (reader is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelGrantResponse>>([]);
        }

        var scopeFilter = await ResolveGrantFilterIdsAsync(tenant, studyCode, siteCode, context, cancellationToken).ConfigureAwait(false);
        if (scopeFilter.NotFound)
        {
            return TypedResults.Ok<IReadOnlyList<KernelGrantResponse>>([]);
        }

        var now = clock.UtcNow;
        var rows = await reader.ListGrantsAsync(
            tenant,
            new KernelGrantQuery(
                validAt ?? now,
                knownAt ?? now,
                scopeFilter.StudyId,
                scopeFilter.SiteId,
                scopeFilter.StudySiteId,
                granteeId,
                roleCode,
                includeInactive),
            cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<KernelGrantResponse>>([]);
        }

        if (IsTrustedTrustReader(actor.Actor))
        {
            return TypedResults.Ok<IReadOnlyList<KernelGrantResponse>>([.. rows.Select(KernelGrantResponse.From)]);
        }

        var grantRefs = rows.Select(r => NodeRef.For(r.GrantId)).ToList();
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, grantRefs, cancellationToken).ConfigureAwait(false);
        var visible = rows
            .Where(r => masks.For(NodeRef.For(r.GrantId)) is null
                || (actor.Actor is { } caller && string.Equals(caller.Id, r.GranteeId, StringComparison.Ordinal)))
            .Select(KernelGrantResponse.From)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KernelGrantResponse>>(visible);
    }

    internal static async Task<Results<Created<KernelGrantResponse>, ProblemHttpResult>> IssueGrantAsync(
        KernelIssueGrantRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid grant request", "Supply grantee_id, scope, and capability or role.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (!TryParseActor(request.GranteeId, request.GranteeKind, out var grantee))
        {
            return KernelErrorEndpoints.Problem(AuthorityErrors.GrantTargetInvalid);
        }

        var resolvedRole = await ResolveRoleAsync(tenant, request.RoleId, request.RoleCode, context, cancellationToken).ConfigureAwait(false);
        if (resolvedRole.Error is { } roleError)
        {
            return KernelErrorEndpoints.Problem(roleError);
        }

        var resolvedScope = await ResolveScopeAsync(
            tenant,
            request.ScopeLevel,
            request.ScopeTargetRef,
            request.ScopeTargetId,
            request.ScopeCountry,
            request.StudyCode,
            request.SiteCode,
            context,
            cancellationToken).ConfigureAwait(false);
        if (resolvedScope.Error is { } scopeError)
        {
            return KernelErrorEndpoints.Problem(scopeError);
        }

        var grantId = request.GrantId is { IsEmpty: false } given
            ? given
            : GrantId.FromGuid(DeriveGuid(tenant, "issue-grant", idempotencyKey));
        var now = clock.UtcNow;

        var result = await sender.SendAsync(
            new IssueGrant(
                tenant,
                grantId,
                grantee,
                string.IsNullOrWhiteSpace(request.Capability) ? null : request.Capability.Trim(),
                resolvedRole.RoleId,
                resolvedScope.Scope!,
                request.ValidFrom ?? now,
                request.ValidTo,
                request.WithGrantOption,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = await HydrateGrantResponseAsync(tenant, result.Value!, resolvedScope, resolvedRole, context, now, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/grants/{response.Ref}", response);
    }

    internal static async Task<Results<Created<KernelGrantResponse>, ProblemHttpResult>> DelegateGrantAsync(
        string grantRef,
        KernelDelegateGrantRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryParseGrantId(grantRef, out var parentGrantId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{grantRef}' is not a grant NodeRef (grant:<uuid>) or UUID.");
        }

        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid delegation request", "Supply grantee_id, scope, and capability or role.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (!TryParseActor(request.GranteeId, request.GranteeKind, out var grantee))
        {
            return KernelErrorEndpoints.Problem(AuthorityErrors.GrantTargetInvalid);
        }

        var resolvedRole = await ResolveRoleAsync(tenant, request.RoleId, request.RoleCode, context, cancellationToken).ConfigureAwait(false);
        if (resolvedRole.Error is { } roleError)
        {
            return KernelErrorEndpoints.Problem(roleError);
        }

        var resolvedScope = await ResolveScopeAsync(
            tenant,
            request.ScopeLevel,
            request.ScopeTargetRef,
            request.ScopeTargetId,
            request.ScopeCountry,
            request.StudyCode,
            request.SiteCode,
            context,
            cancellationToken).ConfigureAwait(false);
        if (resolvedScope.Error is { } scopeError)
        {
            return KernelErrorEndpoints.Problem(scopeError);
        }

        var grantId = request.GrantId is { IsEmpty: false } given
            ? given
            : GrantId.FromGuid(DeriveGuid(tenant, $"delegate-grant:{parentGrantId.Value:D}", idempotencyKey));
        var now = clock.UtcNow;

        var result = await sender.SendAsync(
            new DelegateGrant(
                tenant,
                grantId,
                parentGrantId,
                grantee,
                string.IsNullOrWhiteSpace(request.Capability) ? null : request.Capability.Trim(),
                resolvedRole.RoleId,
                resolvedScope.Scope!,
                request.ValidFrom ?? now,
                request.ValidTo,
                request.WithGrantOption,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = await HydrateGrantResponseAsync(tenant, result.Value!, resolvedScope, resolvedRole, context, now, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/grants/{response.Ref}", response);
    }

    internal static async Task<Results<Ok<KernelGrantResponse>, ProblemHttpResult>> RevokeGrantAsync(
        string grantRef,
        KernelRevokeGrantRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryParseGrantId(grantRef, out var grantId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{grantRef}' is not a grant NodeRef (grant:<uuid>) or UUID.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var effectiveAt = request?.EffectiveAt ?? clock.UtcNow;
        var result = await sender.SendAsync(
            new RevokeGrant(tenant, grantId, effectiveAt, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = await HydrateGrantResponseAsync(tenant, result.Value!, default, default, context, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(response);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelQualificationResponse>>, ProblemHttpResult>> ListQualificationsAsync(
        [FromQuery(Name = "holder_id")] string? holderId,
        [FromQuery(Name = "code")] string? code,
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "include_inactive")] bool includeInactive,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var reader = context.RequestServices.GetService<IKernelTrustReader>();
        if (reader is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelQualificationResponse>>([]);
        }

        StudyId? studyId = null;
        if (!string.IsNullOrWhiteSpace(studyCode))
        {
            var directory = context.RequestServices.GetService<IStudyDirectory>();
            var study = directory is not null
                ? await directory.FindStudyByCodeAsync(tenant, studyCode.Trim(), cancellationToken).ConfigureAwait(false)
                : null;
            if (study is null)
            {
                return TypedResults.Ok<IReadOnlyList<KernelQualificationResponse>>([]);
            }

            studyId = study.StudyId;
        }

        var now = clock.UtcNow;
        var rows = await reader.ListQualificationsAsync(
            tenant,
            new KernelQualificationQuery(validAt ?? now, knownAt ?? now, holderId, code, studyId, null, includeInactive),
            cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<KernelQualificationResponse>>([]);
        }

        if (IsTrustedTrustReader(actor.Actor))
        {
            return TypedResults.Ok<IReadOnlyList<KernelQualificationResponse>>([.. rows.Select(KernelQualificationResponse.From)]);
        }

        var qualRefs = rows.Select(r => NodeRef.For(r.QualificationId)).ToList();
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, qualRefs, cancellationToken).ConfigureAwait(false);
        var visible = rows
            .Where(r => masks.For(NodeRef.For(r.QualificationId)) is null
                || (actor.Actor is { } caller && string.Equals(caller.Id, r.HolderId, StringComparison.Ordinal)))
            .Select(KernelQualificationResponse.From)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KernelQualificationResponse>>(visible);
    }

    internal static async Task<Results<Created<KernelQualificationResponse>, ProblemHttpResult>> RecordQualificationAsync(
        KernelRecordQualificationRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid qualification request", "Supply holder_id, kind, code, and evidence_ref.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (!TryParseActor(request.HolderId, request.HolderKind, out var holder))
        {
            return KernelErrorEndpoints.Problem(QualificationErrors.CodeInvalid);
        }

        var kind = QualificationKind.Training;
        if (!string.IsNullOrWhiteSpace(request.Kind)
            && (!Enum.TryParse<QualificationKind>(request.Kind.Trim(), ignoreCase: true, out kind) || !Enum.IsDefined(kind)))
        {
            return KernelErrorEndpoints.Problem(QualificationErrors.CodeInvalid);
        }

        if (!TryParseArtifactVersionId(request.EvidenceRef, request.EvidenceArtifactVersionId, out var evidenceVersionId))
        {
            return KernelErrorEndpoints.Problem(QualificationErrors.EvidenceInvalid);
        }

        var resolvedVersion = await ResolveProtocolVersionAsync(
            tenant,
            kind == QualificationKind.ProtocolTraining,
            request.ProtocolVersionRef,
            request.ProtocolVersionId,
            request.StudyCode,
            request.ProtocolVersionNumber,
            context,
            cancellationToken).ConfigureAwait(false);
        if (resolvedVersion.Error is { } versionError)
        {
            return KernelErrorEndpoints.Problem(versionError);
        }

        var qualificationId = request.QualificationId is { IsEmpty: false } given
            ? given
            : QualificationId.FromGuid(DeriveGuid(tenant, "record-qualification", idempotencyKey));
        var now = clock.UtcNow;

        var result = await sender.SendAsync(
            new RecordQualification(
                tenant,
                qualificationId,
                holder,
                kind,
                (request.Code ?? string.Empty).Trim(),
                resolvedVersion.ProtocolVersionId,
                evidenceVersionId,
                request.ValidFrom ?? now,
                request.ValidTo,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = KernelQualificationResponse.From(
            result.Value!,
            resolvedVersion.VersionNumber,
            resolvedVersion.StudyId,
            resolvedVersion.StudyCode,
            context.RequestServices.GetService<ICurrentActor>()?.Actor?.ToString(),
            now);
        return TypedResults.Created($"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/qualifications/{response.Ref}", response);
    }

    internal static async Task<Results<Ok<KernelQualificationResponse>, ProblemHttpResult>> RevokeQualificationAsync(
        string qualificationRef,
        KernelRevokeQualificationRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryParseQualificationId(qualificationRef, out var qualificationId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{qualificationRef}' is not a qualification NodeRef (qualification:<uuid>) or UUID.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var effectiveAt = request?.EffectiveAt ?? clock.UtcNow;
        var result = await sender.SendAsync(
            new RevokeQualification(tenant, qualificationId, effectiveAt, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = KernelQualificationResponse.From(result.Value!, recordedAt: clock.UtcNow);
        return TypedResults.Ok(response);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelQualificationRequirementResponse>>, ProblemHttpResult>> ListQualificationRequirementsAsync(
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "capability")] string? capability,
        [FromQuery(Name = "qualification_code")] string? qualificationCode,
        [FromQuery(Name = "include_inactive")] bool includeInactive,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var reader = context.RequestServices.GetService<IKernelTrustReader>();
        if (reader is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelQualificationRequirementResponse>>([]);
        }

        StudyId? studyId = null;
        if (!string.IsNullOrWhiteSpace(studyCode))
        {
            var directory = context.RequestServices.GetService<IStudyDirectory>();
            var study = directory is not null
                ? await directory.FindStudyByCodeAsync(tenant, studyCode.Trim(), cancellationToken).ConfigureAwait(false)
                : null;
            if (study is null)
            {
                return TypedResults.Ok<IReadOnlyList<KernelQualificationRequirementResponse>>([]);
            }

            studyId = study.StudyId;
        }

        var now = clock.UtcNow;
        var rows = await reader.ListQualificationRequirementsAsync(
            tenant,
            new KernelQualificationRequirementQuery(validAt ?? now, knownAt ?? now, studyId, capability, qualificationCode, includeInactive),
            cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<KernelQualificationRequirementResponse>>([]);
        }

        if (IsTrustedTrustReader(actor.Actor))
        {
            return TypedResults.Ok<IReadOnlyList<KernelQualificationRequirementResponse>>([.. rows.Select(KernelQualificationRequirementResponse.From)]);
        }

        var reqRefs = rows.Select(r => NodeRef.For(r.RequirementId)).ToList();
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, reqRefs, cancellationToken).ConfigureAwait(false);
        var visible = rows
            .Where(r => masks.For(NodeRef.For(r.RequirementId)) is null)
            .Select(KernelQualificationRequirementResponse.From)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<KernelQualificationRequirementResponse>>(visible);
    }

    internal static async Task<Results<Created<KernelQualificationRequirementResponse>, ProblemHttpResult>> DefineQualificationRequirementAsync(
        KernelDefineQualificationRequirementRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid qualification requirement request", "Supply capability, qualification_code, and scope.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolvedScope = await ResolveScopeAsync(
            tenant,
            request.ScopeLevel,
            request.ScopeTargetRef,
            request.ScopeTargetId,
            request.ScopeCountry,
            request.StudyCode,
            request.SiteCode,
            context,
            cancellationToken).ConfigureAwait(false);
        if (resolvedScope.Error is { } scopeError)
        {
            return KernelErrorEndpoints.Problem(scopeError);
        }

        var resolvedVersion = await ResolveProtocolVersionAsync(
            tenant,
            requireProtocolVersion: false,
            request.ProtocolVersionRef,
            request.ProtocolVersionId,
            null,
            null,
            context,
            cancellationToken).ConfigureAwait(false);
        if (resolvedVersion.Error is { } versionError)
        {
            return KernelErrorEndpoints.Problem(versionError);
        }

        var requirementId = request.RequirementId is { IsEmpty: false } given
            ? given
            : QualificationRequirementId.FromGuid(DeriveGuid(tenant, "define-qualification-requirement", idempotencyKey));
        var now = clock.UtcNow;

        var result = await sender.SendAsync(
            new DefineQualificationRequirement(
                tenant,
                requirementId,
                (request.Capability ?? string.Empty).Trim(),
                (request.QualificationCode ?? string.Empty).Trim(),
                resolvedVersion.ProtocolVersionId,
                resolvedScope.Scope!,
                request.ValidFrom ?? now,
                request.ValidTo,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = KernelQualificationRequirementResponse.From(
            result.Value!,
            resolvedVersion.VersionNumber,
            resolvedScope.StudyId ?? resolvedVersion.StudyId,
            resolvedScope.StudyCode ?? resolvedVersion.StudyCode,
            context.RequestServices.GetService<ICurrentActor>()?.Actor?.ToString(),
            now);
        return TypedResults.Created($"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/qualification-requirements/{response.Ref}", response);
    }

    internal static async Task<Results<Ok<KernelQualificationRequirementResponse>, ProblemHttpResult>> RetireQualificationRequirementAsync(
        string requirementRef,
        KernelRetireQualificationRequirementRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryParseRequirementId(requirementRef, out var requirementId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{requirementRef}' is not a qualification_requirement NodeRef (qualification_requirement:<uuid>) or UUID.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var effectiveAt = request?.EffectiveAt ?? clock.UtcNow;
        var result = await sender.SendAsync(
            new RetireQualificationRequirement(tenant, requirementId, effectiveAt, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = KernelQualificationRequirementResponse.From(result.Value!, recordedAt: clock.UtcNow);
        return TypedResults.Ok(response);
    }

    private static bool IsTrustedTrustReader(Actor? actor) =>
        actor == TrustOperators.KnowledgeTrainingOperator
        || actor == TrustOperators.CtmsDelegationReader;

    private static (string Key, string Reason, ProblemHttpResult? Problem) ReadMutationHeaders(
        HttpContext context,
        IKernelChangeReason changeReason)
    {
        var key = context.Request.Headers[IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxIdempotencyKeyLength)
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired));
        }

        if (changeReason.Read(context) is not { Length: > 0 } reason)
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired));
        }

        return (key, reason, null);
    }

    private static bool TryParseActor(string? id, string? kindRaw, out Actor actor)
    {
        actor = default!;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var trimmed = id.Trim();
        if (trimmed.StartsWith("person:", StringComparison.Ordinal))
        {
            var raw = trimmed["person:".Length..];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            actor = Actor.Person(raw);
            return true;
        }

        if (trimmed.StartsWith("svc:", StringComparison.Ordinal))
        {
            var raw = trimmed["svc:".Length..];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            actor = Actor.ServiceAccount(raw);
            return true;
        }

        var kind = ActorKind.Person;
        if (!string.IsNullOrWhiteSpace(kindRaw)
            && (!Enum.TryParse(kindRaw.Trim(), ignoreCase: true, out kind) || !Enum.IsDefined(kind) || kind == ActorKind.AutomationAgent))
        {
            return false;
        }

        actor = new Actor(kind, trimmed);
        return true;
    }

    private static async Task<(RoleId? RoleId, string? RoleCode, string? RoleName, IReadOnlyList<string>? Capabilities, KernelError? Error)> ResolveRoleAsync(
        TenantId tenant,
        RoleId? roleId,
        string? roleCode,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (roleId is { IsEmpty: false } explicitRoleId)
        {
            var reader = context.RequestServices.GetService<IKernelTrustReader>();
            if (reader is not null)
            {
                var allRoles = await reader.ListRolesAsync(tenant, cancellationToken).ConfigureAwait(false);
                var matched = allRoles.FirstOrDefault(r => r.RoleId == explicitRoleId);
                return (explicitRoleId, matched?.Code, matched?.Name, matched?.Capabilities, null);
            }

            return (explicitRoleId, null, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(roleCode))
        {
            return (null, null, null, null, null);
        }

        var trustReader = context.RequestServices.GetService<IKernelTrustReader>();
        if (trustReader is null)
        {
            return (null, null, null, null, AuthorityErrors.RoleUnknown);
        }

        var found = await trustReader.FindRoleByCodeAsync(tenant, roleCode.Trim(), cancellationToken).ConfigureAwait(false);
        return found is null
            ? (null, null, null, null, AuthorityErrors.RoleUnknown)
            : (found.RoleId, found.Code, found.Name, found.Capabilities, null);
    }

    private readonly record struct ResolvedGrantScope(
        AuthorityScope? Scope,
        StudyId? StudyId,
        string? StudyCode,
        SiteId? SiteId,
        string? SiteCode,
        StudySiteId? StudySiteId,
        KernelError? Error);

    private static async Task<ResolvedGrantScope> ResolveScopeAsync(
        TenantId tenant,
        string? scopeLevelRaw,
        string? scopeTargetRef,
        Guid? scopeTargetId,
        string? scopeCountry,
        string? studyCode,
        string? siteCode,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(studyCode) || !string.IsNullOrWhiteSpace(siteCode))
        {
            var directory = context.RequestServices.GetService<IStudyDirectory>();
            if (directory is null)
            {
                return new ResolvedGrantScope(null, null, null, null, null, null, AuthorityErrors.ScopeUnknown);
            }

            StudyDirectoryEntry? study = null;
            if (!string.IsNullOrWhiteSpace(studyCode))
            {
                study = await directory.FindStudyByCodeAsync(tenant, studyCode.Trim(), cancellationToken).ConfigureAwait(false);
                if (study is null)
                {
                    return new ResolvedGrantScope(null, null, null, null, null, null, AuthorityErrors.ScopeUnknown);
                }
            }

            if (!string.IsNullOrWhiteSpace(siteCode))
            {
                var site = await directory.FindSiteByCodeAsync(tenant, siteCode.Trim(), cancellationToken: cancellationToken).ConfigureAwait(false);
                if (site is null)
                {
                    return new ResolvedGrantScope(null, null, null, null, null, null, AuthorityErrors.ScopeUnknown);
                }

                if (study is not null)
                {
                    var studySite = await directory.FindStudySiteBySiteAsync(tenant, study.StudyId, site.SiteId, cancellationToken).ConfigureAwait(false);
                    if (studySite is null)
                    {
                        return new ResolvedGrantScope(null, null, null, null, null, null, AuthorityErrors.ScopeUnknown);
                    }

                    return new ResolvedGrantScope(
                        AuthorityScope.StudySite(studySite.StudySiteId),
                        study.StudyId,
                        study.StudyCode,
                        site.SiteId,
                        site.SiteCode,
                        studySite.StudySiteId,
                        null);
                }

                var allStudies = await directory.ListStudiesAsync(tenant, cancellationToken).ConfigureAwait(false);
                foreach (var candidateStudy in allStudies)
                {
                    var linked = await directory.FindStudySiteBySiteAsync(tenant, candidateStudy.StudyId, site.SiteId, cancellationToken).ConfigureAwait(false);
                    if (linked is not null)
                    {
                        return new ResolvedGrantScope(
                            AuthorityScope.StudySite(linked.StudySiteId),
                            candidateStudy.StudyId,
                            candidateStudy.StudyCode,
                            site.SiteId,
                            site.SiteCode,
                            linked.StudySiteId,
                            null);
                    }
                }

                return new ResolvedGrantScope(null, null, null, null, null, null, AuthorityErrors.ScopeUnknown);
            }

            if (!string.IsNullOrWhiteSpace(scopeCountry))
            {
                return new ResolvedGrantScope(
                    AuthorityScope.StudyCountry(study!.StudyId, scopeCountry.Trim()),
                    study.StudyId,
                    study.StudyCode,
                    null,
                    null,
                    null,
                    null);
            }

            return new ResolvedGrantScope(
                AuthorityScope.Study(study!.StudyId),
                study.StudyId,
                study.StudyCode,
                null,
                null,
                null,
                null);
        }

        Guid? resolvedTargetId = scopeTargetId;
        ScopeLevel parsedLevel = ScopeLevel.Tenant;
        if (!string.IsNullOrWhiteSpace(scopeTargetRef))
        {
            if (!NodeRef.TryParse(scopeTargetRef.Trim(), out var targetNode))
            {
                return new ResolvedGrantScope(null, null, null, null, null, null, AuthorityErrors.ScopeInvalid);
            }

            resolvedTargetId = targetNode.Id;
            if (string.IsNullOrWhiteSpace(scopeLevelRaw))
            {
                parsedLevel = targetNode.Kind switch
                {
                    NodeKinds.Tenant => ScopeLevel.Tenant,
                    NodeKinds.Organization => ScopeLevel.Organization,
                    NodeKinds.Study => string.IsNullOrWhiteSpace(scopeCountry) ? ScopeLevel.Study : ScopeLevel.StudyCountry,
                    NodeKinds.StudySite => ScopeLevel.StudySite,
                    _ => (ScopeLevel)(-1),
                };
                if (!Enum.IsDefined(parsedLevel))
                {
                    return new ResolvedGrantScope(null, null, null, null, null, null, AuthorityErrors.ScopeInvalid);
                }

                if (parsedLevel == ScopeLevel.Tenant)
                {
                    resolvedTargetId = null;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(scopeLevelRaw)
            && (!Enum.TryParse(scopeLevelRaw.Trim(), ignoreCase: true, out parsedLevel) || !Enum.IsDefined(parsedLevel)))
        {
            return new ResolvedGrantScope(null, null, null, null, null, null, AuthorityErrors.ScopeInvalid);
        }

        var scope = new AuthorityScope(
            parsedLevel,
            parsedLevel == ScopeLevel.Tenant ? null : resolvedTargetId,
            string.IsNullOrWhiteSpace(scopeCountry) ? null : scopeCountry.Trim());

        return new ResolvedGrantScope(scope, null, null, null, null, null, null);
    }

    private static async Task<(StudyId? StudyId, SiteId? SiteId, StudySiteId? StudySiteId, bool NotFound)> ResolveGrantFilterIdsAsync(
        TenantId tenant,
        string? studyCode,
        string? siteCode,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studyCode) && string.IsNullOrWhiteSpace(siteCode))
        {
            return (null, null, null, false);
        }

        var directory = context.RequestServices.GetService<IStudyDirectory>();
        if (directory is null)
        {
            return (null, null, null, true);
        }

        StudyId? studyId = null;
        if (!string.IsNullOrWhiteSpace(studyCode))
        {
            var study = await directory.FindStudyByCodeAsync(tenant, studyCode.Trim(), cancellationToken).ConfigureAwait(false);
            if (study is null)
            {
                return (null, null, null, true);
            }

            studyId = study.StudyId;
        }

        SiteId? siteId = null;
        StudySiteId? studySiteId = null;
        if (!string.IsNullOrWhiteSpace(siteCode))
        {
            var site = await directory.FindSiteByCodeAsync(tenant, siteCode.Trim(), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (site is null)
            {
                return (null, null, null, true);
            }

            siteId = site.SiteId;
            if (studyId is { } sid)
            {
                var linked = await directory.FindStudySiteBySiteAsync(tenant, sid, site.SiteId, cancellationToken).ConfigureAwait(false);
                if (linked is null)
                {
                    return (null, null, null, true);
                }

                studySiteId = linked.StudySiteId;
            }
        }

        return (studyId, siteId, studySiteId, false);
    }

    private static async Task<(ProtocolVersionId? ProtocolVersionId, int? VersionNumber, StudyId? StudyId, string? StudyCode, KernelError? Error)> ResolveProtocolVersionAsync(
        TenantId tenant,
        bool requireProtocolVersion,
        string? protocolVersionRef,
        ProtocolVersionId? protocolVersionId,
        string? studyCode,
        int? versionNumber,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (protocolVersionId is { IsEmpty: false } explicitId)
        {
            var versions = context.RequestServices.GetService<IProtocolVersionReader>();
            var found = versions is not null ? await versions.GetAsync(tenant, explicitId, cancellationToken).ConfigureAwait(false) : null;
            return (explicitId, found?.Number, found?.StudyId, null, null);
        }

        if (!string.IsNullOrWhiteSpace(protocolVersionRef))
        {
            var trimmed = protocolVersionRef.Trim();
            if (NodeRef.TryParse(trimmed, out var node) && node.Kind == NodeKinds.ProtocolVersion && node.Version is null)
            {
                var pvId = ProtocolVersionId.FromGuid(node.Id);
                var versions = context.RequestServices.GetService<IProtocolVersionReader>();
                var found = versions is not null ? await versions.GetAsync(tenant, pvId, cancellationToken).ConfigureAwait(false) : null;
                return (pvId, found?.Number, found?.StudyId, null, null);
            }

            if (Guid.TryParse(trimmed, out var rawGuid) && rawGuid != Guid.Empty)
            {
                var pvId = ProtocolVersionId.FromGuid(rawGuid);
                var versions = context.RequestServices.GetService<IProtocolVersionReader>();
                var found = versions is not null ? await versions.GetAsync(tenant, pvId, cancellationToken).ConfigureAwait(false) : null;
                return (pvId, found?.Number, found?.StudyId, null, null);
            }

            return (null, null, null, null, QualificationErrors.ProtocolVersionUnknown);
        }

        if (requireProtocolVersion && !string.IsNullOrWhiteSpace(studyCode))
        {
            var directory = context.RequestServices.GetService<IStudyDirectory>();
            var versions = context.RequestServices.GetService<IProtocolVersionReader>();
            if (directory is null || versions is null)
            {
                return (null, null, null, null, QualificationErrors.ProtocolVersionUnknown);
            }

            var study = await directory.FindStudyByCodeAsync(tenant, studyCode.Trim(), cancellationToken).ConfigureAwait(false);
            if (study is null)
            {
                return (null, null, null, null, QualificationErrors.ProtocolVersionUnknown);
            }

            var studyVersions = await versions.ListForStudyAsync(tenant, study.StudyId, cancellationToken).ConfigureAwait(false);
            var matched = versionNumber is { } explicitNum
                ? studyVersions.FirstOrDefault(v => v.Number == explicitNum)
                : studyVersions.FirstOrDefault(v => v.Status == ProtocolVersionStatus.Approved) ?? studyVersions.FirstOrDefault();
            if (matched is null)
            {
                return (null, null, null, null, QualificationErrors.ProtocolVersionUnknown);
            }

            return (matched.Id, matched.Number, study.StudyId, study.StudyCode, null);
        }

        return (null, null, null, null, null);
    }

    private static async Task<KernelGrantResponse> HydrateGrantResponseAsync(
        TenantId tenant,
        GrantState state,
        ResolvedGrantScope resolvedScope,
        (RoleId? RoleId, string? RoleCode, string? RoleName, IReadOnlyList<string>? Capabilities, KernelError? Error) resolvedRole,
        HttpContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reader = context.RequestServices.GetService<IKernelTrustReader>();
        if (reader is not null)
        {
            var rows = await reader.ListGrantsAsync(
                tenant,
                new KernelGrantQuery(now, now, GranteeId: state.Grantee.Id, IncludeInactive: true),
                cancellationToken).ConfigureAwait(false);
            if (rows.FirstOrDefault(r => r.GrantId == state.GrantId) is { } hydrated)
            {
                return KernelGrantResponse.From(hydrated);
            }
        }

        return KernelGrantResponse.From(
            state,
            resolvedRole.RoleCode,
            resolvedRole.RoleName,
            resolvedRole.Capabilities,
            resolvedScope.StudyId,
            resolvedScope.StudyCode,
            resolvedScope.SiteId,
            resolvedScope.SiteCode,
            resolvedScope.StudySiteId,
            context.RequestServices.GetService<ICurrentActor>()?.Actor?.ToString(),
            now);
    }

    private static bool TryParseGrantId(string raw, out GrantId grantId)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (NodeRef.TryParse(trimmed, out var node) && node.Kind == NodeKinds.Grant && node.Version is null)
        {
            grantId = GrantId.FromGuid(node.Id);
            return true;
        }

        if (Guid.TryParse(trimmed, out var guid) && guid != Guid.Empty)
        {
            grantId = GrantId.FromGuid(guid);
            return true;
        }

        grantId = default;
        return false;
    }

    private static bool TryParseQualificationId(string raw, out QualificationId qualificationId)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (NodeRef.TryParse(trimmed, out var node) && node.Kind == NodeKinds.Qualification && node.Version is null)
        {
            qualificationId = QualificationId.FromGuid(node.Id);
            return true;
        }

        if (Guid.TryParse(trimmed, out var guid) && guid != Guid.Empty)
        {
            qualificationId = QualificationId.FromGuid(guid);
            return true;
        }

        qualificationId = default;
        return false;
    }

    private static bool TryParseRequirementId(string raw, out QualificationRequirementId requirementId)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (NodeRef.TryParse(trimmed, out var node) && node.Kind == NodeKinds.QualificationRequirement && node.Version is null)
        {
            requirementId = QualificationRequirementId.FromGuid(node.Id);
            return true;
        }

        if (Guid.TryParse(trimmed, out var guid) && guid != Guid.Empty)
        {
            requirementId = QualificationRequirementId.FromGuid(guid);
            return true;
        }

        requirementId = default;
        return false;
    }

    private static bool TryParseArtifactVersionId(string? evidenceRef, ArtifactVersionId? explicitId, out ArtifactVersionId versionId)
    {
        if (explicitId is { IsEmpty: false } given)
        {
            versionId = given;
            return true;
        }

        var trimmed = (evidenceRef ?? string.Empty).Trim();
        if (NodeRef.TryParse(trimmed, out var node) && node.Kind == NodeKinds.ArtifactVersion && node.Version is null)
        {
            versionId = ArtifactVersionId.FromGuid(node.Id);
            return true;
        }

        if (Guid.TryParse(trimmed, out var guid) && guid != Guid.Empty)
        {
            versionId = ArtifactVersionId.FromGuid(guid);
            return true;
        }

        versionId = default;
        return false;
    }

    private static Guid DeriveGuid(TenantId tenant, string purpose, string idempotencyKey)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-trust:{tenant.Value:D}:{purpose}:{idempotencyKey}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
