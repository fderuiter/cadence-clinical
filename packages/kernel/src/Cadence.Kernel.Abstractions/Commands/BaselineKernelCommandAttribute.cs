using System;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Marks a kernel command as a baseline trust, authority, privacy, or tenant/subscription lifecycle operation that
/// remains available even when a workspace's commercial trial subscription has expired (<c>PRODUCT_CATALOG.md</c>).
/// Audit trails, electronic signatures, authorization management, privacy erasure/disclosures, and commercial status
/// transitions are never gated behind active paid plans.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BaselineKernelCommandAttribute : Attribute
{
    public static bool IsBaseline(Type commandType) =>
        Attribute.IsDefined(commandType, typeof(BaselineKernelCommandAttribute), inherit: false);
}
