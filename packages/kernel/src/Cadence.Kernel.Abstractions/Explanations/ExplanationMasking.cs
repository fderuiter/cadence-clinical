using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;

namespace Cadence.Kernel.Explanations;

/// <summary>
/// Resolves per-caller <see cref="MaskReason"/> entries for <see cref="NodeRef"/>s cited in an
/// <see cref="Explanation"/> (ADR-2471, ADR-2482, K-EXP-2). Implemented by the kernel application layer over
/// <see cref="INodeVisibility"/> and the request's accountable actor so module and kernel error filters can mask HTTP
/// problem responses without coupling <c>Cadence.Kernel.Modules</c> to <c>Cadence.Kernel.Application</c>.
/// </summary>
public interface IKernelExplanationMasker
{
    /// <summary>The nodes in <paramref name="nodes"/> that the current request's caller may not see, with their reason.</summary>
    ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken);
}

/// <summary>
/// Masks restricted or blinded <see cref="NodeRef"/>s inside <see cref="Explanation"/> wire representations in place
/// (<c>masked:&lt;kind&gt;:&lt;MaskReason&gt;</c>) without dropping conditions, steps, or evidence entries and without
/// leaking hidden UUIDs (K-EXP-2).
/// </summary>
public static partial class ExplanationMasking
{
    /// <summary>Prefix emitted in place of a hidden <see cref="NodeRef"/> string (<c>masked:&lt;kind&gt;:&lt;reason&gt;</c>).</summary>
    public const string MaskedPrefix = "masked:";

    /// <summary>Collects every distinct <see cref="NodeRef"/> referenced by <paramref name="explanation"/>.</summary>
    public static IReadOnlyList<NodeRef> CollectNodes(Explanation explanation)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        return CollectNodes([explanation]);
    }

    /// <summary>Collects every distinct <see cref="NodeRef"/> referenced across <paramref name="explanations"/>.</summary>
    public static IReadOnlyList<NodeRef> CollectNodes(IEnumerable<Explanation> explanations)
    {
        ArgumentNullException.ThrowIfNull(explanations);
        var set = new HashSet<NodeRef>();
        foreach (var explanation in explanations)
        {
            if (explanation.Subject is { } subject)
            {
                set.Add(subject);
            }

            CollectInlineNodes(explanation.Claim, set);

            foreach (var step in explanation.Steps)
            {
                CollectInlineNodes(step.Claim, set);
                foreach (var node in step.Evidence)
                {
                    set.Add(node);
                }
            }

            foreach (var condition in explanation.Conditions)
            {
                CollectInlineNodes(condition.Description, set);
                foreach (var node in condition.Evidence)
                {
                    set.Add(node);
                }
            }

            foreach (var action in explanation.NextActions)
            {
                CollectInlineNodes(action.Description, set);
                if (action.Target is { } target)
                {
                    set.Add(target);
                }
            }
        }

        return [.. set];
    }

    /// <summary>Collects inline <c>kind:uuid[@version]</c> <see cref="NodeRef"/> tokens embedded in free text.</summary>
    public static IReadOnlyList<NodeRef> CollectInlineNodes(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var set = new HashSet<NodeRef>();
        CollectInlineNodes(text, set);
        return [.. set];
    }

    private static void CollectInlineNodes(string? text, HashSet<NodeRef> destination)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (Match match in InlineNodeRefPattern().Matches(text))
        {
            if (NodeRef.TryParse(match.Value, out var parsed))
            {
                destination.Add(parsed);
            }
        }
    }

    /// <summary>Looks up the <see cref="MaskReason"/> for <paramref name="node"/> or its unversioned identity.</summary>
    public static MaskReason? ReasonFor(NodeRef? node, IReadOnlyDictionary<NodeRef, MaskReason>? masks)
    {
        if (node is null || masks is not { Count: > 0 })
        {
            return null;
        }

        if (masks.TryGetValue(node, out var exact))
        {
            return exact;
        }

        return masks.TryGetValue(node.Unversioned(), out var unversioned) ? unversioned : null;
    }

    /// <summary>Formats <paramref name="node"/> as canonical <c>kind:uuid[@version]</c> or <c>masked:kind:Reason</c>.</summary>
    public static string Format(NodeRef node, IReadOnlyDictionary<NodeRef, MaskReason>? masks)
    {
        ArgumentNullException.ThrowIfNull(node);
        return ReasonFor(node, masks) is { } reason
            ? $"{MaskedPrefix}{node.Kind}:{reason}"
            : node.ToString();
    }

    /// <summary>Formats an optional <see cref="NodeRef"/> with <see cref="Format"/>.</summary>
    public static string? FormatOptional(NodeRef? node, IReadOnlyDictionary<NodeRef, MaskReason>? masks) =>
        node is null ? null : Format(node, masks);

    /// <summary>Formats a list of <see cref="NodeRef"/>s in place, preserving item count and order.</summary>
    public static IReadOnlyList<string> FormatAll(IEnumerable<NodeRef> nodes, IReadOnlyDictionary<NodeRef, MaskReason>? masks)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return [.. nodes.Select(n => Format(n, masks))];
    }

    /// <summary>
    /// Replaces any inline <c>kind:uuid[@version]</c> occurrences in <paramref name="text"/> that match a masked node
    /// with <c>masked:kind:Reason</c> so free-text claims or descriptions never leak masked UUIDs.
    /// </summary>
    public static string? ScrubInlineRefs(string? text, IReadOnlyDictionary<NodeRef, MaskReason>? masks)
    {
        if (string.IsNullOrEmpty(text) || masks is not { Count: > 0 })
        {
            return text;
        }

        return InlineNodeRefPattern().Replace(text, match =>
        {
            if (NodeRef.TryParse(match.Value, out var parsed) && ReasonFor(parsed, masks) is { } reason)
            {
                return $"{MaskedPrefix}{parsed.Kind}:{reason}";
            }

            return match.Value;
        });
    }

    [GeneratedRegex(@"\b[a-z][a-z0-9_]*:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:@[1-9][0-9]*)?", RegexOptions.CultureInvariant)]
    private static partial Regex InlineNodeRefPattern();
}

