using System;

namespace Cadence.Security.Blinding;

/// <summary>
/// Attribute marking a sensitive domain or DTO field subject to role-aware unblinding and masking.
/// @req:PRD-SYS-053
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public class BlindedFieldAttribute : Attribute
{
    public string MaskedValue { get; }

    public BlindedFieldAttribute(string maskedValue = "MASKED")
    {
        MaskedValue = maskedValue;
    }
}
