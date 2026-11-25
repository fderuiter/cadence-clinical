using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

/// <summary>
/// Resolves caller-supplied study codes or <see cref="NodeRef"/> values in Kernel read endpoints while enforcing
/// study-level caller visibility (<see cref="INodeVisibility"/>).
/// </summary>
internal static class KernelStudyLookup
{
    internal readonly record struct ResolvedStudy(StudyId StudyId, NodeRef UnversionedRef, NodeRef PinnedRef)
    {
        internal string? StudyCode { get; init; }
    }

    internal readonly record struct ResolvedScope(NodeRef ScopeRef, NodeRef PinnedRef);

    internal static async Task<(ResolvedStudy Study, ProblemHttpResult? Problem)> ResolveVisibleStudyAsync(
        TenantId tenant,
        string? studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        Func<NodeRef, MaskReason, string> hiddenDetail,
        bool allowVersionedStudyRef,
        CancellationToken cancellationToken)
    {
        var trimmed = (studyRef ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return (default, KernelProblems.BadRequest("Invalid study reference", "Provide a study NodeRef or study code."));
        }

        StudyId studyId;
        NodeRef pinnedRef;
        string? resolvedStudyCode;
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            if (!NodeRef.TryParse(trimmed, out var node))
            {
                return (default, KernelProblems.InvalidNodeRef(trimmed));
            }

            if (node.Kind != NodeKinds.Study || (!allowVersionedStudyRef && node.Version is not null))
            {
                return (default, KernelProblems.WrongKind(node, NodeKinds.Study));
            }

            studyId = StudyId.FromGuid(node.Id);
            var unversionedLookup = NodeRef.For(studyId);
            var navigator = context.RequestServices.GetService<IKernelNavigator>();
            var navSummary = navigator is not null
                ? await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false)
                : null;
            if (navSummary is not null)
            {
                pinnedRef = navSummary.Ref;
                resolvedStudyCode = navSummary.Code ?? navSummary.Title;
            }
            else
            {
                var directory = context.RequestServices.GetService<IStudyDirectory>();
                var byId = directory is not null
                    ? await directory.FindStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false)
                    : null;
                if (byId is null)
                {
                    return (default, KernelProblems.NotFound(unversionedLookup));
                }

                pinnedRef = NodeRef.For(studyId, byId.VersionIndex);
                resolvedStudyCode = byId.StudyCode;
            }
        }
        else
        {
            if (trimmed.Length > SubjectCode.MaxLength || trimmed.Any(char.IsControl))
            {
                return (default, KernelProblems.BadRequest("Invalid study reference", "Provide a valid study NodeRef or study code."));
            }

            var directory = context.RequestServices.GetService<IStudyDirectory>();
            var byCode = directory is not null
                ? await directory.FindStudyByCodeAsync(tenant, trimmed, cancellationToken).ConfigureAwait(false)
                : null;
            if (byCode is null)
            {
                return (default, TypedResults.Problem(
                    title: "Study not found",
                    detail: $"Study '{trimmed}' does not exist in your tenant.",
                    statusCode: StatusCodes.Status404NotFound));
            }

            studyId = byCode.StudyId;
            pinnedRef = NodeRef.For(studyId, byCode.VersionIndex);
            resolvedStudyCode = byCode.StudyCode;
        }

        var unversionedRef = NodeRef.For(studyId);
        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            [pinnedRef, unversionedRef],
            cancellationToken).ConfigureAwait(false);
        if ((masks.For(pinnedRef) ?? masks.For(unversionedRef)) is { } mask)
        {
            return (default, KernelProblems.NotVisible(hiddenDetail(unversionedRef, mask)));
        }

        return (new ResolvedStudy(studyId, unversionedRef, pinnedRef) { StudyCode = resolvedStudyCode }, null);
    }

    internal static ProblemHttpResult? ValidateScopeOrStudyCodeSyntax(string scopeOrStudyCode)
    {
        var trimmed = (scopeOrStudyCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return KernelProblems.InvalidNodeRef(scopeOrStudyCode ?? string.Empty);
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            return NodeRef.TryParse(trimmed, out _) ? null : KernelProblems.InvalidNodeRef(trimmed);
        }

        return trimmed.Length > SubjectCode.MaxLength || trimmed.Any(char.IsControl)
            ? KernelProblems.BadRequest("Invalid scope", "Provide a valid NodeRef or study code.")
            : null;
    }

    internal static async Task<(ResolvedScope Scope, ProblemHttpResult? Problem)> ResolveScopeOrStudyCodeAsync(
        TenantId tenant,
        string scopeOrStudyCode,
        HttpContext context,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        var trimmed = (scopeOrStudyCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return (default, KernelProblems.BadRequest("Invalid scope", "Provide a NodeRef or study code."));
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            if (!NodeRef.TryParse(trimmed, out var parsed))
            {
                return (default, KernelProblems.InvalidNodeRef(trimmed));
            }

            if (await navigator.ResolveAsync(tenant, parsed, cancellationToken).ConfigureAwait(false) is not { } summary)
            {
                return (default, KernelProblems.NotFound(parsed));
            }

            return (new ResolvedScope(parsed, summary.Ref), null);
        }

        if (trimmed.Length > SubjectCode.MaxLength || trimmed.Any(char.IsControl))
        {
            return (default, KernelProblems.BadRequest("Invalid scope", "Provide a valid NodeRef or study code."));
        }

        var directory = context.RequestServices.GetService<IStudyDirectory>();
        var byCode = directory is not null
            ? await directory.FindStudyByCodeAsync(tenant, trimmed, cancellationToken).ConfigureAwait(false)
            : null;
        if (byCode is null)
        {
            return (default, TypedResults.Problem(
                title: "Study not found",
                detail: $"Study '{trimmed}' does not exist in your tenant.",
                statusCode: StatusCodes.Status404NotFound));
        }

        return (new ResolvedScope(NodeRef.For(byCode.StudyId), NodeRef.For(byCode.StudyId, byCode.VersionIndex)), null);
    }
}
