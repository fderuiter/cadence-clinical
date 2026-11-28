using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class DependencyValidatorService
{
    private const string DependabotRelativePath = ".github/dependabot.yml";
    private const int MaxTotalDependabotOpenPullRequestsLimit = 20;

    private sealed class DependabotGroup
    {
        public required string Name { get; init; }

        public HashSet<string> Patterns { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ExcludePatterns { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ParsedDependabotConfig
    {
        public string? Version { get; set; }

        public List<DependabotUpdateEntry> Updates { get; } = [];
    }

    private sealed class DependabotUpdateEntry
    {
        public string? PackageEcosystem { get; set; }

        public string? Directory { get; set; }

        public List<string> Directories { get; } = [];

        public string? TargetBranch { get; set; }

        public string? RawOpenPullRequestsLimit { get; set; }

        public int? OpenPullRequestsLimit { get; set; }

        public List<DependabotGroup> OrderedGroups { get; } = [];

        public Dictionary<string, DependabotGroup> Groups { get; } = new(StringComparer.Ordinal);

        public List<DependabotIgnoreEntry> Ignores { get; } = [];
    }

    private sealed class DependabotIgnoreEntry
    {
        public string? DependencyName { get; set; }

        public bool HasVersions { get; set; }

        public List<string> Versions { get; } = [];

        public bool HasUpdateTypes { get; set; }

        public List<string> UpdateTypes { get; } = [];
    }

    /// <summary>
    /// Validates a Dependabot v2 YAML configuration document for syntax errors, target uniqueness,
    /// and unique <c>ignore</c> rules (<c>dependency-name</c> combined with <c>versions</c> or <c>update-types</c>).
    /// </summary>
    public static IReadOnlyList<string> ValidateDependabotContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var yamlErrors = YamlSyntaxValidatorService.ValidateContent(content, DependabotRelativePath);
        if (yamlErrors.Count > 0)
        {
            return yamlErrors;
        }

        var parseErrors = new List<string>();
        var parsed = ParseDependabotConfig(content, parseErrors);
        if (parseErrors.Count > 0)
        {
            return parseErrors;
        }

        return ValidateDependabotStructureAndUniqueness(parsed);
    }

    private static string? ResolveDependabotPath(RepoSnapshot snapshot)
    {
        if (snapshot.Exists(DependabotRelativePath))
        {
            return DependabotRelativePath;
        }

        return snapshot.Exists(".github/dependabot.yaml") ? ".github/dependabot.yaml" : null;
    }

    private static int ValidateDependabotConfig(RepoSnapshot snapshot, string dependabotPath)
    {
        if (!snapshot.TryReadBytes(dependabotPath, out byte[] bytes, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {dependabotPath}: {readError}");
            return 1;
        }

        var yamlErrors = YamlSyntaxValidatorService.ValidateBytes(bytes, dependabotPath);
        if (yamlErrors.Count > 0)
        {
            foreach (string err in yamlErrors)
            {
                Console.WriteLine($"[ERROR] {err}");
            }

            return yamlErrors.Count;
        }

        string content = WorkspacePrimitives.StrictUtf8.GetString(WorkspacePrimitives.StripBom(bytes));
        var parseErrors = new List<string>();
        var parsed = ParseDependabotConfig(content, parseErrors);
        if (parseErrors.Count > 0)
        {
            foreach (string parseError in parseErrors)
            {
                Console.WriteLine($"[ERROR] {dependabotPath}: {parseError}");
            }

            return parseErrors.Count;
        }

        var structuralErrors = ValidateDependabotStructureAndUniqueness(parsed);
        foreach (string structuralError in structuralErrors)
        {
            Console.WriteLine($"[DEPENDABOT VIOLATION] {dependabotPath}: {structuralError}");
        }

        if (parsed.Updates.Count == 0)
        {
            return structuralErrors.Count;
        }

        int errors = structuralErrors.Count;
        errors += ValidateDependabotOpenPullRequestsLimits(dependabotPath, parsed.Updates);
        errors += ValidateNugetDependabotBlock(dependabotPath, FindDependabotBlock(parsed.Updates, "nuget", "/"));
        errors += ValidateNpmDependabotBlock(dependabotPath, FindDependabotBlock(parsed.Updates, "npm", "/"));
        errors += ValidateDockerDotnetDependabotBlock(dependabotPath, FindDependabotBlock(parsed.Updates, "docker", "/docker"));
        errors += ValidateDockerSceDependabotBlock(dependabotPath, FindDependabotBlock(parsed.Updates, "docker", "/docker/sce/python"), "/docker/sce/python", "python", ScePythonIgnoredUpdateTypes);
        errors += ValidateDockerSceDependabotBlock(dependabotPath, FindDependabotBlock(parsed.Updates, "docker", "/docker/sce/r"), "/docker/sce/r", "r-base", SceRIgnoredUpdateTypes);
        errors += ValidateDockerComposeDependabotBlock(dependabotPath, FindDependabotBlock(parsed.Updates, "docker-compose", "/docker"));
        errors += ValidateGitHubActionsDependabotBlock(dependabotPath, FindDependabotBlock(parsed.Updates, "github-actions", "/"));

        return errors;
    }

    private static List<string> ValidateDependabotStructureAndUniqueness(ParsedDependabotConfig parsed)
    {
        var errors = new List<string>();
        if (!string.Equals(parsed.Version, "2", StringComparison.Ordinal))
        {
            errors.Add("Top-level 'version' must be 2.");
        }

        if (parsed.Updates.Count == 0)
        {
            errors.Add("'updates' must define at least one package-ecosystem entry.");
            return errors;
        }

        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        for (int u = 0; u < parsed.Updates.Count; u++)
        {
            ValidateDependabotUpdateEntry(parsed.Updates[u], u, seenTargets, errors);
        }

        return errors;
    }

    private static void ValidateDependabotUpdateEntry(
        DependabotUpdateEntry update,
        int updateIndex,
        HashSet<string> seenTargets,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(update.PackageEcosystem))
        {
            errors.Add($"'#/updates/{updateIndex}' is missing a non-empty 'package-ecosystem'.");
        }

        var directories = ResolveUpdateDirectories(update);
        if (directories.Count == 0)
        {
            errors.Add($"'#/updates/{updateIndex}' must specify 'directory' or 'directories'.");
        }
        else if (!string.IsNullOrWhiteSpace(update.PackageEcosystem))
        {
            string targetBranch = update.TargetBranch ?? string.Empty;
            string branchSuffix = string.IsNullOrEmpty(targetBranch) ? string.Empty : $" (target-branch '{targetBranch}')";
            foreach (string dir in directories)
            {
                string targetKey = $"{update.PackageEcosystem}\0{dir}\0{targetBranch}";
                if (!seenTargets.Add(targetKey))
                {
                    errors.Add($"'#/updates/{updateIndex}' duplicates package-ecosystem '{update.PackageEcosystem}' and directory '{dir}'{branchSuffix}.");
                }
            }
        }

        var seenVersionIgnores = new HashSet<string>(StringComparer.Ordinal);
        var seenUpdateTypeIgnores = new HashSet<string>(StringComparer.Ordinal);
        var seenBareIgnores = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < update.Ignores.Count; i++)
        {
            ValidateDependabotIgnoreEntry(
                update.Ignores[i],
                updateIndex,
                i,
                seenVersionIgnores,
                seenUpdateTypeIgnores,
                seenBareIgnores,
                errors);
        }
    }

    private static List<string> ResolveUpdateDirectories(DependabotUpdateEntry update)
    {
        if (update.Directories.Count > 0)
        {
            return update.Directories;
        }

        if (!string.IsNullOrWhiteSpace(update.Directory))
        {
            return [update.Directory!];
        }

        return [];
    }

    private static void ValidateDependabotIgnoreEntry(
        DependabotIgnoreEntry ignore,
        int updateIndex,
        int ignoreIndex,
        HashSet<string> seenVersionIgnores,
        HashSet<string> seenUpdateTypeIgnores,
        HashSet<string> seenBareIgnores,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(ignore.DependencyName))
        {
            errors.Add($"'#/updates/{updateIndex}/ignore/{ignoreIndex}' is missing a non-empty 'dependency-name'.");
            return;
        }

        string depName = ignore.DependencyName!;

        if (ignore.HasVersions)
        {
            ValidateIgnoreListUniqueness(
                depName,
                ignore.Versions,
                propertyName: "versions",
                updateIndex,
                ignoreIndex,
                seenVersionIgnores,
                errors);
        }

        if (ignore.HasUpdateTypes)
        {
            ValidateIgnoreListUniqueness(
                depName,
                ignore.UpdateTypes,
                propertyName: "update-types",
                updateIndex,
                ignoreIndex,
                seenUpdateTypeIgnores,
                errors);
        }

        if (!ignore.HasVersions && !ignore.HasUpdateTypes && !seenBareIgnores.Add(depName))
        {
            errors.Add($"The property '#/updates/{updateIndex}/ignore/{ignoreIndex}/dependency-name' includes a duplicate.");
        }
    }

    private static void ValidateIgnoreListUniqueness(
        string depName,
        List<string> values,
        string propertyName,
        int updateIndex,
        int ignoreIndex,
        HashSet<string> seenCombinations,
        List<string> errors)
    {
        if (values.Count == 0)
        {
            errors.Add($"'#/updates/{updateIndex}/ignore/{ignoreIndex}/{propertyName}' must not be empty.");
            return;
        }

        bool reportedOverlap = false;
        foreach (string item in values)
        {
            string itemKey = $"{depName}\0{item}";
            if (!seenCombinations.Add(itemKey) && !reportedOverlap)
            {
                errors.Add(
                    $"The property '#/updates/{updateIndex}/ignore/{ignoreIndex}/{propertyName}' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and '{propertyName}'");
                reportedOverlap = true;
            }
        }
    }

    private static DependabotUpdateEntry? FindDependabotBlock(
        IReadOnlyList<DependabotUpdateEntry> blocks,
        string ecosystem,
        string directory) =>
        blocks.FirstOrDefault(b =>
            string.Equals(b.PackageEcosystem, ecosystem, StringComparison.Ordinal) &&
            ResolveUpdateDirectories(b).Contains(directory, StringComparer.Ordinal));

    private static int ValidateDependabotOpenPullRequestsLimits(
        string relativePath,
        IReadOnlyList<DependabotUpdateEntry> updates)
    {
        int errors = 0;
        long totalLimit = 0;

        for (int i = 0; i < updates.Count; i++)
        {
            DependabotUpdateEntry update = updates[i];
            string ecosystem = string.IsNullOrWhiteSpace(update.PackageEcosystem) ? $"#/updates/{i}" : update.PackageEcosystem!;
            string directory = ResolveUpdateDirectories(update).FirstOrDefault() ?? "?";

            if (update.OpenPullRequestsLimit is not int limit || limit <= 0)
            {
                string rawDisplay = update.RawOpenPullRequestsLimit is null ? "<missing>" : $"'{update.RawOpenPullRequestsLimit}'";
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} ({ecosystem} '{directory}') must specify a positive integer 'open-pull-requests-limit' (found {rawDisplay}).");
                errors++;
                continue;
            }

            totalLimit += limit;
        }

        if (totalLimit > MaxTotalDependabotOpenPullRequestsLimit)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} total 'open-pull-requests-limit' sum ({totalLimit}) must not exceed {MaxTotalDependabotOpenPullRequestsLimit}.");
            errors++;
        }

        return errors;
    }

    private static int RejectNonFinalCatchAllGroups(
        string relativePath,
        string ecosystemLabel,
        DependabotUpdateEntry block)
    {
        int errors = 0;
        for (int i = 0; i < block.OrderedGroups.Count - 1; i++)
        {
            DependabotGroup earlier = block.OrderedGroups[i];
            if (earlier.Patterns.Contains("*"))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} ({ecosystemLabel}) catch-all group '{earlier.Name}' must appear last after all cohort groups.");
                errors++;
            }
        }

        return errors;
    }

    private static int ValidateTrailingCatchAllGroup(
        string relativePath,
        string ecosystemLabel,
        DependabotUpdateEntry block,
        IReadOnlySet<string>? reservedCohortGroupNames = null)
    {
        int errors = RejectNonFinalCatchAllGroups(relativePath, ecosystemLabel, block);
        if (block.OrderedGroups.Count == 0)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} ({ecosystemLabel}) must define a trailing catch-all group with patterns [\"*\"].");
            return errors + 1;
        }

        DependabotGroup lastGroup = block.OrderedGroups[^1];
        if (reservedCohortGroupNames is not null && reservedCohortGroupNames.Contains(lastGroup.Name))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} ({ecosystemLabel}) must append a dedicated trailing catch-all group with patterns [\"*\"] after cohort group '{lastGroup.Name}'.");
            errors++;
            return errors;
        }

        if (lastGroup.Patterns.Count != 1 || !lastGroup.Patterns.Contains("*"))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} ({ecosystemLabel}) final group '{lastGroup.Name}' must define patterns [\"*\"] after all cohort groups.");
            errors++;
        }

        if (lastGroup.ExcludePatterns.Count > 0)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} ({ecosystemLabel}) trailing catch-all group '{lastGroup.Name}' must not define exclude-patterns.");
            errors++;
        }

        return errors;
    }

    private static bool HasIgnoreUpdateType(DependabotUpdateEntry block, string dependencyName, string updateType) =>
        block.Ignores.Any(i =>
            string.Equals(i.DependencyName, dependencyName, StringComparison.Ordinal) &&
            !i.HasVersions &&
            i.UpdateTypes.Contains(updateType, StringComparer.Ordinal));
}
