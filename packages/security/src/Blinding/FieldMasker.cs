using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

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
        "treatment_group",
        "randomization_seed",
        "seed",
        "encrypted_allocation",
        "stratum_key",
        "randomization_id",
        "encrypted_sequence",
        "unblinded_audit_rationale"
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
        ["treatment_group"] = val => val != null ? "BLINDED" : val,
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
        ["unblinded_audit_rationale"] = val => val != null ? "MASKED" : val,
    };

    public static object? MaskPayload(object? payload, Principal? principal)
    {
        if (payload == null) return null;

        var unmaskedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool isUnblinded = principal is { UnblindedAccess: true };

        if (principal != null)
        {
            foreach (var r in principal.Roles)
            {
                string normRole = RoleNormalization.NormalizeRole(r);
                if (ROLE_UNMASKED_FIELDS.TryGetValue(normRole, out var fields) ||
                    ROLE_UNMASKED_FIELDS.TryGetValue(r, out fields))
                {
                    foreach (var f in fields) unmaskedFields.Add(f);
                }
            }
        }

        return RecursiveMask(payload, unmaskedFields, isUnblinded);
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && input[i - 1] != '_' && (char.IsLower(input[i - 1]) || (i + 1 < input.Length && char.IsLower(input[i + 1]))))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static object? RecursiveMask(object? target, HashSet<string> unmaskedFields, bool isUnblinded = false)
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
                string keySnake = ToSnakeCase(keyStr);

                if (!isUnblinded &&
                    (MASKING_RULES.TryGetValue(keySnake, out var maskFunc) || MASKING_RULES.TryGetValue(keyLower, out maskFunc))
                    && !unmaskedFields.Contains(keySnake) && !unmaskedFields.Contains(keyLower))
                {
                    result[keySnake] = maskFunc(entry.Value);
                }
                else
                {
                    result[keySnake] = RecursiveMask(entry.Value, unmaskedFields, isUnblinded);
                }
            }
            return result;
        }

        if (target is IEnumerable<KeyValuePair<string, object?>> kvpEnumerable)
        {
            var result = new Dictionary<string, object?>();
            foreach (var kvp in kvpEnumerable)
            {
                string keyStr = kvp.Key ?? "";
                string keyLower = keyStr.ToLowerInvariant();
                string keySnake = ToSnakeCase(keyStr);

                if (!isUnblinded &&
                    (MASKING_RULES.TryGetValue(keySnake, out var maskFunc) || MASKING_RULES.TryGetValue(keyLower, out maskFunc))
                    && !unmaskedFields.Contains(keySnake) && !unmaskedFields.Contains(keyLower))
                {
                    result[keySnake] = maskFunc(kvp.Value);
                }
                else
                {
                    result[keySnake] = RecursiveMask(kvp.Value, unmaskedFields, isUnblinded);
                }
            }
            return result;
        }

        if (target is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                list.Add(RecursiveMask(item, unmaskedFields, isUnblinded));
            }
            return list;
        }

        // Object property inspection
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var maskedDict = new Dictionary<string, object?>();

        foreach (var prop in props)
        {
            if (!prop.CanRead) continue;
            if (prop.Name == "EqualityContract") continue;

            object? val = prop.GetValue(target);
            if (val is Type) continue;

            string propName = prop.Name;
            string propLower = propName.ToLowerInvariant();
            string propSnake = ToSnakeCase(propName);
            var jsonAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            string jsonName = jsonAttr?.Name ?? propName;
            string jsonNameLower = jsonName.ToLowerInvariant();
            string jsonNameSnake = ToSnakeCase(jsonName);

            var blindAttr = prop.GetCustomAttribute<BlindedFieldAttribute>();
            bool isUnmasked = isUnblinded
                || unmaskedFields.Contains(propLower) || unmaskedFields.Contains(propSnake)
                || unmaskedFields.Contains(jsonNameLower) || unmaskedFields.Contains(jsonNameSnake);

            string dictKey = jsonNameSnake;

            if (blindAttr != null && !isUnmasked)
            {
                maskedDict[dictKey] = val != null ? blindAttr.MaskedValue : null;
            }
            else if (!isUnmasked && (MASKING_RULES.TryGetValue(jsonNameSnake, out var maskFunc)
                || MASKING_RULES.TryGetValue(jsonNameLower, out maskFunc)
                || MASKING_RULES.TryGetValue(propSnake, out maskFunc)
                || MASKING_RULES.TryGetValue(propLower, out maskFunc)))
            {
                maskedDict[dictKey] = maskFunc(val);
            }
            else
            {
                maskedDict[dictKey] = RecursiveMask(val, unmaskedFields, isUnblinded);
            }
        }

        return maskedDict;
    }
}
