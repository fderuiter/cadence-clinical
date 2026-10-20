using System.Collections.Generic;
using System.Text.Json;

namespace Cadence.Modules.Intelligence.Grounding;

/// <summary>
/// The structured output a model returns at the end of a run (ADR-2633 §4.6): what it proposes, the claims behind it
/// with their citations and quoted values, the objects the proposal would change or depends on, and the capabilities
/// a person would use to carry it out. It is untrusted until <see cref="CitationVerifier"/> has checked it.
/// </summary>
/// <param name="Intent">What the proposal would achieve, in one or two sentences.</param>
/// <param name="Claims">The findings behind it, each citing what it was read from.</param>
/// <param name="Affected">NodeRefs the proposal would change or depends on.</param>
/// <param name="RequestedCapabilities">Kernel capability tokens a person would use to execute it.</param>
public sealed record AgentRunOutput(
    string Intent,
    IReadOnlyList<AgentClaim> Claims,
    IReadOnlyList<string> Affected,
    IReadOnlyList<string> RequestedCapabilities);

/// <summary>One claim: a sentence, the NodeRefs it was read from, and any values it quotes.</summary>
public sealed record AgentClaim(string Text, IReadOnlyList<string> Citations, IReadOnlyList<QuotedValue>? Values = null);

/// <summary>A value quoted from an object, exactly as the tool returned it (for an observation, its <c>value</c>).</summary>
public sealed record QuotedValue(string Node, JsonElement Value);
