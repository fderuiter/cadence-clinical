using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

internal static class KernelProtocolGovernanceHelpers
{
    internal static (string IdempotencyKey, string Reason, ProblemHttpResult? Problem) ReadMutationHeaders(
        HttpContext context,
        IKernelChangeReason changeReason)
    {
        var idempotencyKey = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired));
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired));
        }

        return (idempotencyKey, reason, null);
    }

    internal static async Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveVisibleStudyAsync(
        TenantId tenant,
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveStudyIdAsync(tenant, studyRef, context, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is not null)
        {
            return resolved;
        }

        var studyNode = NodeRef.For(resolved.StudyId);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [studyNode], cancellationToken).ConfigureAwait(false);
        if (masks.For(studyNode) is not null)
        {
            return (StudyId.Empty, TypedResults.Problem(
                title: "Forbidden",
                detail: "You do not have access to this study.",
                statusCode: StatusCodes.Status403Forbidden));
        }

        return (resolved.StudyId, null);
    }

    internal static async Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveStudyIdAsync(
        TenantId tenant,
        string studyRef,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studyRef))
        {
            return (StudyId.Empty, KernelProblems.BadRequest("Invalid study reference", "Provide a study NodeRef (study:<uuid>) or study code."));
        }

        var trimmed = studyRef.Trim();
        if (NodeRef.TryParse(trimmed, out var parsed) && parsed is not null)
        {
            if (!parsed.TryGetId<StudyId>(out var parsedStudyId))
            {
                return (StudyId.Empty, KernelProblems.BadRequest("Invalid NodeRef", $"'{studyRef}' is a {parsed.Kind}, not a study."));
            }

            return (parsedStudyId, null);
        }

        if (Guid.TryParse(trimmed, out var rawGuid) && rawGuid != Guid.Empty)
        {
            return (StudyId.FromGuid(rawGuid), null);
        }

        var directory = context.RequestServices.GetService<IStudyDirectory>();
        if (directory is not null)
        {
            var found = await directory.FindStudyByCodeAsync(tenant, trimmed, cancellationToken).ConfigureAwait(false);
            if (found is not null)
            {
                return (found.StudyId, null);
            }
        }

        return (StudyId.Empty, TypedResults.Problem(
            title: "Not found",
            detail: $"Study '{trimmed}' does not exist in your tenant.",
            statusCode: StatusCodes.Status404NotFound));
    }

    internal static bool TryParseProtocolVersion(string raw, out ProtocolVersionId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = ProtocolVersionId.FromGuid(guid);
            return true;
        }

        id = ProtocolVersionId.Empty;
        return false;
    }

    internal static bool TryParseEffectivity(string raw, out EffectivityId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = EffectivityId.FromGuid(guid);
            return true;
        }

        id = EffectivityId.Empty;
        return false;
    }

    internal static bool TryParseArtifactVersion(string raw, out ArtifactVersionId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = ArtifactVersionId.FromGuid(guid);
            return true;
        }

        id = ArtifactVersionId.Empty;
        return false;
    }

    internal static bool TryParseStudySite(string raw, out StudySiteId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = StudySiteId.FromGuid(guid);
            return true;
        }

        id = StudySiteId.Empty;
        return false;
    }

    internal static bool TryParseAttestation(string raw, out AttestationId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = AttestationId.FromGuid(guid);
            return true;
        }

        id = AttestationId.Empty;
        return false;
    }

    internal static string NormalizeSha256(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return $"sha256:{trimmed["sha256:".Length..].ToLowerInvariant()}";
        }

        return trimmed.Length == 64 && trimmed.All(Uri.IsHexDigit)
            ? $"sha256:{trimmed.ToLowerInvariant()}"
            : trimmed;
    }

    internal static Guid DeriveGuid(TenantId tenant, string kind, string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-governance:{tenant}:{kind}:{key}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
