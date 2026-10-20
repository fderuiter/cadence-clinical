using System;
using System.Security.Cryptography;
using System.Text;

using Cadence.Core.Domain;
using Cadence.Kernel;

namespace Cadence.Modules.Intelligence.Domain;

/// <summary>
/// A tenant's kill switch and monthly token budget, for all agents (<see cref="AllTasks"/>) or for one task
/// (ADR-2630 §4.2). A switched-off scope refuses new runs; a spent budget refuses new runs until the next month. Both
/// stop AI spend only: clinical work never waits on an agent (K-AI-8). Written only by <c>SetAgentControl</c>, so every
/// change is on the kernel audit chain.
/// </summary>
public sealed class AgentControl : AggregateRoot<Guid>
{
    /// <summary>The scope that covers every task.</summary>
    public const string AllTasks = "*";

    private AgentControl()
    {
    }

    private AgentControl(TenantId tenantId, string scope)
        : base(IdFor(tenantId, scope))
    {
        TenantId = tenantId;
        Scope = scope;
    }

    /// <summary>The tenant the control belongs to.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary><see cref="AllTasks"/>, or one task key.</summary>
    public string Scope { get; private set; } = AllTasks;

    /// <summary>False when the scope's agents are switched off.</summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>The most tokens (input and output) the scope may use in a calendar month (UTC); null for no limit.</summary>
    public long? MonthlyTokenLimit { get; private set; }

    /// <summary>A new control for <paramref name="scope"/>, switched on and without a limit.</summary>
    public static AgentControl For(TenantId tenantId, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return new AgentControl(tenantId, scope);
    }

    /// <summary>The control's id: one per tenant and scope, derived from both so it can be found without a lookup.</summary>
    public static Guid IdFor(TenantId tenantId, string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"intelligence.AgentControl:{tenantId.Value:D}:{scope}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Switches the scope on or off and sets its budget.</summary>
    public void Set(bool enabled, long? monthlyTokenLimit)
    {
        if (monthlyTokenLimit is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monthlyTokenLimit), monthlyTokenLimit, "A budget is never negative.");
        }

        Enabled = enabled;
        MonthlyTokenLimit = monthlyTokenLimit;
    }
}
