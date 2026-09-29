using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cadence.Security.Rbac;

namespace Cadence.Security.Blinding;

/// <summary>
/// Role-aware response property masking engine for trial blinding rules.
/// @req:PRD-SYS-053
/// </summary>
public static class FieldMasker
{
    public static IReadOnlySet<string> UNMASKED_ALLOCATION_FIELDS { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "treatment_arm",
        "treatment_arm_id",
        "randomization_seed",
        "seed",
        "encrypted_allocation",
        "stratum_key",
        "randomization_id",
        "encrypted_sequence"
    };

    public static IReadOnlySet<string> UNMASKED_SUPPLY_FIELDS { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "drug_code",
        "administered_drug_code",
        "kit_reference"
    };

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ROLE_UNMASKED_FIELDS { get; } = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        [RoleNormalization.ROLE_UNBLINDED_STATISTICIAN] = UNMASKED_ALLOCATION_FIELDS,
        [RoleNormalization.ROLE_IDMC] = UNMASKED_ALLOCATION_FIELDS,
        [RoleNormalization.ROLE_PHARMACIST] = UNMASKED_SUPPLY_FIELDS,
        [RoleNormalization.ROLE_EMERGENCY_UNBLINDER] = new HashSet<string>(UNMASKED_ALLOCATION_FIELDS.Concat(UNMASKED_SUPPLY_FIELDS), StringComparer.OrdinalIgnoreCase)
    };

    public static IReadOnlyDictionary<string, Func<object?, object?>> MASKING_RULES { get; } = new Dictionary<string, Func<object?, object?>>(StringComparer.OrdinalIgnoreCase)
    {
        ["initials"] = val => val != null ? "**" : val,
        ["ssn"] = val => val != null ? "***-**-****" : val,
        ["dob"] = val => val != null ? "MASKED" : val,
        ["treatment_arm_id"] = val => val != null ? "BLINDED" : val,
        ["treatment_arm"] = val => val != null ? "BLINDED" : val,
        ["administered_drug_code"] = val => val != null ? "Obfuscated Kit" : val,
        ["drug_code"] = val => val != null ? "Obfuscated Kit" : val,
        ["changed_reason_for_blinded_field"] = val => val != null ? "Obfuscated" : val,
        ["randomization_seed"] = val => val != null ? "MASKED" : val,
        ["seed"] = val => val != null ? "MASKED" : val,
        ["encrypted_allocation"] = val => val != null ? "MASKED" : val,
        ["kit_reference"] = val => val != null ? "Obfuscated Kit" : val,
        ["stratum_key"] = val => val != null ? "MASKED" : val,
        ["randomization_id"] = val => val != null ? "MASKED" : val,
        ["encrypted_sequence"] = val => val != null ? "MASKED" : val,
    };

    public static object? MaskPayload(object? payload, Principal? principal)
    {
        if (payload == null) return null;
        if (principal?.UnblindedAccess == true) return payload;

        var unmaskedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (principal != null)
        {
            foreach (var r in principal.Roles)
            {
                if (ROLE_UNMASKED_FIELDS.TryGetValue(r, out var fields))
                {
                    foreach (var f in fields) unmaskedFields.Add(f);
                }
            }
        }

        return RecursiveMask(payload, unmaskedFields);
    }

    private static object? RecursiveMask(object? target, HashSet<string> unmaskedFields)
    {
        if (target == null) return null;

        Type type = target.GetType();

        if (type == typeof(string) || type.IsValueType)
        {
            return target;
        }

        if (target is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                string keyStr = entry.Key?.ToString() ?? "";
                string keyLower = keyStr.ToLowerInvariant();

                if (MASKING_RULES.TryGetValue(keyLower, out var maskFunc) && !unmaskedFields.Contains(keyLower))
                {
                    result[keyStr] = maskFunc(entry.Value);
                }
                else
                {
                    result[keyStr] = RecursiveMask(entry.Value, unmaskedFields);
                }
            }
            return result;
        }

        if (target is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                list.Add(RecursiveMask(item, unmaskedFields));
            }
            return list;
        }

        // Object property inspection
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var maskedDict = new Dictionary<string, object?>();

        foreach (var prop in props)
        {
            if (!prop.CanRead) continue;
            string propName = prop.Name;
            string propLower = propName.ToLowerInvariant();
            object? val = prop.GetValue(target);

            var blindAttr = prop.GetCustomAttribute<BlindedFieldAttribute>();

            if (blindAttr != null && !unmaskedFields.Contains(propLower))
            {
                maskedDict[propName] = val != null ? blindAttr.MaskedValue : null;
            }
            else if (MASKING_RULES.TryGetValue(propLower, out var maskFunc) && !unmaskedFields.Contains(propLower))
            {
                maskedDict[propName] = maskFunc(val);
            }
            else
            {
                maskedDict[propName] = RecursiveMask(val, unmaskedFields);
            }
        }

        return maskedDict;
    }
}
