using System;
using System.Text.RegularExpressions;

using Cadence.Core.Domain;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// One version of a tenant's work-queue definition (Canvas D10, ADR-2486): the items a named source reports as due,
/// for whoever may exercise <see cref="Capability"/> on them. A queue is keyed by a capability, never a role name, so
/// who sees an item follows the grants in force. Versions are insert-only; the highest version of a code is current.
/// </summary>
public partial class WorkQueueDefinition : AggregateRoot<WorkQueueDefinitionId>
{
    /// <summary>The longest code the kernel stores.</summary>
    public const int MaxCodeLength = 64;

    /// <summary>The longest title the kernel stores.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>The longest capability token or source name the kernel stores.</summary>
    public const int MaxTokenLength = 100;

    protected WorkQueueDefinition() { }

    private WorkQueueDefinition(
        WorkQueueDefinitionId id, TenantId tenantId, string code, int version, string title, string capability, VerbClass verbClass, string source)
        : base(id)
    {
        TenantId = tenantId;
        Code = code;
        Version = version;
        Title = title;
        Capability = capability;
        VerbClass = verbClass;
        Source = source;
    }

    public TenantId TenantId { get; private set; }

    /// <summary>The queue's stable code across versions.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>1 for the first definition of the code, then one more than the previous version.</summary>
    public int Version { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>The capability token an item needs (for example <c>protocol.approve</c>).</summary>
    public string Capability { get; private set; } = string.Empty;

    /// <summary>The capability's verb class, which the authorization decision for each item uses.</summary>
    public VerbClass VerbClass { get; private set; }

    /// <summary>The registered source of candidate items.</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>
    /// Defines version <paramref name="version"/> of <paramref name="code"/> after checking the code, title and
    /// capability. The caller has checked that <paramref name="source"/> is registered, that the version follows the
    /// code's latest and that <paramref name="verbClass"/> is the one a kernel command declares for the capability.
    /// </summary>
    public static CommandResult<WorkQueueDefinition> Define(
        WorkQueueDefinitionId id,
        TenantId tenantId,
        string? code,
        int version,
        string? title,
        string? capability,
        VerbClass verbClass,
        string source)
    {
        if (code is null || !CodePattern().IsMatch(code))
        {
            return WorkQueueErrors.CodeInvalid;
        }

        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength)
        {
            return WorkQueueErrors.TitleInvalid;
        }

        if (capability is null || capability.Length > MaxTokenLength || !Authorization.Capability.IsWellFormed(capability)
            || !Enum.IsDefined(verbClass))
        {
            return WorkQueueErrors.CapabilityInvalid;
        }

        return CommandResult<WorkQueueDefinition>.Success(
            new WorkQueueDefinition(id, tenantId, code, version, title.Trim(), capability, verbClass, source));
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
