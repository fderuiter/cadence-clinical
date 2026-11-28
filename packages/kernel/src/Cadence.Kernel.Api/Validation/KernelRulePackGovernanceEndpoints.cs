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
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api.Validation;

internal static class KernelRulePackGovernanceEndpoints
{
    private static readonly Capability ApproveLibraryItemCapability = new("library_item.approve", VerbClass.Approve);
    private static readonly Capability RegisterStandardCapability = new("standard.register", VerbClass.Execute);
    private static readonly Capability ApproveStandardCapability = new("standard.approve", VerbClass.Approve);

    internal static async Task<Results<Ok<KernelStudyRulePacksResponse>, ProblemHttpResult>> ListStudyRulePacksAsync(
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var studyId = resolved.StudyId;
        var registry = context.RequestServices.GetService<IStandardsRegistry>();
        if (registry is null)
        {
            return TypedResults.Ok(new KernelStudyRulePacksResponse(NodeRef.For(studyId).ToString(), null, [], []));
        }

        var profile = await registry.FindStudyStandardsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var pinnedIds = (profile?.LibraryPins ?? []).ToHashSet();
        var items = await registry.ListLibraryItemsAsync(tenant, LibraryItemKind.RulePack, cancellationToken).ConfigureAwait(false);
        var packs = items.Select(item => KernelRulePackResponse.From(item, pinnedIds.Contains(item.Id))).ToList();
        var pinnedRulePackRefs = items
            .Where(item => pinnedIds.Contains(item.Id))
            .Select(item => NodeRef.For(item.Id).ToString())
            .ToList();

        return TypedResults.Ok(new KernelStudyRulePacksResponse(
            NodeRef.For(studyId).ToString(),
            profile is not null ? NodeRef.For(profile.Id).ToString() : null,
            pinnedRulePackRefs,
            packs));
    }

    internal static async Task<Results<Ok<KernelRulePackPreviewResponse>, ProblemHttpResult>> PreviewStudyRulePackAsync(
        string studyRef,
        string itemRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryParseLibraryItem(itemRef, out var itemId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{itemRef}' is not a library_item NodeRef (library_item:<uuid>).");
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var registry = context.RequestServices.GetService<IStandardsRegistry>();
        var validation = context.RequestServices.GetService<ICollectionValidationService>();
        if (registry is null || validation is null)
        {
            return TypedResults.Ok(new KernelRulePackPreviewResponse(NodeRef.For(itemId).ToString(), 0, 0, 0, []));
        }

        var item = await registry.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.Kind != LibraryItemKind.RulePack)
        {
            return KernelProblems.NotFound(NodeRef.For(itemId));
        }

        IReadOnlyList<CollectionInstanceRecord>? studyForms = null;
        IReadOnlySet<EnrollmentId>? unblindedEnrollments = null;
        var probe = context.RequestServices.GetService<IAuthorizationProbe>();
        var collections = context.RequestServices.GetService<ICollectionInstanceRecords>();
        if (probe is not null && collections is not null)
        {
            studyForms = await collections.ListForStudyAsync(tenant, resolved.StudyId, cancellationToken).ConfigureAwait(false);
            var enrollmentNodes = studyForms
                .Select(f => (EnrollmentId: f.EnrollmentId, Node: NodeRef.For(f.EnrollmentId)))
                .DistinctBy(x => x.EnrollmentId)
                .ToArray();
            var permitted = new HashSet<EnrollmentId>();
            if (enrollmentNodes.Length > 0)
            {
                var decisions = await probe.CanIManyAsync(
                    tenant,
                    KernelValidationFindingVisibility.EnrollmentUnblindCapability,
                    [.. enrollmentNodes.Select(x => x.Node)],
                    cancellationToken).ConfigureAwait(false);
                foreach (var (enrollmentId, node) in enrollmentNodes)
                {
                    if (decisions.TryGetValue(node, out var decision) && decision.Permitted)
                    {
                        permitted.Add(enrollmentId);
                    }
                }
            }

            unblindedEnrollments = permitted;
        }

        var asOf = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var preview = studyForms is not null
            ? await validation.PreviewRulePackAsync(
                tenant,
                resolved.StudyId,
                itemId,
                studyForms,
                asOf,
                actor.Actor,
                unblindedEnrollments,
                cancellationToken).ConfigureAwait(false)
            : await validation.PreviewRulePackAsync(
                tenant,
                resolved.StudyId,
                itemId,
                asOf,
                actor.Actor,
                unblindedEnrollments,
                cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new KernelRulePackPreviewResponse(
            NodeRef.For(itemId).ToString(),
            preview.FormsEvaluated,
            preview.RulesEvaluated,
            preview.Violations.Count,
            [.. preview.Violations.Select(KernelRulePackPreviewViolationResponse.From)]));
    }

