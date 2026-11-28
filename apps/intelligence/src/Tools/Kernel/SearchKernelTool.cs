using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Validation;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

/// <summary>
/// <c>search_kernel</c>: finds objects by code or title. A hit the principal may not see is left out, because a masked
/// hit would confirm that a hidden object matched the text (ADR-2484).
/// </summary>
internal sealed class SearchKernelTool(IKernelSearch search, IAgentVisibility visibility, IServiceProvider? services = null)
    : AgentTool<SearchArguments, SearchView>(IntelligenceToolsJsonContext.Default.SearchArguments, IntelligenceToolsJsonContext.Default.SearchView, Schema)
{
    /// <summary>The most hits one agent search returns.</summary>
    public const int MaxLimit = 20;

    private const int MaxTextLength = 200;

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "text": { "type": "string", "minLength": 1, "maxLength": 200, "description": "Words to find; each matches as a prefix, and an exact code ranks first." },
            "kinds": { "type": "array", "items": { "type": "string" }, "description": "Only these NodeRef kinds: organization, site, study, study_site, enrollment, protocol_version, collection_instance, observation, form_query, validation_finding, qualification, artifact, proposal, concept, code_system, code_system_version, standard_version, library_item." },
            "limit": { "type": "integer", "minimum": 1, "maximum": 20, "default": 10 }
          },
          "required": ["text"],
          "additionalProperties": false
        }
        """;

    public override string Name => KernelToolNames.SearchKernel;

    public override string Description =>
        "Finds Cadence clinical and governance objects (studies, sites, study sites, enrollments, protocol versions, " +
        "collection instances, observations, form queries, validation findings, qualifications, artifacts, proposals, " +
        "organizations, concepts, code systems, terminology codes, standards, and library items) by code or title. " +
        "Returns NodeRefs you can open with get_node.";

    protected override string? Validate(SearchArguments input)
    {
        if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > MaxTextLength)
        {
            return $"text must be 1 to {MaxTextLength} characters.";
        }

        if (input.Limit is < 1 or > MaxLimit)
        {
            return $"limit must be between 1 and {MaxLimit}.";
        }

        return input.Kinds?.FirstOrDefault(k => !NodeKinds.IsRegistered(k)) is { } unknown ? $"'{unknown}' is not a NodeRef kind." : null;
    }

    protected override async ValueTask<ToolResult<SearchView>> ExecuteAsync(AgentToolContext context, SearchArguments input, CancellationToken cancellationToken)
    {
        var request = new SearchRequest(input.Text, input.Kinds is { Count: > 0 } kinds ? [.. kinds] : null, input.Limit);
        var hits = await search.SearchAsync(context.Principal.Tenant, request, cancellationToken).ConfigureAwait(false);
        var masks = await visibility.MaskAsync(context.Principal, [.. hits.Select(h => h.Ref)], cancellationToken).ConfigureAwait(false);
        var unmasked = hits.Where(h => !masks.ContainsKey(h.Ref)).ToList();

        List<SearchHit> shown;
        if (unmasked.Any(h => h.Ref.Kind == NodeKinds.ValidationFinding)
            && services?.GetService<IValidationFindings>() is { } findings)
        {
            shown = new List<SearchHit>(unmasked.Count);
            foreach (var hit in unmasked)
            {
                if (hit.Ref.Kind == NodeKinds.ValidationFinding)
                {
                    var finding = await findings.FindAsync(
                        context.Principal.Tenant,
                        new ValidationFindingId(hit.Ref.Id),
                        cancellationToken).ConfigureAwait(false);
                    if (finding is null
                        || !await ValidationFindingExplanations.IsFindingVisibleAsync(
                            context.Principal,
                            finding,
                            services,
                            visibility,
                            cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }
                }

                shown.Add(hit);
            }
        }
        else
        {
            shown = unmasked;
        }

        foreach (var hit in shown)
        {
            context.ReadSet.Record(hit.Ref);
        }

        return new SearchView([.. shown.Select(h => new SearchHitView(h.Ref.ToString(), h.Ref.Kind, h.Title, h.Code, h.Detail, h.ExactCode))]);
    }
}
