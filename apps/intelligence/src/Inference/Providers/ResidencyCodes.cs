using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cadence.Modules.Intelligence.Inference.Providers;

/// <summary>Residency codes in the form <c>Tenant.Residency</c> uses: two to eight upper-case letters (ADR-2458).</summary>
public static partial class ResidencyCodes
{
    /// <summary>The configured codes as a set; throws naming <paramref name="setting"/> when one is malformed.</summary>
    public static FrozenSet<string> Parse(IEnumerable<string>? codes, string setting)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes ?? [])
        {
            if (code is null || !Code().IsMatch(code))
            {
                throw new InvalidOperationException($"{setting}: '{code}' is not a residency code (two to eight upper-case letters).");
            }

            set.Add(code);
        }

        return set.ToFrozenSet(StringComparer.Ordinal);
    }

    [GeneratedRegex("^[A-Z]{2,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex Code();
}
