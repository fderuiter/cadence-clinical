using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.References;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// What one agent run has read (ADR-2633 §4.6): every visible NodeRef a tool returned and, for observations, the
/// canonical JSON of the value it returned. The citation verifier (K-AI-4) checks the run's output against it. Masked
/// objects are never recorded, so a claim can never cite something the agent was not shown. Tools may run in parallel,
/// so it is thread-safe.
/// </summary>
public sealed class RunReadSet
{
    private readonly Lock gate = new();
    private readonly HashSet<NodeRef> nodes = [];
    private readonly Dictionary<(string Kind, Guid Id), string> values = [];

    /// <summary>Every object read, in no particular order.</summary>
    public IReadOnlyCollection<NodeRef> Nodes
    {
        get
        {
            lock (gate)
            {
                return [.. nodes];
            }
        }
    }

    /// <summary>Records that the run was shown <paramref name="node"/>.</summary>
    public void Record(NodeRef node)
    {
        ArgumentNullException.ThrowIfNull(node);
        lock (gate)
        {
            nodes.Add(node);
        }
    }

    /// <summary>Records that the run was shown <paramref name="node"/> with the value whose fingerprint is <paramref name="valueFingerprint"/>.</summary>
    public void RecordValue(NodeRef node, string valueFingerprint)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrEmpty(valueFingerprint);
        lock (gate)
        {
            nodes.Add(node);
            values[(node.Kind, node.Id)] = valueFingerprint;
        }
    }

    /// <summary>
    /// True when the run read <paramref name="cited"/>: the same kind and id and, when the citation names a version, a
    /// read of exactly that version.
    /// </summary>
    public bool HasRead(NodeRef cited)
    {
        ArgumentNullException.ThrowIfNull(cited);
        lock (gate)
        {
            return nodes.Any(n => n.Kind == cited.Kind && n.Id == cited.Id && (cited.Version is null || n.Version == cited.Version));
        }
    }

    /// <summary>The fingerprint of the value the run was shown for <paramref name="node"/>, or null when it was shown none.</summary>
    public string? ValueOf(NodeRef node)
    {
        ArgumentNullException.ThrowIfNull(node);
        lock (gate)
        {
            return values.GetValueOrDefault((node.Kind, node.Id));
        }
    }
}
