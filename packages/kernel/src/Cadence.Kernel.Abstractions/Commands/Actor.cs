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
/// @req:PRD-SYS-053
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

    public static Actor Person(string subject) => new(ActorKind.Person, subject);

    public static Actor ServiceAccount(string name) => new(ActorKind.ServiceAccount, ServiceAccountPrefix + name);

    public override string ToString() => $"{Kind}:{Id}";
}
