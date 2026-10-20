using System;
using System.Collections.Frozen;

namespace Cadence.Kernel.Commands;

/// <summary>What kind of principal performed a kernel action (spec §9.1).</summary>
public enum ActorKind
{
    /// <summary>A human, authenticated through OIDC.</summary>
    Person,

    /// <summary>Named, non-human background work (for example a message consumer).</summary>
    ServiceAccount,

    /// <summary>An AI or rules agent acting through the Intelligence engine (spec §11).</summary>
    AutomationAgent,
}

/// <summary>
/// The resolved principal behind a kernel command. Every kernel write has one (K-TX-5): system work runs as a named
/// service account whose id starts with <c>svc:</c> (ADR-2257), and anonymous or generic ids such as
/// <c>system</c> are refused at construction.
/// </summary>
public sealed record Actor
{
    /// <summary>Required prefix of a service-account id.</summary>
    public const string ServiceAccountPrefix = "svc:";

    /// <summary>Required prefix of an automation-agent id.</summary>
    public const string AutomationAgentPrefix = "agent:";

    private static readonly FrozenSet<string> ReservedIds = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "system", "anonymous", "unknown", "admin", "root", "svc:", "agent:");

    public Actor(ActorKind kind, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (ReservedIds.Contains(id.Trim()))
        {
            throw new ArgumentException($"'{id}' does not identify an accountable actor (K-TX-5).", nameof(id));
        }

        var requiredPrefix = kind switch
        {
            ActorKind.ServiceAccount => ServiceAccountPrefix,
            ActorKind.AutomationAgent => AutomationAgentPrefix,
            _ => null,
        };
        if (requiredPrefix is not null && !id.StartsWith(requiredPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{kind} ids must start with '{requiredPrefix}'.", nameof(id));
        }

        if (kind == ActorKind.Person && (id.StartsWith(ServiceAccountPrefix, StringComparison.Ordinal) || id.StartsWith(AutomationAgentPrefix, StringComparison.Ordinal)))
        {
            throw new ArgumentException("A person id cannot use a service-account or agent prefix.", nameof(id));
        }

        Kind = kind;
        Id = id;
    }

    public ActorKind Kind { get; }

    /// <summary>Stable id: the OIDC subject for a person, <c>svc:&lt;name&gt;</c> or <c>agent:&lt;name&gt;</c> otherwise.</summary>
    public string Id { get; }

    /// <summary>
    /// The person an automation agent acts for (K-AI-6, ADR-2632): the agent's authority is bounded by this person's, and
    /// an agent acting for nobody is refused everything. Null for a person or a service account, and for an agent named
    /// only as an identity, such as the grantee of a grant.
    /// </summary>
    public Actor? RequestedBy
    {
        get;
        init
        {
            if (value is not null && (Kind != ActorKind.AutomationAgent || value.Kind != ActorKind.Person))
            {
                throw new ArgumentException("Only an automation agent acts for a requesting person, and the requester is a person (K-AI-6).", nameof(RequestedBy));
            }

            field = value;
        }
    }

    public static Actor Person(string subject) => new(ActorKind.Person, subject);

    public static Actor ServiceAccount(string name) => new(ActorKind.ServiceAccount, ServiceAccountPrefix + name);

    /// <summary>
    /// An AI or rules agent acting through the Intelligence engine (spec §11, ADR-2489): <c>agent:&lt;name&gt;</c>. An agent
    /// may read and propose; the authorizer denies it every Execute and Approve command.
    /// </summary>
    public static Actor AutomationAgent(string name) => new(ActorKind.AutomationAgent, AutomationAgentPrefix + name);

    /// <summary>
    /// This automation agent acting for <paramref name="person"/> (K-AI-6): it may do only what both it and the person
    /// may do, and it sees only what both may see.
    /// </summary>
    public Actor ActingFor(Actor person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return this with { RequestedBy = person };
    }

    /// <summary>This actor as an identity: an agent without the person it acts for, as grants and proposals name it.</summary>
    public Actor AsIdentity() => RequestedBy is null ? this : this with { RequestedBy = null };

    public override string ToString() => $"{Kind}:{Id}";
}
