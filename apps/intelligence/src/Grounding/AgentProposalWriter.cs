using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Modules.Intelligence.Tools;

namespace Cadence.Modules.Intelligence.Grounding;

/// <summary>
/// Turns a run's output into one kernel Proposal (ADR-2633 §4.7): verify citations (K-AI-4), refuse when nothing is
/// grounded, store the plan with the model and configuration version (K-AI-5), then propose as the agent for its
/// requester. It sends nothing but <see cref="Propose"/>, and never retries a refused proposal with other content.
/// </summary>
public sealed class AgentProposalWriter(
    IPlanArtifactStore plans, IAgentProposalGateway gateway, IClock clock)
{
    /// <summary>How long an agent's proposal stays approvable unless the caller says otherwise.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(14);

    /// <summary>Verifies <paramref name="output"/>, stores its plan and proposes it.</summary>
    public async Task<ProposalWriteResult> WriteAsync(
        AgentToolSession session,
        AgentRunProvenance provenance,
        StudyId study,
        AgentRunOutput output,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(output);

        var grounded = CitationVerifier.Verify(output, session.ReadSet);
        if (!grounded.IsGrounded)
        {
            return ProposalWriteResult.Refused(ProposalWriteStatus.NotGrounded, grounded, "No claim in the output is grounded in what the run read.");
        }

        if (Problem(output, grounded, lifetime ?? DefaultLifetime) is { } problem)
        {
            return ProposalWriteResult.Refused(ProposalWriteStatus.Invalid, grounded, problem);
        }

        var principal = session.Principal;
        var now = clock.UtcNow;
        var capabilities = output.RequestedCapabilities.ToList();
        var plan = new PlanDocument(
            PlanDocument.CurrentSchema,
            principal.RunId,
            principal.Agent.Id,
            principal.Requester.Id,
            study.ToString(),
            grounded.Intent.Trim(),
            new PlanModel(provenance.ModelId),
            new PlanConfiguration(provenance.ConfigurationId, provenance.ConfigurationVersion, provenance.ConfigurationHash),
            [.. grounded.Claims.Select(c => new PlanClaim(
                c.Text,
                [.. c.Citations.Select(n => n.ToString())],
                [.. c.Values.Select(v => new PlanValue(v.Node.ToString(), JsonElement.Parse(v.Fingerprint)))]))],
            [.. grounded.Affected.Select(n => n.ToString())],
            capabilities,
            [.. grounded.Dropped.Select(d => new PlanDroppedItem(d.Kind, d.Index, d.Reason))],
            [.. session.Calls.Select(c => new PlanToolCall(c.Tool, c.ErrorCode, c.StartedAt, c.FinishedAt))],
            now);

        var canonical = JsonCanonicalization.Canonicalize(JsonSerializer.Serialize(plan, IntelligenceToolsJsonContext.Default.PlanDocument));
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var content = new PlanContent(bytes, ContentHash.OfBytes(bytes), PlanDocument.MediaType);

        var stored = await plans.StoreAsync(principal, study, content, cancellationToken).ConfigureAwait(false);
        if (!stored.IsSuccess)
        {
            return ProposalWriteResult.Refused(ProposalWriteStatus.PlanRefused, grounded, stored.Error!.Message, stored.Error, content.ContentHash);
        }

        var command = new Propose(
            principal.Tenant,
            ProposalId.New(now),
            study,
            plan.Intent,
            stored.Value,
            grounded.Affected,
            SimulatedImpact: null,
            capabilities,
            now + (lifetime ?? DefaultLifetime),
            provenance.ModelId,
            IdempotencyKey: string.Create(CultureInfo.InvariantCulture, $"agent-run:{principal.RunId:N}"),
            ReasonForChange: string.Create(
                CultureInfo.InvariantCulture,
                $"Proposed by {principal.Agent.Id} for {principal.Requester.Id} with task configuration {provenance.ConfigurationId} v{provenance.ConfigurationVersion} and model {provenance.ModelId}."));

        var proposed = await gateway.ProposeAsync(principal, command, cancellationToken).ConfigureAwait(false);
        return proposed.IsSuccess
            ? new ProposalWriteResult(ProposalWriteStatus.Proposed, grounded, proposed.Value, null, null, content.ContentHash)
            : ProposalWriteResult.Refused(ProposalWriteStatus.ProposalRefused, grounded, proposed.Error!.Message, proposed.Error, content.ContentHash);
    }

    private static string? Problem(AgentRunOutput output, GroundedOutput grounded, TimeSpan lifetime)
    {
        var intent = grounded.Intent?.Trim() ?? string.Empty;
        if (intent.Length is 0 or > Propose.MaxIntentLength)
        {
            return $"The intent must be 1 to {Propose.MaxIntentLength} characters.";
        }

        if (grounded.Affected.Count > Propose.MaxAffected)
        {
            return $"A proposal names at most {Propose.MaxAffected} affected objects.";
        }

        var capabilities = output.RequestedCapabilities ?? [];
        if (capabilities.Count is 0 or > Propose.MaxRequestedCapabilities)
        {
            return $"A proposal requests 1 to {Propose.MaxRequestedCapabilities} capabilities.";
        }

        if (capabilities.FirstOrDefault(c => !Capability.IsWellFormed(c)) is { } malformed)
        {
            return $"'{malformed}' is not a capability token.";
        }

        if (capabilities.Distinct(StringComparer.Ordinal).Count() != capabilities.Count)
        {
            return "A capability is requested twice.";
        }

        return lifetime <= TimeSpan.Zero || lifetime > Propose.MaxLifetime
            ? $"A proposal expires within {Propose.MaxLifetime.TotalDays} days."
            : null;
    }
}

/// <summary>
/// The model and task configuration version that produced a run's output (K-AI-5, ADR-2631). The Foundation's
/// configuration registry fills it from the qualified configuration the run loaded.
/// </summary>
public sealed record AgentRunProvenance(string ModelId, string ConfigurationId, int ConfigurationVersion, string ConfigurationHash)
{
    /// <summary>Returns this provenance, or throws when a field is missing or malformed.</summary>
    /// <exception cref="ArgumentException">A field is missing or malformed.</exception>
    public AgentRunProvenance Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConfigurationId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConfigurationVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConfigurationHash);
        return this;
    }
}

/// <summary>What happened to a run's output.</summary>
public enum ProposalWriteStatus
{
    /// <summary>The kernel recorded the proposal.</summary>
    Proposed,

    /// <summary>Nothing survived citation checking, so nothing was proposed.</summary>
    NotGrounded,

    /// <summary>The output breaks a proposal limit (intent, capabilities, affected objects, lifetime).</summary>
    Invalid,

    /// <summary>The host refused to store the plan.</summary>
    PlanRefused,

    /// <summary>The kernel refused the proposal.</summary>
    ProposalRefused,
}

/// <summary>The outcome of <see cref="AgentProposalWriter.WriteAsync"/>, with the verified output for the ledger.</summary>
public sealed record ProposalWriteResult(
    ProposalWriteStatus Status,
    GroundedOutput Grounded,
    ProposalState? Proposal,
    string? Problem,
    KernelError? Error,
    string? PlanContentHash)
{
    internal static ProposalWriteResult Refused(
        ProposalWriteStatus status, GroundedOutput grounded, string problem, KernelError? error = null, string? planHash = null) =>
        new(status, grounded, null, problem, error, planHash);
}
