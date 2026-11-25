using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates <c>.env.example</c> duplicate-key absence, POSIX shell-sourceability, fail-closed local
/// startup secret defaults, pairwise-distinct Base64 cryptographic keys, and <c>README.md</c>
/// cryptographic key coverage.
/// </summary>
public static class EnvExampleStartupValidator
{
    private static readonly string[] RequiredNonEmptyEnvKeys =
    [
        "ConnectionStrings__cadence",
        "POSTGRES_CONNECTION_STRING",
        "GATEWAY_SECRET",
        "AUDIT_LOG_SECRET_KEY",
        "DemographicsEncryption__KeyBase64",
        "RANDOMIZATION_ALLOCATION_KEY",
        "CGT_BARCODE_HMAC_KEY",
        "FIELD_ENCRYPTION_KEY",
        "FIELD_LOOKUP_HASH_KEY",
        "CADENCE_SCE_SIGNING_KEY",
        "CADENCE_IMAGING_DEID_SIGNING_KEY",
        "CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY",
        "CADENCE_BIOMETRIC_LIVENESS_KEY",
    ];

    private static readonly string[] Required256BitBase64EnvKeys =
    [
        "RANDOMIZATION_ALLOCATION_KEY",
        "CGT_BARCODE_HMAC_KEY",
        "FIELD_ENCRYPTION_KEY",
        "FIELD_LOOKUP_HASH_KEY",
        "CADENCE_SCE_SIGNING_KEY",
        "CADENCE_IMAGING_DEID_SIGNING_KEY",
        "CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY",
        "CADENCE_BIOMETRIC_LIVENESS_KEY",
    ];

    private static readonly HashSet<string> RequiredNonEmptyEnvKeySet =
        new(RequiredNonEmptyEnvKeys, StringComparer.Ordinal);

    /// <summary>
    /// Extracts key-value pairs from a dotenv file, stripping inline comments and enclosing quotes.
    /// </summary>
    public static Dictionary<string, string> ExtractKeyValuesFromEnvFile(string filePath) =>
        ExtractKeyValuesFromEnvFile(filePath, out _);

