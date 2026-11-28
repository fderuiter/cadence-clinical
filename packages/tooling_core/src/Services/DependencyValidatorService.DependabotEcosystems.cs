using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static partial class DependencyValidatorService
{
    private static readonly (string GroupName, string[] Patterns, string[] ExcludePatterns)[] RequiredNugetGroups =
    [
        (
            "dotnet-10-servicing",
            [
                "Microsoft.AspNetCore.*",
                "Microsoft.EntityFrameworkCore*",
                "Microsoft.Extensions.*",
                "System.Security.Cryptography.*",
                "System.IO.*",
                "dotnet-ef",
            ],
            ["Microsoft.Extensions.AI*"]
        ),
        ("dotnet-ai", ["Microsoft.Extensions.AI*"], []),
        ("aspire", ["Aspire.*"], []),
        ("identitymodel", ["Microsoft.IdentityModel.*", "System.IdentityModel.*"], []),
        ("opentelemetry", ["OpenTelemetry*"], []),
        ("masstransit", ["MassTransit*"], []),
        ("fluentvalidation", ["FluentValidation*"], []),
        ("dotnet-testing", ["xunit*", "Microsoft.NET.Test.Sdk", "TngTech.ArchUnitNET*"], []),
        ("analyzers", ["Microsoft.CodeAnalysis.*", "SonarAnalyzer.CSharp"], []),
        ("anthropic", ["Anthropic*"], []),
    ];

    private static readonly string[] RequiredNugetMajorIgnores =
    [
        "MassTransit*",
        "Microsoft.AspNetCore.*",
        "Microsoft.EntityFrameworkCore*",
        "Microsoft.Extensions.*",
        "System.Security.Cryptography.*",
        "System.IO.*",
        "dotnet-ef",
        "Aspire.*",
        "Fluid.Core",
        "xunit*",
        "Microsoft.NET.Test.Sdk",
        "TngTech.ArchUnitNET*",
        "Microsoft.CodeAnalysis.*",
        "SonarAnalyzer.CSharp",
        "Anthropic*",
    ];

    private static readonly (string DependencyName, string VersionRange)[] RequiredNugetVersionIgnores =
    [
        ("AWSSDK.*", ">= 4.1.0"),
        ("OpenTelemetry*", ">= 1.21.0"),
        ("SonarAnalyzer.CSharp", ">= 10.7.0"),
    ];

    private static readonly HashSet<string> RequiredNugetGroupNames =
        RequiredNugetGroups.Select(g => g.GroupName).ToHashSet(StringComparer.Ordinal);

    private static readonly string[] RequiredNpmGroups =
    [
        "typescript-eslint",
        "vitest",
        "playwright",
        "vue-vite",
    ];

    private static readonly HashSet<string> RequiredNpmGroupNames =
        RequiredNpmGroups.ToHashSet(StringComparer.Ordinal);

    private static readonly string[] RequiredDockerDotnetPatterns =
    [
        "mcr.microsoft.com/dotnet/*",
        "dotnet/*",
    ];

    private static readonly string[] ScePythonIgnoredUpdateTypes =
    [
        "version-update:semver-major",
        "version-update:semver-minor",
    ];

    private static readonly string[] SceRIgnoredUpdateTypes =
    [
        "version-update:semver-major",
        "version-update:semver-minor",
        "version-update:semver-patch",
    ];

    private static int ValidateNugetDependabotBlock(string relativePath, DependabotUpdateEntry? block)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'nuget' update block for directory '/'.");
            return 1;
        }

        int errors = 0;
        foreach (var (groupName, patterns, excludePatterns) in RequiredNugetGroups)
        {
            if (!block.Groups.TryGetValue(groupName, out var group))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (nuget '/') is missing lockstep group '{groupName}'.");
                errors++;
                continue;
            }

            foreach (string pattern in patterns.Where(p => !group.Patterns.Contains(p)))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (nuget group '{groupName}') must include pattern '{pattern}'.");
                errors++;
            }

            foreach (string excluded in excludePatterns.Where(p => !group.ExcludePatterns.Contains(p)))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (nuget group '{groupName}') must exclude pattern '{excluded}'.");
                errors++;
            }
        }

        foreach (string dep in RequiredNugetMajorIgnores)
        {
            if (!HasIgnoreUpdateType(block, dep, "version-update:semver-major"))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} (nuget '/') must ignore 'version-update:semver-major' for '{dep}'.");
                errors++;
            }
        }

        foreach (var (dep, range) in RequiredNugetVersionIgnores)
        {
            if (!block.Ignores.Any(i =>
                    string.Equals(i.DependencyName, dep, StringComparison.Ordinal) &&
                    !i.HasUpdateTypes &&
                    i.Versions.Contains(range, StringComparer.Ordinal)))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} (nuget '/') must ignore versions ['{range}'] for '{dep}'.");
                errors++;
            }
        }

        errors += ValidateTrailingCatchAllGroup(relativePath, "nuget '/'", block, RequiredNugetGroupNames);
        return errors;
    }

    private static int ValidateNpmDependabotBlock(string relativePath, DependabotUpdateEntry? block)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'npm' update block for directory '/'.");
            return 1;
        }

        int errors = 0;
        foreach (string groupName in RequiredNpmGroups)
        {
            if (!block.Groups.TryGetValue(groupName, out var group) || group.Patterns.Count == 0)
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (npm '/') is missing lockstep group '{groupName}'.");
                errors++;
            }
        }

        if (block.Groups.TryGetValue("vue-vite", out var vueViteGroup) && !vueViteGroup.Patterns.Contains("vue-tsc"))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (npm group 'vue-vite') must include pattern 'vue-tsc'.");
            errors++;
        }

        if (!HasIgnoreUpdateType(block, "@types/node", "version-update:semver-major"))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (npm '/') must ignore 'version-update:semver-major' for '@types/node'.");
            errors++;
        }

        errors += ValidateTrailingCatchAllGroup(relativePath, "npm '/'", block, RequiredNpmGroupNames);
        return errors;
    }

    private static int ValidateDockerDotnetDependabotBlock(string relativePath, DependabotUpdateEntry? block)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'docker' update block for directory '/docker'.");
            return 1;
        }

        int errors = RejectNonFinalCatchAllGroups(relativePath, "docker '/docker'", block);
        if (!block.Groups.TryGetValue("dotnet-containers", out var group))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (docker '/docker') is missing lockstep group 'dotnet-containers'.");
            errors++;
        }
        else
        {
            foreach (string pattern in RequiredDockerDotnetPatterns.Where(p => !group.Patterns.Contains(p)))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (docker group 'dotnet-containers') must include pattern '{pattern}'.");
                errors++;
            }
        }

        foreach (string dep in RequiredDockerDotnetPatterns)
        {
            if (!HasIgnoreUpdateType(block, dep, "version-update:semver-major"))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (docker '/docker') must ignore 'version-update:semver-major' for '{dep}'.");
                errors++;
            }
        }

        return errors;
    }

    private static int ValidateDockerSceDependabotBlock(
        string relativePath,
        DependabotUpdateEntry? block,
        string directory,
        string dependencyName,
        string[] requiredUpdateTypes)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'docker' update block for directory '{directory}'.");
            return 1;
        }

        int errors = 0;
        foreach (string updateType in requiredUpdateTypes)
        {
            if (!HasIgnoreUpdateType(block, dependencyName, updateType))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} (docker '{directory}') must ignore '{updateType}' for '{dependencyName}'.");
                errors++;
            }
        }

        return errors;
    }

    private static int ValidateDockerComposeDependabotBlock(string relativePath, DependabotUpdateEntry? block)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'docker-compose' update block for directory '/docker'.");
            return 1;
        }

        int errors = 0;
        bool blocksAllPostgresUpdates = block.Ignores.Any(i =>
            string.Equals(i.DependencyName, "postgres", StringComparison.Ordinal) &&
            !i.HasUpdateTypes &&
            (i.Versions.Count == 0 || i.Versions.Contains(">= 0", StringComparer.Ordinal)));

        if (!blocksAllPostgresUpdates)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} (docker-compose '/docker') must ignore 'postgres' without 'update-types' scoping so standalone compose @sha256 digest bumps cannot drift from PostgresTestImage.cs (ADR-2533).");
            errors++;
        }

        errors += ValidateTrailingCatchAllGroup(relativePath, "docker-compose '/docker'", block);
        return errors;
    }

    private static int ValidateGitHubActionsDependabotBlock(string relativePath, DependabotUpdateEntry? block)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'github-actions' update block for directory '/'.");
            return 1;
        }

        return ValidateTrailingCatchAllGroup(relativePath, "github-actions '/'", block);
    }
}
