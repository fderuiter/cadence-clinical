using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel.References;
using Cadence.Modules.Intelligence.Tools;

namespace Cadence.Modules.Intelligence.Grounding;

/// <summary>
/// Checks a run's output against what the run read (ADR-2633 §4.6, K-AI-4). A claim survives only when it cites at
/// least one object, every citation is an object the run was shown, and every value it quotes is exactly the value
/// the run was shown for that object. Affected objects survive only when the run read them. Everything else is
/// dropped with its reason, before a person sees it. It checks provenance, not meaning.
/// </summary>
public static class CitationVerifier
{
    /// <summary>Verifies <paramref name="output"/> against <paramref name="readSet"/>.</summary>
    public static GroundedOutput Verify(AgentRunOutput output, RunReadSet readSet)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(readSet);
        var dropped = new List<DroppedItem>();
        var kept = new List<GroundedClaim>();

        for (var index = 0; index < output.Claims.Count; index++)
        {
            var claim = output.Claims[index];
            if (CheckClaim(claim, readSet) is { } failure)
            {
                dropped.Add(new DroppedItem(DroppedItemKind.Claim, index, claim.Text, failure.Reason, failure.Detail));
                continue;
            }

            kept.Add(new GroundedClaim(
                claim.Text,
                [.. claim.Citations.Select(NodeRef.Parse).Distinct()],
                [.. (claim.Values ?? []).Select(v => new GroundedValue(NodeRef.Parse(v.Node), ValueFingerprint.Of(v.Value)!))]));
        }

        var affected = new List<NodeRef>();
        for (var index = 0; index < output.Affected.Count; index++)
        {
            var text = output.Affected[index];
            if (!NodeRef.TryParse(text, out var node))
            {
                dropped.Add(new DroppedItem(DroppedItemKind.Affected, index, text, DropReason.CitationInvalid, "Not a NodeRef."));
            }
            else if (!readSet.HasRead(node))
            {
                dropped.Add(new DroppedItem(DroppedItemKind.Affected, index, text, DropReason.AffectedNotRead, "The run never read this object."));
            }
            else if (!affected.Contains(node))
            {
                affected.Add(node);
            }
        }

        return new GroundedOutput(output.Intent, kept, affected, dropped);
    }

    private static (DropReason Reason, string Detail)? CheckClaim(AgentClaim claim, RunReadSet readSet)
    {
        if (string.IsNullOrWhiteSpace(claim.Text))
        {
            return (DropReason.Empty, "The claim has no text.");
        }

        if (claim.Citations is not { Count: > 0 })
        {
            return (DropReason.Uncited, "The claim cites nothing.");
        }

        foreach (var citation in claim.Citations)
        {
            if (!NodeRef.TryParse(citation, out var node))
            {
                return (DropReason.CitationInvalid, $"'{citation}' is not a NodeRef.");
            }

            if (!readSet.HasRead(node))
            {
                return (DropReason.CitationNotRead, $"The run never read {citation}.");
            }
        }

        foreach (var value in claim.Values ?? [])
        {
            if (!NodeRef.TryParse(value.Node, out var node))
            {
                return (DropReason.CitationInvalid, $"'{value.Node}' is not a NodeRef.");
            }

            if (!claim.Citations.Contains(value.Node, StringComparer.Ordinal))
            {
                return (DropReason.ValueNotCited, $"The value of {value.Node} is quoted but the claim does not cite it.");
            }

            if (readSet.ValueOf(node) is not { } shown || !readSet.HasRead(node))
            {
                return (DropReason.ValueNotRead, $"The run was never shown a value for {value.Node}.");
            }

            if (ValueFingerprint.Of(value.Value) is not { } quoted || !string.Equals(quoted, shown, StringComparison.Ordinal))
            {
                return (DropReason.ValueMismatch, $"The value quoted for {value.Node} is not the value the run was shown.");
            }
        }

        return null;
    }
}

/// <summary>Why the verifier dropped an item.</summary>
public enum DropReason
{
    /// <summary>The claim has no text.</summary>
    Empty,

    /// <summary>The claim cites nothing.</summary>
    Uncited,

    /// <summary>A citation or affected object is not a well-formed NodeRef.</summary>
    CitationInvalid,

    /// <summary>The claim cites an object the run did not read (or read at a different version).</summary>
    CitationNotRead,

    /// <summary>A quoted value belongs to an object the claim does not cite.</summary>
    ValueNotCited,

    /// <summary>A quoted value belongs to an object whose value the run was never shown.</summary>
    ValueNotRead,

    /// <summary>A quoted value differs from the value the run was shown: a fabricated or altered value.</summary>
    ValueMismatch,

    /// <summary>An affected object the run did not read.</summary>
    AffectedNotRead,
}

/// <summary>Whether a dropped item was a claim or an affected object.</summary>
public enum DroppedItemKind
{
    /// <summary>A claim, with its citations and values.</summary>
    Claim,

    /// <summary>An object the output named as affected.</summary>
    Affected,
}

/// <summary>An item the verifier dropped, kept for the reviewer and the ledger.</summary>
/// <param name="Kind">Whether it was a claim or an affected object.</param>
/// <param name="Index">Its position in the model's output.</param>
/// <param name="Text">The claim text or the affected reference, as the model wrote it.</param>
/// <param name="Reason">Why it was dropped.</param>
/// <param name="Detail">The reason, in a sentence.</param>
public sealed record DroppedItem(DroppedItemKind Kind, int Index, string Text, DropReason Reason, string Detail);

/// <summary>A value that survived: the object and the fingerprint (<see cref="ValueFingerprint"/>) of the value the run was shown.</summary>
public sealed record GroundedValue(NodeRef Node, string Fingerprint);

/// <summary>A claim that survived verification.</summary>
public sealed record GroundedClaim(string Text, IReadOnlyList<NodeRef> Citations, IReadOnlyList<GroundedValue> Values);

/// <summary>A run's output after verification: what survived and what was dropped.</summary>
public sealed record GroundedOutput(string Intent, IReadOnlyList<GroundedClaim> Claims, IReadOnlyList<NodeRef> Affected, IReadOnlyList<DroppedItem> Dropped)
{
    /// <summary>True when at least one claim survived.</summary>
    public bool IsGrounded => Claims.Count > 0;
}