    /// <summary>
    /// Extracts key-value pairs from a dotenv file, stripping inline comments and enclosing quotes,
    /// while reporting any duplicate key declarations in file order.
    /// </summary>
    public static Dictionary<string, string> ExtractKeyValuesFromEnvFile(
        string filePath,
        out IReadOnlyList<string> duplicateKeys)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        var duplicateSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (string line in File.ReadLines(filePath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            int eq = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                string key = trimmed[..eq].Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    if (values.ContainsKey(key) && duplicateSet.Add(key))
                    {
                        duplicates.Add(key);
                    }

                    values[key] = StripEnvValueComment(trimmed[(eq + 1)..]);
                }
            }
        }

        duplicateKeys = duplicates;
        return values;
    }

    /// <summary>
    /// Validates <c>.env.example</c> duplicate-key absence, startup key presence, non-emptiness,
    /// shell-sourceability, pairwise-distinct Base64 lengths, and <c>README.md</c> startup cryptographic key documentation.
    /// </summary>
    public static bool ValidateEnvExampleStartupValues(
        string repoRoot,
        string envExamplePath,
        IReadOnlyDictionary<string, string> exampleValues,
        IReadOnlyList<string>? duplicateKeys = null)
    {
        bool ok = true;
        bool isFullRepo = File.Exists(Path.Combine(repoRoot, "Cadence.sln"));

        if (duplicateKeys is { Count: > 0 })
        {
            foreach (string dupKey in duplicateKeys)
            {
                Console.WriteLine($"[ERROR] .env.example defines duplicate key '{dupKey}'.");
                ok = false;
            }
        }

        foreach ((string key, string value) in exampleValues)
        {
            if (RequiredNonEmptyEnvKeySet.Contains(key) && string.IsNullOrWhiteSpace(value))
            {
                Console.WriteLine(
                    $"[ERROR] .env.example leaves required startup key '{key}' blank; local .env provisioning requires a non-empty development value.");
                ok = false;
            }
        }

        if (!isFullRepo)
        {
            return ok;
        }

        ok &= ValidateEnvExampleShellSourceability(envExamplePath);

        foreach (string requiredKey in RequiredNonEmptyEnvKeys)
        {
            if (!exampleValues.TryGetValue(requiredKey, out string? value) || string.IsNullOrWhiteSpace(value))
            {
                if (!exampleValues.ContainsKey(requiredKey))
                {
                    Console.WriteLine($"[ERROR] .env.example is missing required local startup key '{requiredKey}'.");
                }

                ok = false;
            }
        }

        if (exampleValues.TryGetValue("GATEWAY_SECRET", out string? gatewaySecret) &&
            !string.IsNullOrWhiteSpace(gatewaySecret) &&
            gatewaySecret.Length < 32)
        {
            Console.WriteLine("[ERROR] .env.example 'GATEWAY_SECRET' must be at least 32 characters long.");
            ok = false;
        }

        var seenKeyMaterial = new Dictionary<string, string>(StringComparer.Ordinal);
        if (exampleValues.TryGetValue("DemographicsEncryption__KeyBase64", out string? demoKey) &&
            !string.IsNullOrWhiteSpace(demoKey))
        {
            if (!TryDecodeBase64(demoKey, out byte[] bytes) || bytes.Length is not (16 or 24 or 32))
            {
                Console.WriteLine(
                    "[ERROR] .env.example 'DemographicsEncryption__KeyBase64' must decode to 16, 24, or 32 Base64 bytes.");
                ok = false;
            }
            else
            {
                seenKeyMaterial[Convert.ToHexString(bytes)] = "DemographicsEncryption__KeyBase64";
            }
        }

        foreach (string key in Required256BitBase64EnvKeys)
        {
            if (!exampleValues.TryGetValue(key, out string? raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!TryDecodeBase64(raw, out byte[] bytes) || bytes.Length != 32)
            {
                Console.WriteLine($"[ERROR] .env.example '{key}' must decode to 32 Base64 bytes.");
                ok = false;
                continue;
            }

            string hex = Convert.ToHexString(bytes);
            if (seenKeyMaterial.TryGetValue(hex, out string? duplicateOf))
            {
                Console.WriteLine(
                    $"[ERROR] .env.example '{key}' reuses identical key material with '{duplicateOf}'; local Base64 keys must be pairwise distinct.");
                ok = false;
            }
            else
            {
                seenKeyMaterial[hex] = key;
            }
        }

        string readmePath = Path.Combine(repoRoot, "README.md");
        if (File.Exists(readmePath))
        {
            string readmeText = File.ReadAllText(readmePath);
            foreach (string cryptoKey in Required256BitBase64EnvKeys
                .Prepend("DemographicsEncryption__KeyBase64")
                .Prepend("AUDIT_LOG_SECRET_KEY"))
            {
                if (!readmeText.Contains($"`{cryptoKey}`", StringComparison.Ordinal))
                {
                    Console.WriteLine(
                        $"[ERROR] README.md local setup summary omits required cryptographic key '{cryptoKey}'.");
                    ok = false;
                }
            }
        }

        return ok;
    }

    /// <summary>
    /// Verifies that every non-empty value in <paramref name="envExamplePath"/> containing whitespace
    /// or POSIX shell metacharacters is quoted so <c>set -a; . ./.env; set +a</c> succeeds.
    /// </summary>
    public static bool ValidateEnvExampleShellSourceability(string envExamplePath)
    {
        bool ok = true;
        foreach (string line in File.ReadLines(envExamplePath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            int eq = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            string key = trimmed[..eq].Trim();
            string raw = StripRawUnquotedComment(trimmed[(eq + 1)..]);
            if (raw.Length == 0)
            {
                continue;
            }

            bool isQuoted = raw.Length >= 2 &&
                            ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\''));
            if (!isQuoted && raw.Any(c => char.IsWhiteSpace(c) || c is ';' or '&' or '|' or '$' or '`' or '<' or '>' or '(' or ')'))
            {
                Console.WriteLine(
                    $"[ERROR] .env.example '{key}' has an unquoted value containing whitespace or shell metacharacters; quote it so 'set -a; . ./.env; set +a' succeeds.");
                ok = false;
            }
        }

        return ok;
    }

    private static string StripEnvValueComment(string rawValue)
    {
        string trimmed = StripRawUnquotedComment(rawValue);
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed;
    }

    private static string StripRawUnquotedComment(string rawValue)
    {
        string trimmed = rawValue.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return string.Empty;
        }

        char quote = '\0';
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if ((c == '"' || c == '\'') && (i == 0 || trimmed[i - 1] != '\\'))
            {
                if (quote == '\0')
                {
                    quote = c;
                }
                else if (quote == c)
                {
                    quote = '\0';
                }

                continue;
            }

            if (quote == '\0' && c == '#' && i > 0 && char.IsWhiteSpace(trimmed[i - 1]))
            {
                return trimmed[..i].TrimEnd();
            }
        }

        return trimmed;
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
