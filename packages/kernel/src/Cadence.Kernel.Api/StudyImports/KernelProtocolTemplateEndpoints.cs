using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api.StudyImports;

internal static class KernelProtocolTemplateEndpoints
{
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
        var amendment = await reader.GetAmendmentAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        var labels = await ResolveConceptLabelsAsync(
            tenant,
            design,
            context.RequestServices.GetService<IConceptRegistry>(),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(KernelProtocolDraftSummaryResponse.From(version, design, labels, amendment: amendment));
    }

    internal static Task<IReadOnlyDictionary<ConceptRef, string>> ResolveConceptLabelsAsync(
        TenantId tenant,
        ProtocolDesign? design,
        IConceptRegistry? concepts,
        CancellationToken cancellationToken) =>
        ResolveConceptLabelsAsync(tenant, design, concepts, stagedLabels: null, cancellationToken);

    internal static async Task<IReadOnlyDictionary<ConceptRef, string>> ResolveConceptLabelsAsync(
        TenantId tenant,
        ProtocolDesign? design,
        IConceptRegistry? concepts,
        IReadOnlyDictionary<ConceptRef, string>? stagedLabels,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<ConceptRef, string>();
        if (design is null)
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
            if (stagedLabels is not null
                && stagedLabels.TryGetValue(conceptRef, out var stagedLabel)
                && !string.IsNullOrWhiteSpace(stagedLabel))
            {
                map[conceptRef] = stagedLabel;
                continue;
            }

            if (concepts is null)
            {
                continue;
            }

            var resolved = await concepts.ResolveAsync(tenant, conceptRef, cancellationToken).ConfigureAwait(false);
            if (resolved is not null && !string.IsNullOrWhiteSpace(resolved.PreferredLabel))
            {
                map[conceptRef] = resolved.PreferredLabel;
            }
        }

        return map;
    }
}
