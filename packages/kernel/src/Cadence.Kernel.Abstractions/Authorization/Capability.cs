using System;
using System.Text.RegularExpressions;

namespace Cadence.Kernel.Authorization;

/// <summary>What kind of act a capability allows (spec §9.3). Separation of duties and step-up rules key on it.</summary>
public enum VerbClass
{
    /// <summary>See kernel data.</summary>
    Read,

    /// <summary>Suggest a change that someone else accepts (a draft, a proposed mapping).</summary>
    Propose,

    /// <summary>Change kernel state directly.</summary>
    Execute,

    /// <summary>Accept, approve or retire governed content.</summary>
    Approve,
}

/// <summary>
/// A <c>resource.action</c> token with its verb class (spec §9.3), for example <c>observation.record</c> (Execute) or
/// <c>standard.approve</c> (Approve). Tokens are lower snake_case segments joined by dots, at least two segments, so
/// <c>identity.reveal.person</c> is valid and <c>Observation.Record</c> is not.
/// </summary>
public sealed partial record Capability
{
    public Capability(string token, VerbClass verbClass)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!IsWellFormed(token))
        {
            throw new ArgumentException($"'{token}' is not a capability token (resource.action, lower snake_case).", nameof(token));
        }

        if (!Enum.IsDefined(verbClass))
        {
            throw new ArgumentOutOfRangeException(nameof(verbClass), verbClass, "Unknown verb class.");
        }

        Token = token;
        VerbClass = verbClass;
    }

    public string Token { get; }

    public VerbClass VerbClass { get; }

    /// <summary>True when <paramref name="token"/> follows the capability token grammar.</summary>
    public static bool IsWellFormed(string? token) => token is not null && token.Length <= 96 && TokenPattern().IsMatch(token);

    public override string ToString() => Token;

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:_[a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}

/// <summary>
/// Declares the capability a kernel command needs and the object it is authorized against (ADR-2483). Every kernel
/// command carries one; a command without it is denied. <see cref="Resource"/> names a property of the command that
/// holds a kernel identifier: the object the command acts on, or, for a command that creates an object, the scope it is
/// created in. The command catalogue lists the command on objects of that property's kind.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class KernelCapabilityAttribute(string token, VerbClass verbClass) : Attribute
{
    public string Token { get; } = token;

    public VerbClass VerbClass { get; } = verbClass;

    /// <summary>The command property holding the resource's identifier. Defaults to the tenant.</summary>
    public string Resource { get; init; } = "TenantId";

    /// <summary>False for commands that are never offered on an object, such as provisioning a tenant.</summary>
    public bool InCatalogue { get; init; } = true;
}
