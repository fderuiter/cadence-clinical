using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

/// <summary>Shared NodeRef handling for the kernel tools.</summary>
internal static class ToolNodes
{
    /// <summary>A sentence saying why <paramref name="text"/> is not a NodeRef, or null when it is one.</summary>
    public static string? Problem(string? text) =>
        NodeRef.TryParse(text, out _) ? null : $"'{text}' is not a NodeRef (kind:uuid or kind:uuid@version, lower case).";

    /// <summary>The snake_case name of a mask reason, as tools report it.</summary>
    public static string MaskName(MaskReason reason) => reason switch
    {
        MaskReason.Blinded => "blinded",
        MaskReason.IdentityVault => "identity_vault",
        _ => "not_permitted",
    };

    /// <summary>
    /// <paramref name="explanation"/> with every evidence reference the principal may not see replaced by a count. The
    /// visible references are recorded as read.
    /// </summary>
    public static async Task<ExplanationView> ViewAsync(
        Explanation explanation, AgentToolContext context, IAgentVisibility visibility, CancellationToken cancellationToken)
    {
        var refs = explanation.Steps.SelectMany(s => s.Evidence)
            .Concat(explanation.Conditions.SelectMany(c => c.Evidence))
            .Concat(explanation.NextActions.Where(a => a.Target is not null).Select(a => a.Target!))
            .Concat(explanation.Subject is null ? [] : [explanation.Subject])
            .Distinct()
            .ToList();
        var masks = await visibility.MaskAsync(context.Principal, refs, cancellationToken).ConfigureAwait(false);
        foreach (var shown in refs.Where(r => !masks.ContainsKey(r)))
        {
            context.ReadSet.Record(shown);
        }

        (IReadOnlyList<string> Shown, int Masked) Split(IReadOnlyList<NodeRef> evidence) =>
            ([.. evidence.Where(e => !masks.ContainsKey(e)).Select(e => e.ToString())], evidence.Count(masks.ContainsKey));

        return new ExplanationView(
            explanation.Subject is { } subject && !masks.ContainsKey(subject) ? subject.ToString() : null,
            explanation.Claim,
            explanation.Producer,
            [.. explanation.Steps.Select(s =>
            {
                var (shown, masked) = Split(s.Evidence);
                return new ExplanationStepView(s.Claim, shown, masked);
            })],
            [.. explanation.Conditions.Select(c =>
            {
                var (shown, masked) = Split(c.Evidence);
                return new ConditionView(c.Code, c.Description, c.Met, shown, masked);
            })],
            [.. explanation.NextActions.Select(a => new NextActionView(
                a.Description, a.Capability, a.Target is { } target && !masks.ContainsKey(target) ? target.ToString() : null))]);
    }
}