    internal static async Task<Results<Ok<KernelRulePackResponse>, ProblemHttpResult>> ApproveLibraryItemAsync(
        string itemRef,
        KernelApproveLibraryItemRequest? request,
        HttpContext context,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this library item approval.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseLibraryItem(itemRef, out var itemId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{itemRef}' is not a library_item NodeRef (library_item:<uuid>).");
        }

        var idempotencyKey = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (context.RequestServices.GetService<ITenantEntitlementReader>() is { } entitlementReader)
        {
            var clock = context.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
            var entitlements = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            if (entitlements.IsReadOnly)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
            }

            if (!entitlements.ProductFamilies.Contains(ProductFamily.Blueprint))
            {
                return KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
            }
        }

        var existing = await registry.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(StandardsErrors.LibraryItemUnknown);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "approve-library-item-attest", idempotencyKey));

        var canApprove = await probe.CanIAsync(tenant, ApproveLibraryItemCapability, NodeRef.For(itemId), cancellationToken).ConfigureAwait(false);
        if (canApprove.Permitted)
        {
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(NodeKinds.LibraryItem, itemId.Value, existing.ContentHash),
                    new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                    KernelLockEndpoints.StepUpMethod,
                    $"{idempotencyKey}:attest",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return KernelErrorEndpoints.Problem(attestResult.Error!);
            }
        }

        var approveResult = await sender.SendAsync(
            new ApproveLibraryItem(tenant, itemId, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!approveResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(approveResult.Error!);
        }

        if (existing.Kind == LibraryItemKind.RulePack)
        {
            await EnsureBaselineStandardApprovedAsync(
                tenant,
                registry,
                sender,
                probe,
                new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                idempotencyKey,
                reason,
                cancellationToken).ConfigureAwait(false);
        }

        var updated = await registry.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false) ?? existing;
        return TypedResults.Ok(KernelRulePackResponse.From(updated, pinned: false));
    }

    internal static async Task<Results<Ok<KernelStudyRulePacksResponse>, ProblemHttpResult>> DeclareStudyRulePacksAsync(
        string studyRef,
        KernelDeclareStudyRulePacksRequest? request,
        HttpContext context,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request?.RulePackRefs is null)
        {
            return KernelProblems.BadRequest("Invalid standards declaration", "Provide rule_pack_refs as an array of library_item NodeRefs.");
        }

        var idempotencyKey = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var requestedRulePacks = new List<LibraryItemId>();
        foreach (var rawRef in request.RulePackRefs)
        {
            if (!TryParseLibraryItem(rawRef, out var itemId))
            {
                return KernelProblems.BadRequest("Invalid NodeRef", $"'{rawRef}' is not a library_item NodeRef (library_item:<uuid>).");
            }

            requestedRulePacks.Add(itemId);
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var studyId = resolved.StudyId;
        var currentProfile = await registry.FindStudyStandardsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var profileId = currentProfile?.Id
            ?? StudyStandardsProfileId.FromGuid(DeriveGuid(tenant, "study-standards-profile", studyId.Value.ToString("D")));

        var preservedPins = new List<LibraryItemId>();
        if (currentProfile is not null)
        {
            foreach (var pinId in currentProfile.LibraryPins)
            {
                var pinnedItem = await registry.FindLibraryItemAsync(tenant, pinId, cancellationToken).ConfigureAwait(false);
                if (pinnedItem is not null && pinnedItem.Kind != LibraryItemKind.RulePack)
                {
                    preservedPins.Add(pinId);
                }
            }
        }

        IReadOnlyList<StandardVersionId> standardVersions = currentProfile?.StandardVersions ?? [];
        if (standardVersions.Count == 0)
        {
            var allStandards = await registry.ListStandardVersionsAsync(tenant, cancellationToken).ConfigureAwait(false);
            standardVersions = [.. allStandards
                .Where(s => s.Status == GovernanceStatus.Approved)
                .GroupBy(s => s.StandardCode, StringComparer.Ordinal)
                .Select(g => g.Last().Id)];
        }

        var mergedPins = preservedPins.Concat(requestedRulePacks).Distinct().ToList();
        var declareResult = await sender.SendAsync(
            new DeclareStudyStandards(
                tenant,
                studyId,
                profileId,
                standardVersions,
                currentProfile?.ControlledTerminology ?? [],
                mergedPins,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!declareResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(declareResult.Error!);
        }

        return await ListStudyRulePacksAsync(studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveVisibleStudyAsync(
        TenantId tenant,
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var (study, problem) = await KernelStudyLookup.ResolveVisibleStudyAsync(
            tenant,
            studyRef,
            context,
            visibility,
            actor,
            static (studyNode, mask) => $"You may not access study {studyNode} ({mask}).",
            allowVersionedStudyRef: true,
            cancellationToken).ConfigureAwait(false);
        return (study.StudyId, problem);
    }

    internal static Guid DeriveGuid(TenantId tenant, string kind, string seed)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-validation:{tenant}:{kind}:{seed}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private static bool TryParseLibraryItem(string text, out LibraryItemId itemId)
    {
        itemId = default;
        if (!NodeRef.TryParse(text, out var node) || node.Kind != NodeKinds.LibraryItem)
        {
            return false;
        }

        itemId = LibraryItemId.FromGuid(node.Id);
        return !itemId.IsEmpty;
    }

    private static async Task EnsureBaselineStandardApprovedAsync(
        TenantId tenant,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        DateTimeOffset authenticatedAt,
        string idempotencyKey,
        string reason,
        CancellationToken cancellationToken)
    {
        var existingStandards = await registry.ListStandardVersionsAsync(tenant, cancellationToken).ConfigureAwait(false);
        if (existingStandards.Any(s => s.Status == GovernanceStatus.Approved))
        {
            return;
        }

        var package = Cadence.Standards.StandardSeeds.Load(Cadence.Standards.StandardSeeds.Names[0]).Package;
        var summary = existingStandards.FirstOrDefault(s =>
            string.Equals(s.StandardCode, package.StandardCode, StringComparison.Ordinal)
            && string.Equals(s.Version, package.Version, StringComparison.Ordinal));

        if (summary is null)
        {
            var canRegister = await probe.CanIAsync(tenant, RegisterStandardCapability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false);
            if (!canRegister.Permitted)
            {
                return;
            }

            var standardId = StandardVersionId.FromGuid(DeriveGuid(tenant, "baseline-standard", $"{package.StandardCode}:{package.Version}"));
            var regResult = await sender.SendAsync(
                new RegisterStandardVersion(tenant, standardId, package, null, $"{idempotencyKey}:std-register", reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!regResult.IsSuccess && regResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return;
            }

            summary = await registry.FindStandardVersionAsync(tenant, package.StandardCode, package.Version, cancellationToken).ConfigureAwait(false);
        }

        if (summary is not null && summary.Status == GovernanceStatus.Draft)
        {
            var canApprove = await probe.CanIAsync(tenant, ApproveStandardCapability, NodeRef.For(summary.Id), cancellationToken).ConfigureAwait(false);
            if (!canApprove.Permitted)
            {
                return;
            }

            var attestId = AttestationId.FromGuid(DeriveGuid(tenant, "baseline-standard-attest", $"{idempotencyKey}:{summary.Id.Value:D}"));
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(NodeKinds.StandardVersion, summary.Id.Value, summary.ContentHash),
                    authenticatedAt,
                    KernelLockEndpoints.StepUpMethod,
                    $"{idempotencyKey}:std-attest",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return;
            }

            await sender.SendAsync(
                new ApproveStandardVersion(tenant, summary.Id, attestId, $"{idempotencyKey}:std-approve", reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
