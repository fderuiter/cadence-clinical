using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cadence.Apps.Interop.Application.Services;

public record PackageEntryDto(
    string Name,
    string Version,
    string? Checksum = null,
    string? Repository = null,
    bool IsQualified = true
);

public record LockfileValidationResultDto(
    string LockType,
    string Status,
    string LockDigest,
    int PackageCount,
    List<PackageEntryDto> Packages,
    bool IsQualified,
    List<string> UnqualifiedPackages,
    string Details
);

public interface ILockfileValidatorService
{
    LockfileValidationResultDto ValidateLockfile(string lockType, string content, string? expectedDigest = null, bool strictQualification = false);
}

public class LockfileValidatorService : ILockfileValidatorService
{
    private static readonly HashSet<string> QualifiedPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        // Python packages
        "pandas", "numpy", "scipy", "polars", "scikit-learn", "matplotlib", "seaborn",
        "statsmodels", "cdisc-rules-engine", "pyarrow", "requests", "uvicorn", "fastapi",
        "pydantic", "jinja2", "openpyxl", "xlsxwriter", "reportlab", "biopython", "sympy", "duckdb",
        // R packages
        "rtables", "dplyr", "ggplot2", "haven", "admiral", "cdiscpilot", "pharmaverse", "tidyr",
        "readr", "stringr", "purrr", "forcats", "tibble", "survival", "lme4", "nlme", "gt",
        "flextable", "officer", "renv", "knitr", "rmarkdown"
    };

    public LockfileValidationResultDto ValidateLockfile(string lockType, string content, string? expectedDigest = null, bool strictQualification = false)
    {
        string normalizedType = lockType.ToUpperInvariant();
        if (normalizedType is not ("UV_LOCK" or "RENV_LOCK" or "REQUIREMENTS_TXT"))
        {
            normalizedType = "UV_LOCK";
        }

        string lockDigest = ComputeSha256(content ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(expectedDigest))
        {
            string cleanExpected = expectedDigest.Trim();
            if (cleanExpected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                cleanExpected = cleanExpected[7..];
            }
            if (!string.Equals(cleanExpected, lockDigest, StringComparison.OrdinalIgnoreCase))
            {
                return new LockfileValidationResultDto(
                    LockType: normalizedType,
                    Status: "TAMPERED",
                    LockDigest: lockDigest,
                    PackageCount: 0,
                    Packages: [],
                    IsQualified: false,
                    UnqualifiedPackages: [],
                    Details: $"Lockfile SHA-256 digest mismatch. Expected: {cleanExpected}, Computed: {lockDigest}"
                );
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new LockfileValidationResultDto(
                LockType: normalizedType,
                Status: "INVALID_SYNTAX",
                LockDigest: lockDigest,
                PackageCount: 0,
                Packages: [],
                IsQualified: false,
                UnqualifiedPackages: [],
                Details: "Lockfile content is empty."
            );
        }

        List<PackageEntryDto> parsedPackages = new();
        bool parseSuccess = false;

        try
        {
            switch (normalizedType)
            {
                case "RENV_LOCK":
                    parseSuccess = ParseRenvLock(content, parsedPackages);
                    break;
                case "UV_LOCK":
                    parseSuccess = ParseUvLock(content, parsedPackages);
                    break;
                case "REQUIREMENTS_TXT":
                    parseSuccess = ParseRequirementsTxt(content, parsedPackages);
                    break;
            }
        }
        catch
        {
            parseSuccess = false;
        }

        if (!parseSuccess)
        {
            return new LockfileValidationResultDto(
                LockType: normalizedType,
                Status: "INVALID_SYNTAX",
                LockDigest: lockDigest,
                PackageCount: 0,
                Packages: [],
                IsQualified: false,
                UnqualifiedPackages: [],
                Details: $"Failed to parse {normalizedType} lockfile syntax."
            );
        }

        List<string> unqualified = new();
        List<PackageEntryDto> verifiedPackages = new();

        foreach (var pkg in parsedPackages)
        {
            bool isPkgQualified = QualifiedPackages.Contains(pkg.Name);
            if (!isPkgQualified)
            {
                unqualified.Add(pkg.Name);
            }
            verifiedPackages.Add(pkg with { IsQualified = isPkgQualified });
        }

        bool isQualified = unqualified.Count == 0 || !strictQualification;
        string status = "VALID";
        if (unqualified.Count > 0 && strictQualification)
        {
            status = "UNQUALIFIED_PACKAGE";
        }

        string details = status == "VALID"
            ? $"Lockfile validated successfully. Total {verifiedPackages.Count} packages parsed."
            : $"Unqualified packages detected in lockfile: {string.Join(", ", unqualified)}";

        return new LockfileValidationResultDto(
            LockType: normalizedType,
            Status: status,
            LockDigest: lockDigest,
            PackageCount: verifiedPackages.Count,
            Packages: verifiedPackages,
            IsQualified: isQualified,
            UnqualifiedPackages: unqualified,
            Details: details
        );
    }

    private static bool ParseRenvLock(string content, List<PackageEntryDto> packages)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return false;

        if (root.TryGetProperty("Packages", out var pkgsElement) && pkgsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in pkgsElement.EnumerateObject())
            {
                string pkgName = prop.Name;
                string version = "0.0.0";
                string? hash = null;
                string? repo = null;

                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    if (prop.Value.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.String)
                        version = v.GetString() ?? "0.0.0";
                    if (prop.Value.TryGetProperty("Hash", out var h) && h.ValueKind == JsonValueKind.String)
                        hash = h.GetString();
                    if (prop.Value.TryGetProperty("Repository", out var r) && r.ValueKind == JsonValueKind.String)
                        repo = r.GetString();
                    else if (prop.Value.TryGetProperty("Source", out var s) && s.ValueKind == JsonValueKind.String)
                        repo = s.GetString();
                }
                packages.Add(new PackageEntryDto(pkgName, version, hash, repo ?? "CRAN"));
            }
            return true;
        }

        return true;
    }

    private static bool ParseUvLock(string content, List<PackageEntryDto> packages)
    {
        // Simple TOML / lockfile parser for [[package]] sections or JSON/TOML lines
        string[] lines = content.Split('\n');
        string? currentName = null;
        string? currentVersion = null;
        string? currentSource = null;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith("[[package]]", StringComparison.Ordinal) || line.StartsWith("[[package.", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(currentName))
                {
                    packages.Add(new PackageEntryDto(currentName, currentVersion ?? "0.0.0", null, currentSource ?? "PyPI"));
                }
                currentName = null;
                currentVersion = null;
                currentSource = null;
                continue;
            }

            var nameMatch = Regex.Match(line, @"^name\s*=\s*[""']([^""']+)[""']");
            if (nameMatch.Success)
            {
                currentName = nameMatch.Groups[1].Value;
                continue;
            }

            var verMatch = Regex.Match(line, @"^version\s*=\s*[""']([^""']+)[""']");
            if (verMatch.Success)
            {
                currentVersion = verMatch.Groups[1].Value;
                continue;
            }

            var sourceMatch = Regex.Match(line, @"^source\s*=\s*[""']([^""']+)[""']");
            if (sourceMatch.Success)
            {
                currentSource = sourceMatch.Groups[1].Value;
            }
        }

        if (!string.IsNullOrEmpty(currentName))
        {
            packages.Add(new PackageEntryDto(currentName, currentVersion ?? "0.0.0", null, currentSource ?? "PyPI"));
        }

        // If simple regex found nothing but content exists, try fallback JSON parse in case it's JSON uv.lock
        if (packages.Count == 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("package", out var pkgArr) && pkgArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in pkgArr.EnumerateArray())
                    {
                        if (item.TryGetProperty("name", out var n))
                        {
                            string name = n.GetString() ?? "";
                            string ver = item.TryGetProperty("version", out var v) ? v.GetString() ?? "0.0.0" : "0.0.0";
                            packages.Add(new PackageEntryDto(name, ver, null, "PyPI"));
                        }
                    }
                }
            }
            catch
            {
                // Ignore json fallback failure
            }
        }

        return true;
    }

    private static bool ParseRequirementsTxt(string content, List<PackageEntryDto> packages)
    {
        string[] lines = content.Split('\n');
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            var match = Regex.Match(line, @"^([a-zA-Z0-9_\-\.]+)(?:==([a-zA-Z0-9_\-\.]+))?");
            if (match.Success)
            {
                string pkgName = match.Groups[1].Value;
                string ver = match.Groups[2].Success ? match.Groups[2].Value : "latest";
                string? hash = null;
                var hashMatch = Regex.Match(line, @"--hash=sha256:([a-fA-F0-9]+)");
                if (hashMatch.Success)
                {
                    hash = hashMatch.Groups[1].Value;
                }
                packages.Add(new PackageEntryDto(pkgName, ver, hash, "PyPI"));
            }
        }
        return true;
    }

    private static string ComputeSha256(string raw)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(raw);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
