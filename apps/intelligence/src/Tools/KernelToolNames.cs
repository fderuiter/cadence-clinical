using System.Collections.Frozen;
using System.Collections.Generic;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>The names of the kernel read tools (ADR-2633 §4.3), as a task configuration's allow-list names them.</summary>
public static class KernelToolNames
{
    /// <summary>Reads one object, with an observation's value.</summary>
    public const string GetNode = "get_node";
    /// <summary>Lists an object's children.</summary>
    public const string ListChildren = "list_children";
    /// <summary>Searches objects by text.</summary>
    public const string SearchKernel = "search_kernel";
    /// <summary>Traces lineage backward or forward.</summary>
    public const string TraceLineage = "trace_lineage";
    /// <summary>Explains a finding or decision.</summary>
    public const string Explain = "explain";
    /// <summary>Asks whether the requester may use a capability (dry run).</summary>
    public const string CanI = "can_i";
    /// <summary>Simulates a protocol change without saving it.</summary>
    public const string Simulate = "simulate";

    /// <summary>Every kernel read tool.</summary>
    public static readonly FrozenSet<string> All = FrozenSet.Create(
        StringComparer.Ordinal, GetNode, ListChildren, SearchKernel, TraceLineage, Explain, CanI, Simulate);

    /// <summary>
    /// The tools published to outside agents over MCP (ADR-2633 §4.8): the reads that need no host adapter, plus the dry
    /// run. Simulation joins once the host simulates as the requester (ADR-2632).
    /// </summary>
    public static readonly IReadOnlyList<string> McpProfile = [GetNode, ListChildren, SearchKernel, TraceLineage, Explain, CanI];
}
