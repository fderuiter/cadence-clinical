using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Core.Domain;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// A tenant's named bundle of capability tokens (spec §9.3), an administrative convenience for issuing grants. A role
/// never changes once defined, so what a grant under it permits is the same at every time it is evaluated. Unblinding
/// capabilities are never bundled (K-AUTH-4).
/// </summary>
public partial class Role : AggregateRoot<RoleId>
{
    /// <summary>The longest role name the kernel stores.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The longest role code the kernel stores.</summary>
    public const int MaxCodeLength = 32;

    protected Role() { }

    private Role(RoleId id, TenantId tenantId, string code, string name, List<string> capabilities)
        : base(id)
    {
        TenantId = tenantId;
        Code = code;
        Name = name;
        Capabilities = capabilities;
    }

    public TenantId TenantId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>The bundled capability tokens, sorted and distinct.</summary>
    public List<string> Capabilities { get; private set; } = [];

    /// <summary>Defines a role after checking its code, name and capability tokens.</summary>
    public static CommandResult<Role> Define(RoleId id, TenantId tenantId, string? code, string? name, IReadOnlyList<string>? capabilities)
    {
        if (code is null || !CodePattern().IsMatch(code))
        {
            return AuthorityErrors.RoleCodeInvalid;
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
        {
            return AuthorityErrors.RoleNameInvalid;
        }

        if (capabilities is null || capabilities.Count == 0 || !capabilities.All(Capability.IsWellFormed))
        {
            return AuthorityErrors.CapabilityInvalid;
        }

        if (capabilities.Any(Capability.IsUnblinding))
        {
            return AuthorityErrors.UnblindingNotBundled;
        }

        var tokens = capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return CommandResult<Role>.Success(new Role(id, tenantId, code, name.Trim(), tokens));
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