/// <summary>
/// Canonical RFC 9457 <c>ProblemDetails.extensions["explanation"]</c> wire representation of an ADR-2482
/// <see cref="Explanation"/>, shared across kernel error endpoints and standalone RBAC problem details.
/// </summary>
public sealed record ProblemExplanation(
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("claim")] string Claim,
    [property: JsonPropertyName("producer")] string Producer,
    [property: JsonPropertyName("steps")] IReadOnlyList<ProblemExplanationStep> Steps,
    [property: JsonPropertyName("conditions")] IReadOnlyList<ProblemCondition> Conditions,
    [property: JsonPropertyName("next_actions")] IReadOnlyList<ProblemNextAction> NextActions)
{
    /// <summary>Unmasked domain <see cref="Explanation"/> retained in-memory for deferred HTTP filter masking.</summary>
    [JsonIgnore]
    public Explanation? Source { get; init; }

    /// <summary>Formats <paramref name="explanation"/> for HTTP wire emission with optional per-caller masks.</summary>
    public static ProblemExplanation From(Explanation explanation, IReadOnlyDictionary<NodeRef, MaskReason>? masks = null)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        return new(
            ExplanationMasking.FormatOptional(explanation.Subject, masks),
            ExplanationMasking.ScrubInlineRefs(explanation.Claim, masks)!,
            explanation.Producer,
            [.. explanation.Steps.Select(s => new ProblemExplanationStep(
                ExplanationMasking.ScrubInlineRefs(s.Claim, masks)!,
                ExplanationMasking.FormatAll(s.Evidence, masks)))],
            [.. explanation.Conditions.Select(c => new ProblemCondition(
                c.Code,
                ExplanationMasking.ScrubInlineRefs(c.Description, masks)!,
                c.Met,
                ExplanationMasking.FormatAll(c.Evidence, masks)))],
            [.. explanation.NextActions.Select(a => new ProblemNextAction(
                ExplanationMasking.ScrubInlineRefs(a.Description, masks)!,
                a.Capability,
                ExplanationMasking.FormatOptional(a.Target, masks)))])
        {
            Source = explanation,
        };
    }
}

/// <summary>Wire representation of an <see cref="ExplanationStep"/> inside <see cref="ProblemExplanation"/>.</summary>
public sealed record ProblemExplanationStep(
    [property: JsonPropertyName("claim")] string Claim,
    [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence);

/// <summary>Wire representation of an <see cref="EvaluatedCondition"/> inside <see cref="ProblemExplanation"/>.</summary>
public sealed record ProblemCondition(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("met")] bool Met,
    [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence);

/// <summary>Wire representation of a <see cref="NextAction"/> inside <see cref="ProblemExplanation"/>.</summary>
public sealed record ProblemNextAction(
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("capability")] string? Capability,
    [property: JsonPropertyName("target")] string? Target);
