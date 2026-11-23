using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Cadence.ToolingCore.Services;

public static partial class DependencyValidatorService
{
    public const int MaxTotalOpenPullRequestsLimit = 9;
    public const int MaxNugetOpenPullRequestsLimit = 3;
    public const int MaxNpmOpenPullRequestsLimit = 1;
    public const int MaxSingleStreamOpenPullRequestsLimit = 1;
    public const int MaxBlocksPerWeekday = 2;

    private static readonly string[] ForbiddenPackages = ["node-forge", "elliptic", "jose"];
    private static readonly string[] DependencySections = ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"];
    private static readonly string[] SemverUpdateTypes = ["major", "minor", "patch"];
    private static readonly HashSet<string> AllowedWeekdays = new(StringComparer.Ordinal)
    {
        "monday",
        "tuesday",
        "wednesday",
        "thursday",
        "friday",
    };

    public sealed record DependabotGroup(
        string Name,
        IReadOnlyList<string> Patterns,
        IReadOnlyList<string> ExcludePatterns,
        IReadOnlyList<string> UpdateTypes);

    public sealed record DependabotIgnore(
        string DependencyName,
        IReadOnlyList<string> UpdateTypes,
        IReadOnlyList<string> Versions);

    public sealed record DependabotUpdateBlock(
        string PackageEcosystem,
        string Directory,
        string ScheduleInterval,
        string ScheduleDay,
        int? OpenPullRequestsLimit,
        IReadOnlyList<DependabotGroup> Groups,
        IReadOnlyList<DependabotIgnore> Ignores);

    private sealed record YamlLine(int Number, int Indent, string Text);

    public static int ValidateDependencies(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating dependency manifests, Dependabot batching coverage, and frontend cryptographic checks...");

        var packageManifests = DiscoverPackageManifests(repoRoot);
        string dotnetToolsPath = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
        bool hasDotnetTools = File.Exists(dotnetToolsPath);
        string dependabotPath = Path.Combine(repoRoot, ".github", "dependabot.yml");
        bool hasDependabot = File.Exists(dependabotPath);

        if (packageManifests.Count == 0 && !hasDotnetTools && !hasDependabot)
        {
            Console.WriteLine("✔ No dependency manifests found, skipping.");
            return 0;
        }

        int errors = 0;

        foreach (string pkgPath in packageManifests)
        {
            string relativePath = Path.GetRelativePath(repoRoot, pkgPath).Replace('\\', '/');
            errors += ValidatePackageManifest(pkgPath, relativePath);
        }

        if (hasDotnetTools)
        {
            errors += ValidateDotnetToolsManifest(dotnetToolsPath);
        }

        if (hasDependabot)
        {
            errors += ValidateDependabotConfiguration(repoRoot, dependabotPath, packageManifests, hasDotnetTools, dotnetToolsPath);
        }

        if (errors > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {errors} dependency manifest, Dependabot batching, or security violation(s).");
            return 1;
        }

        Console.WriteLine("✔ Dependency manifests, Dependabot group coverage, and frontend crypto checks passed cleanly.");
        return 0;
    }

    public static IReadOnlyList<DependabotUpdateBlock> ParseDependabotUpdates(string dependabotPath, List<string> errors)
    {
        var yamlErrors = YamlSyntaxValidatorService.ValidateFile(dependabotPath, ".github/dependabot.yml");
        if (yamlErrors.Count > 0)
        {
            foreach (string yamlErr in yamlErrors)
            {
                errors.Add(yamlErr);
            }

            return [];
        }

        var lines = ReadPreprocessedYamlLines(dependabotPath);
        string? version = lines
            .Where(l => l.Indent == 0 && l.Text.StartsWith("version:", StringComparison.Ordinal))
            .Select(l => UnquoteScalar(l.Text["version:".Length..]))
            .FirstOrDefault();

        if (!string.Equals(version, "2", StringComparison.Ordinal))
        {
            errors.Add(".github/dependabot.yml must declare top-level 'version: 2'.");
        }

        int updatesIdx = lines.FindIndex(l => l.Indent == 0 && l.Text == "updates:");
        if (updatesIdx < 0)
        {
            errors.Add(".github/dependabot.yml is missing top-level 'updates:' sequence.");
            return [];
        }

        return ParseUpdateSequence(lines, updatesIdx + 1, errors);
    }

    public static bool MatchesGroup(string dependencyName, DependabotGroup group, bool ignoreCase)
    {
        bool included = group.Patterns.Any(p => GlobMatches(p, dependencyName, ignoreCase));
        if (!included)
        {
            return false;
        }

        return !group.ExcludePatterns.Any(p => GlobMatches(p, dependencyName, ignoreCase));
    }

    public static string? FindMatchingGroup(
        string dependencyName,
        DependabotUpdateBlock block,
        string? updateType = null)
    {
        bool ignoreCase = UsesCaseInsensitiveMatching(block.PackageEcosystem);
        foreach (var group in block.Groups)
        {
            if (!MatchesGroup(dependencyName, group, ignoreCase))
            {
                continue;
            }

            if (updateType is null || group.UpdateTypes.Count == 0 || group.UpdateTypes.Contains(updateType, StringComparer.Ordinal))
            {
                return group.Name;
            }
        }

        return null;
    }

    private static int ValidateDependabotConfiguration(
        string repoRoot,
        string dependabotPath,
        List<string> packageManifests,
        bool hasDotnetTools,
        string dotnetToolsPath)
    {
        var errors = new List<string>();
        var blocks = ParseDependabotUpdates(dependabotPath, errors);
        if (errors.Count == 0)
        {
            ValidateDependabotBlocks(blocks, errors);
            ValidateNugetGroupCoverage(repoRoot, hasDotnetTools, dotnetToolsPath, blocks, errors);
            ValidateNpmGroupCoverage(repoRoot, packageManifests, blocks, errors);
            ValidateDockerGroupCoverage(repoRoot, blocks, errors);
            ValidateComposeGroupCoverage(repoRoot, blocks, errors);
            ValidateWorkflowActionsGroupCoverage(repoRoot, blocks, errors);
        }

        foreach (string error in errors)
        {
            Console.WriteLine($"[DEPENDABOT] {error}");
        }

        return errors.Count;
    }

    private static void ValidateDependabotBlocks(IReadOnlyList<DependabotUpdateBlock> blocks, List<string> errors)
    {
        if (blocks.Count == 0)
        {
            errors.Add(".github/dependabot.yml must configure at least one ecosystem entry under 'updates:'.");
            return;
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var weekdayCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int totalPrLimit = 0;

        foreach (var block in blocks)
        {
            string label = $"{block.PackageEcosystem} ({block.Directory})";
            if (string.IsNullOrWhiteSpace(block.PackageEcosystem) || string.IsNullOrWhiteSpace(block.Directory))
            {
                errors.Add(".github/dependabot.yml update entry is missing 'package-ecosystem' or 'directory'.");
                continue;
            }

            if (!seenKeys.Add($"{block.PackageEcosystem}|{block.Directory}"))
            {
                errors.Add($".github/dependabot.yml defines duplicate update block for {label}.");
            }

            ValidateBlockSchedule(block, label, weekdayCounts, errors);
            totalPrLimit += ValidateBlockPrLimitAndGroups(block, label, errors);
        }

        if (totalPrLimit > MaxTotalOpenPullRequestsLimit)
        {
            errors.Add($".github/dependabot.yml total open-pull-requests-limit is {totalPrLimit}, exceeding cap of {MaxTotalOpenPullRequestsLimit}.");
        }

        foreach (var (day, count) in weekdayCounts.Where(kv => kv.Value > MaxBlocksPerWeekday))
        {
            errors.Add($".github/dependabot.yml schedules {count} update blocks on '{day}', exceeding max {MaxBlocksPerWeekday} per weekday.");
        }

        if (blocks.Count >= 2 && weekdayCounts.Count < 2)
        {
            errors.Add(".github/dependabot.yml must stagger multiple weekly update blocks across distinct weekdays.");
        }

        if (blocks.Count >= 5 && weekdayCounts.Count < AllowedWeekdays.Count)
        {
            errors.Add(".github/dependabot.yml must stagger update blocks across all 5 weekdays (monday through friday).");
        }
    }

    private static void ValidateBlockSchedule(
        DependabotUpdateBlock block,
        string label,
        Dictionary<string, int> weekdayCounts,
        List<string> errors)
    {
        if (!string.Equals(block.ScheduleInterval, "weekly", StringComparison.Ordinal))
        {
            errors.Add($".github/dependabot.yml {label} must set schedule.interval to 'weekly' (found '{block.ScheduleInterval}').");
        }

        if (!AllowedWeekdays.Contains(block.ScheduleDay))
        {
            errors.Add($".github/dependabot.yml {label} must schedule on a weekday monday..friday (found '{block.ScheduleDay}').");
            return;
        }

        weekdayCounts[block.ScheduleDay] = weekdayCounts.GetValueOrDefault(block.ScheduleDay) + 1;
    }

    private static int ValidateBlockPrLimitAndGroups(DependabotUpdateBlock block, string label, List<string> errors)
    {
        if (block.OpenPullRequestsLimit is null or < 1)
        {
            errors.Add($".github/dependabot.yml {label} must set a positive integer 'open-pull-requests-limit'.");
            return 0;
        }

        int limit = block.OpenPullRequestsLimit.Value;
        int ecosystemCap = block.PackageEcosystem switch
        {
            "nuget" => MaxNugetOpenPullRequestsLimit,
            "npm" => MaxNpmOpenPullRequestsLimit,
            _ => MaxSingleStreamOpenPullRequestsLimit,
        };

        if (limit > ecosystemCap)
        {
            errors.Add($".github/dependabot.yml {label} open-pull-requests-limit is {limit}, exceeding ecosystem cap of {ecosystemCap}.");
        }

        if (block.Groups.Count == 0)
        {
            errors.Add($".github/dependabot.yml {label} must define at least one group under 'groups:'.");
            return limit;
        }

        foreach (var group in block.Groups)
        {
            if (group.Patterns.Count == 0 || group.Patterns.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($".github/dependabot.yml {label} group '{group.Name}' must define non-empty 'patterns'.");
            }
        }

        if (string.Equals(block.PackageEcosystem, "npm", StringComparison.Ordinal))
        {
            ValidateSingleNpmLockfileStream(block, label, errors);
        }

        return limit;
    }

    private static void ValidateSingleNpmLockfileStream(DependabotUpdateBlock block, string label, List<string> errors)
    {
        foreach (string updateType in new[] { "minor", "patch" })
        {
            int coveringGroups = block.Groups.Count(
                g => g.UpdateTypes.Count == 0 || g.UpdateTypes.Contains(updateType, StringComparer.Ordinal));
            if (coveringGroups > 1)
            {
                errors.Add($".github/dependabot.yml {label} splits '{updateType}' updates across {coveringGroups} groups, risking concurrent pnpm-lock.yaml conflicts.");
            }
        }
    }

    private static void ValidateNugetGroupCoverage(
        string repoRoot,
        bool hasDotnetTools,
        string dotnetToolsPath,
        IReadOnlyList<DependabotUpdateBlock> blocks,
        List<string> errors)
    {
        var nugetDeps = new SortedSet<string>(StringComparer.Ordinal);
        string propsPath = Path.Combine(repoRoot, "Directory.Packages.props");
        if (File.Exists(propsPath))
        {
            try
            {
                var doc = XDocument.Load(propsPath);
                foreach (var pkg in doc.Descendants("PackageVersion"))
                {
                    string? id = pkg.Attribute("Include")?.Value;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        nugetDeps.Add(id);
                    }
                }
            }
            catch (Exception ex) when (ex is XmlException or IOException)
            {
                errors.Add($"Failed to parse Directory.Packages.props: {ex.Message}");
                return;
            }
        }

        if (hasDotnetTools)
        {
            TryCollectDotnetToolNames(dotnetToolsPath, nugetDeps);
        }

        if (nugetDeps.Count == 0)
        {
            return;
        }

        ValidateEcosystemDependencies("nuget", "/", nugetDeps, blocks, errors);
    }

    private static void TryCollectDotnetToolNames(string dotnetToolsPath, SortedSet<string> target)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(dotnetToolsPath));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("tools", out var tools) &&
                tools.ValueKind == JsonValueKind.Object)
            {
                foreach (var tool in tools.EnumerateObject())
                {
                    target.Add(tool.Name);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Malformed JSON is already reported by ValidateDotnetToolsManifest.
            _ = ex;
        }
    }

    private static void ValidateNpmGroupCoverage(
        string repoRoot,
        List<string> packageManifests,
        IReadOnlyList<DependabotUpdateBlock> blocks,
        List<string> errors)
    {
        var npmDeps = new SortedSet<string>(StringComparer.Ordinal);
        string workspaceYamlPath = Path.Combine(repoRoot, "pnpm-workspace.yaml");
        if (File.Exists(workspaceYamlPath))
        {
            var yamlErrors = YamlSyntaxValidatorService.ValidateFile(workspaceYamlPath, "pnpm-workspace.yaml");
            if (yamlErrors.Count > 0)
            {
                errors.AddRange(yamlErrors);
                return;
            }

            foreach (string pkg in ReadPnpmCatalogKeys(workspaceYamlPath))
            {
                npmDeps.Add(pkg);
            }
        }

        foreach (string manifestPath in packageManifests)
        {
            TryCollectPackageJsonDependencies(manifestPath, npmDeps);
        }

        if (npmDeps.Count == 0)
        {
            return;
        }

        ValidateEcosystemDependencies("npm", "/", npmDeps, blocks, errors);
    }

    private static void TryCollectPackageJsonDependencies(string manifestPath, SortedSet<string> target)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (string section in DependencySections)
            {
                if (!doc.RootElement.TryGetProperty(section, out var deps) || deps.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var dep in deps.EnumerateObject())
                {
                    string? spec = dep.Value.ValueKind == JsonValueKind.String ? dep.Value.GetString() : null;
                    if (spec is not null && !spec.StartsWith("workspace:", StringComparison.Ordinal))
                    {
                        target.Add(dep.Name);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Malformed JSON is already reported by ValidatePackageManifest.
            _ = ex;
        }
    }

    private static void ValidateDockerGroupCoverage(
        string repoRoot,
        IReadOnlyList<DependabotUpdateBlock> blocks,
        List<string> errors)
    {
        string dockerRoot = Path.Combine(repoRoot, "docker");
        if (!Directory.Exists(dockerRoot))
        {
            return;
        }

        var dockerfiles = Directory.EnumerateFiles(dockerRoot, "Dockerfile*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal);

        foreach (string dockerfile in dockerfiles)
        {
            string relDir = Path.GetRelativePath(repoRoot, Path.GetDirectoryName(dockerfile)!).Replace('\\', '/');
            string dependabotDir = "/" + relDir.Trim('/');
            var images = ExtractDockerfileBaseImages(dockerfile);
            if (images.Count > 0)
            {
                ValidateEcosystemDependencies("docker", dependabotDir, images, blocks, errors);
            }
        }
    }

    private static void ValidateComposeGroupCoverage(
        string repoRoot,
        IReadOnlyList<DependabotUpdateBlock> blocks,
        List<string> errors)
    {
        string composePath = Path.Combine(repoRoot, "docker", "docker-compose.yml");
        if (!File.Exists(composePath))
        {
            return;
        }

        var images = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string rawLine in File.ReadAllLines(composePath))
        {
            var match = ComposeImageRegex().Match(StripYamlComment(rawLine));
            if (match.Success)
            {
                images.Add(ExtractImageRepository(UnquoteScalar(match.Groups["image"].Value)));
            }
        }

        if (images.Count > 0)
        {
            ValidateEcosystemDependencies("docker-compose", "/docker", images, blocks, errors);
        }
    }

    private static void ValidateWorkflowActionsGroupCoverage(
        string repoRoot,
        IReadOnlyList<DependabotUpdateBlock> blocks,
        List<string> errors)
    {
        var actions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in DiscoverWorkflowAndActionFiles(repoRoot))
        {
            foreach (string rawLine in File.ReadAllLines(file))
            {
                var match = WorkflowUsesRegex().Match(StripYamlComment(rawLine));
                if (!match.Success)
                {
                    continue;
                }

                string reference = UnquoteScalar(match.Groups["uses"].Value);
                if (reference.StartsWith("./", StringComparison.Ordinal) ||
                    reference.StartsWith(".\\", StringComparison.Ordinal) ||
                    reference.StartsWith("docker://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int atIndex = reference.IndexOf('@', StringComparison.Ordinal);
                actions.Add(atIndex > 0 ? reference[..atIndex] : reference);
            }
        }

        if (actions.Count > 0)
        {
            ValidateEcosystemDependencies("github-actions", "/", actions, blocks, errors);
        }
    }

    private static void ValidateEcosystemDependencies(
        string ecosystem,
        string directory,
        IEnumerable<string> dependencies,
        IReadOnlyList<DependabotUpdateBlock> blocks,
        List<string> errors)
    {
        var block = blocks.FirstOrDefault(
            b => string.Equals(b.PackageEcosystem, ecosystem, StringComparison.Ordinal) &&
                 string.Equals(b.Directory, directory, StringComparison.Ordinal));

        if (block is null)
        {
            errors.Add($".github/dependabot.yml is missing update block for package-ecosystem '{ecosystem}' in directory '{directory}'.");
            return;
        }

        bool ignoreCase = UsesCaseInsensitiveMatching(ecosystem);
        foreach (string dep in dependencies)
        {
            if (FindMatchingGroup(dep, block) is null)
            {
                errors.Add($"{ecosystem} ({directory}) dependency '{dep}' is not covered by any Dependabot group.");
                continue;
            }

            foreach (string updateType in SemverUpdateTypes)
            {
                if (IsUpdateTypeIgnored(dep, updateType, block.Ignores, ignoreCase))
                {
                    continue;
                }

                if (FindMatchingGroup(dep, block, updateType) is null)
                {
                    errors.Add($"{ecosystem} ({directory}) dependency '{dep}' lacks Dependabot group coverage for '{updateType}' updates.");
                }
            }
        }
    }

    private static bool IsUpdateTypeIgnored(
        string dependencyName,
        string updateType,
        IReadOnlyList<DependabotIgnore> ignores,
        bool ignoreCase)
    {
        string expectedToken = $"version-update:semver-{updateType}";
        foreach (var ignore in ignores)
        {
            if (!GlobMatches(ignore.DependencyName, dependencyName, ignoreCase))
            {
                continue;
            }

            if (ignore.UpdateTypes.Contains(expectedToken, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static SortedSet<string> ExtractDockerfileBaseImages(string dockerfilePath)
    {
        var images = new SortedSet<string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawLine in File.ReadAllLines(dockerfilePath))
        {
            var match = DockerfileFromRegex().Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            string imageRef = match.Groups["image"].Value;
            if (!string.Equals(imageRef, "scratch", StringComparison.OrdinalIgnoreCase) && !aliases.Contains(imageRef))
            {
                images.Add(ExtractImageRepository(imageRef));
            }

            if (match.Groups["alias"].Success)
            {
                aliases.Add(match.Groups["alias"].Value);
            }
        }

        return images;
    }

    private static string ExtractImageRepository(string imageReference)
    {
        string trimmed = imageReference;
        int digestIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (digestIndex >= 0)
        {
            trimmed = trimmed[..digestIndex];
        }

        int lastSlash = trimmed.LastIndexOf('/');
        int colonIndex = trimmed.LastIndexOf(':');
        if (colonIndex > lastSlash)
        {
            trimmed = trimmed[..colonIndex];
        }

        return trimmed;
    }

    private static List<string> DiscoverWorkflowAndActionFiles(string repoRoot)
    {
        var files = new List<string>();
        string workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
        if (Directory.Exists(workflowsDir))
        {
            files.AddRange(Directory.EnumerateFiles(workflowsDir)
                .Where(f => f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)));
        }

        string actionsDir = Path.Combine(repoRoot, ".github", "actions");
        if (Directory.Exists(actionsDir))
        {
            files.AddRange(Directory.EnumerateFiles(actionsDir, "*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string name = Path.GetFileName(f);
                    return string.Equals(name, "action.yml", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, "action.yaml", StringComparison.OrdinalIgnoreCase);
                }));
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static List<string> ReadPnpmCatalogKeys(string workspaceYamlPath)
    {
        var keys = new List<string>();
        bool inCatalog = false;
        foreach (string rawLine in File.ReadAllLines(workspaceYamlPath))
        {
            string line = StripYamlComment(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                inCatalog = string.Equals(line.Trim(), "catalog:", StringComparison.Ordinal);
                continue;
            }

            if (!inCatalog)
            {
                continue;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                string key = UnquoteScalar(line[..colon]);
                if (key.Length > 0)
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static List<DependabotUpdateBlock> ParseUpdateSequence(
        List<YamlLine> lines,
        int startIndex,
        List<string> errors)
    {
        var blocks = new List<DependabotUpdateBlock>();
        int idx = startIndex;
        if (idx >= lines.Count || lines[idx].Indent == 0 && !lines[idx].Text.StartsWith("- ", StringComparison.Ordinal))
        {
            return blocks;
        }

        int itemIndent = lines[idx].Indent;
        while (idx < lines.Count && lines[idx].Indent >= itemIndent)
        {
            if (lines[idx].Indent != itemIndent || !lines[idx].Text.StartsWith("- ", StringComparison.Ordinal))
            {
                idx++;
                continue;
            }

            int nextIdx = idx + 1;
            while (nextIdx < lines.Count &&
                   (lines[nextIdx].Indent > itemIndent ||
                    lines[nextIdx].Indent == itemIndent && !lines[nextIdx].Text.StartsWith("- ", StringComparison.Ordinal)))
            {
                if (lines[nextIdx].Indent < itemIndent)
                {
                    break;
                }

                nextIdx++;
            }

            blocks.Add(ParseSingleUpdateBlock(lines, idx, nextIdx, itemIndent, errors));
            idx = nextIdx;
        }

        return blocks;
    }

    private static DependabotUpdateBlock ParseSingleUpdateBlock(
        List<YamlLine> lines,
        int start,
        int end,
        int itemIndent,
        List<string> errors)
    {
        string ecosystem = "";
        string directory = "";
        string interval = "";
        string day = "";
        int? prLimit = null;
        var groups = new List<DependabotGroup>();
        var ignores = new List<DependabotIgnore>();

        int propIndent = itemIndent + 2;
        var normalized = new List<YamlLine>(end - start)
        {
            new(lines[start].Number, propIndent, lines[start].Text[2..].TrimStart()),
        };
        for (int k = start + 1; k < end; k++)
        {
            normalized.Add(lines[k]);
        }

        int i = 0;
        while (i < normalized.Count)
        {
            var current = normalized[i];
            if (current.Indent != propIndent || !TrySplitKeyValue(current.Text, out string key, out string rest))
            {
                i++;
                continue;
            }

            i++;
            switch (key)
            {
                case "package-ecosystem":
                    ecosystem = UnquoteScalar(rest);
                    break;
                case "directory":
                    directory = UnquoteScalar(rest);
                    break;
                case "open-pull-requests-limit":
                    if (int.TryParse(UnquoteScalar(rest), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLimit))
                    {
                        prLimit = parsedLimit;
                    }
                    else
                    {
                        errors.Add($".github/dependabot.yml:{current.Number}: invalid integer '{rest}' for open-pull-requests-limit.");
                    }

                    break;
                case "schedule":
                    ParseScheduleSection(normalized, ref i, propIndent, ref interval, ref day);
                    break;
                case "groups":
                    ParseGroupsSection(normalized, ref i, propIndent, groups);
                    break;
                case "ignore":
                    ParseIgnoreSection(normalized, ref i, propIndent, ignores);
                    break;
            }
        }

        return new DependabotUpdateBlock(ecosystem, directory, interval, day, prLimit, groups, ignores);
    }

    private static void ParseScheduleSection(
        List<YamlLine> lines,
        ref int index,
        int parentIndent,
        ref string interval,
        ref string day)
    {
        while (index < lines.Count && lines[index].Indent > parentIndent)
        {
            if (TrySplitKeyValue(lines[index].Text, out string key, out string val))
            {
                if (key == "interval")
                {
                    interval = UnquoteScalar(val);
                }
                else if (key == "day")
                {
                    day = UnquoteScalar(val);
                }
            }

            index++;
        }
    }

    private static void ParseGroupsSection(
        List<YamlLine> lines,
        ref int index,
        int parentIndent,
        List<DependabotGroup> groups)
    {
        if (index >= lines.Count || lines[index].Indent <= parentIndent)
        {
            return;
        }

        int groupIndent = lines[index].Indent;
        while (index < lines.Count && lines[index].Indent >= groupIndent)
        {
            if (lines[index].Indent != groupIndent || !TrySplitKeyValue(lines[index].Text, out string groupName, out _))
            {
                index++;
                continue;
            }

            string cleanName = UnquoteScalar(groupName);
            index++;
            var patterns = new List<string>();
            var excludePatterns = new List<string>();
            var updateTypes = new List<string>();

            while (index < lines.Count && lines[index].Indent > groupIndent)
            {
                int attrIndent = lines[index].Indent;
                if (!TrySplitKeyValue(lines[index].Text, out string attrKey, out string attrRest))
                {
                    index++;
                    continue;
                }

                index++;
                var list = ReadStringList(lines, ref index, attrIndent, attrRest);
                if (attrKey == "patterns")
                {
                    patterns.AddRange(list);
                }
                else if (attrKey == "exclude-patterns")
                {
                    excludePatterns.AddRange(list);
                }
                else if (attrKey == "update-types")
                {
                    updateTypes.AddRange(list);
                }
            }

            groups.Add(new DependabotGroup(cleanName, patterns, excludePatterns, updateTypes));
        }
    }

    private static void ParseIgnoreSection(
        List<YamlLine> lines,
        ref int index,
        int parentIndent,
        List<DependabotIgnore> ignores)
    {
        if (index >= lines.Count || lines[index].Indent <= parentIndent)
        {
            return;
        }

        int seqIndent = lines[index].Indent;
        while (index < lines.Count && lines[index].Indent >= seqIndent)
        {
            if (lines[index].Indent != seqIndent || !lines[index].Text.StartsWith("- ", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            int itemStart = index;
            index++;
            while (index < lines.Count &&
                   (lines[index].Indent > seqIndent ||
                    lines[index].Indent == seqIndent && !lines[index].Text.StartsWith("- ", StringComparison.Ordinal)))
            {
                index++;
            }

            ignores.Add(ParseSingleIgnoreEntry(lines, itemStart, index, seqIndent));
        }
    }

    private static DependabotIgnore ParseSingleIgnoreEntry(List<YamlLine> lines, int start, int end, int seqIndent)
    {
        int attrIndent = seqIndent + 2;
        var entryLines = new List<YamlLine>(end - start)
        {
            new(lines[start].Number, attrIndent, lines[start].Text[2..].TrimStart()),
        };
        for (int k = start + 1; k < end; k++)
        {
            entryLines.Add(lines[k]);
        }

        string depName = "";
        var updateTypes = new List<string>();
        var versions = new List<string>();
        int idx = 0;
        while (idx < entryLines.Count)
        {
            int curIndent = entryLines[idx].Indent;
            if (!TrySplitKeyValue(entryLines[idx].Text, out string key, out string rest))
            {
                idx++;
                continue;
            }

            idx++;
            if (key == "dependency-name")
            {
                depName = UnquoteScalar(rest);
            }
            else if (key == "update-types")
            {
                updateTypes.AddRange(ReadStringList(entryLines, ref idx, curIndent, rest));
            }
            else if (key == "versions")
            {
                versions.AddRange(ReadStringList(entryLines, ref idx, curIndent, rest));
            }
        }

        return new DependabotIgnore(depName, updateTypes, versions);
    }

    private static List<string> ReadStringList(
        List<YamlLine> lines,
        ref int index,
        int keyIndent,
        string inlineRest)
    {
        var values = new List<string>();
        string trimmed = inlineRest.Trim();
        if (trimmed.StartsWith('['))
        {
            var flow = new StringBuilder(trimmed);
            while (!trimmed.EndsWith(']') && index < lines.Count && lines[index].Indent > keyIndent)
            {
                trimmed = lines[index].Text.Trim();
                flow.Append(' ').Append(trimmed);
                index++;
            }

            ParseFlowArray(flow.ToString(), values);
            return values;
        }

        if (trimmed.Length > 0)
        {
            values.Add(UnquoteScalar(trimmed));
            return values;
        }

        while (index < lines.Count && lines[index].Indent > keyIndent)
        {
            string itemText = lines[index].Text;
            if (itemText.StartsWith('['))
            {
                var flow = new StringBuilder(itemText);
                index++;
                while (!itemText.EndsWith(']') && index < lines.Count && lines[index].Indent > keyIndent)
                {
                    itemText = lines[index].Text.Trim();
                    flow.Append(' ').Append(itemText);
                    index++;
                }

                ParseFlowArray(flow.ToString(), values);
                continue;
            }

            if (itemText.StartsWith("- ", StringComparison.Ordinal))
            {
                values.Add(UnquoteScalar(itemText[2..]));
            }

            index++;
        }

        return values;
    }

    private static void ParseFlowArray(string flowText, List<string> target)
    {
        string inner = flowText.Trim();
        if (inner.StartsWith('['))
        {
            inner = inner[1..];
        }

        if (inner.EndsWith(']'))
        {
            inner = inner[..^1];
        }

        var token = new StringBuilder();
        char quote = '\0';
        foreach (char ch in inner)
        {
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
                else
                {
                    token.Append(ch);
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == ',')
            {
                AddTrimmedToken(token, target);
            }
            else
            {
                token.Append(ch);
            }
        }

        AddTrimmedToken(token, target);
    }

    private static void AddTrimmedToken(StringBuilder token, List<string> target)
    {
        string value = token.ToString().Trim();
        token.Clear();
        if (value.Length > 0)
        {
            target.Add(value);
        }
    }

    private static List<YamlLine> ReadPreprocessedYamlLines(string path)
    {
        var result = new List<YamlLine>();
        string[] rawLines = File.ReadAllLines(path);
        for (int i = 0; i < rawLines.Length; i++)
        {
            string stripped = StripYamlComment(rawLines[i]).TrimEnd();
            if (string.IsNullOrWhiteSpace(stripped))
            {
                continue;
            }

            int indent = 0;
            while (indent < stripped.Length && stripped[indent] == ' ')
            {
                indent++;
            }

            result.Add(new YamlLine(i + 1, indent, stripped[indent..]));
        }

        return result;
    }

    private static bool TrySplitKeyValue(string text, out string key, out string value)
    {
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            key = "";
            value = "";
            return false;
        }

        key = text[..colon].Trim();
        value = text[(colon + 1)..].Trim();
        return true;
    }

    private static string StripYamlComment(string line)
    {
        char quote = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string UnquoteScalar(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            (trimmed[0] == '"' && trimmed[^1] == '"' || trimmed[0] == '\'' && trimmed[^1] == '\''))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static bool UsesCaseInsensitiveMatching(string ecosystem) =>
        string.Equals(ecosystem, "nuget", StringComparison.Ordinal) ||
        string.Equals(ecosystem, "github-actions", StringComparison.Ordinal);

    private static bool GlobMatches(string pattern, string candidate, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        if (pattern == "*")
        {
            return true;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string[] parts = pattern.Split('*');
        if (parts.Length == 1)
        {
            return string.Equals(pattern, candidate, comparison);
        }

        int pos = 0;
        if (parts[0].Length > 0)
        {
            if (!candidate.StartsWith(parts[0], comparison))
            {
                return false;
            }

            pos = parts[0].Length;
        }

        for (int i = 1; i < parts.Length - 1; i++)
        {
            if (parts[i].Length == 0)
            {
                continue;
            }

            int found = candidate.IndexOf(parts[i], pos, comparison);
            if (found < 0)
            {
                return false;
            }

            pos = found + parts[i].Length;
        }

        string tail = parts[^1];
        return tail.Length == 0 ||
               candidate.Length - pos >= tail.Length && candidate.EndsWith(tail, comparison);
    }

    private static List<string> DiscoverPackageManifests(string repoRoot)
    {
        var manifests = new List<string>();

        string rootPkg = Path.Combine(repoRoot, "package.json");
        if (File.Exists(rootPkg))
        {
            manifests.Add(rootPkg);
        }

        foreach (string container in new[] { "apps", "packages" })
        {
            string containerPath = Path.Combine(repoRoot, container);
            if (!Directory.Exists(containerPath))
            {
                continue;
            }

            foreach (string childDir in Directory.GetDirectories(containerPath).OrderBy(d => d, StringComparer.Ordinal))
            {
                string childPkg = Path.Combine(childDir, "package.json");
                if (File.Exists(childPkg))
                {
                    manifests.Add(childPkg);
                }
            }
        }

        return manifests;
    }

    private static int ValidatePackageManifest(string pkgPath, string relativePath)
    {
        try
        {
            string json = File.ReadAllText(pkgPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine($"[ERROR] {relativePath} root element must be a JSON object.");
                return 1;
            }

            int errors = 0;
            foreach (string section in DependencySections)
            {
                if (!root.TryGetProperty(section, out var depsObj))
                {
                    continue;
                }

                if (depsObj.ValueKind != JsonValueKind.Object)
                {
                    Console.WriteLine($"[ERROR] {relativePath} property '{section}' must be a JSON object.");
                    errors++;
                    continue;
                }

                foreach (var entry in depsObj.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Value.GetString()))
                    {
                        Console.WriteLine($"[ERROR] {relativePath} dependency '{entry.Name}' in '{section}' must specify a non-empty version string.");
                        errors++;
                    }
                }

                foreach (string forbidden in ForbiddenPackages)
                {
                    if (depsObj.TryGetProperty(forbidden, out _))
                    {
                        Console.WriteLine($"[SECURITY VIOLATION] Forbidden client-side crypto package '{forbidden}' detected in {relativePath} ({section}).");
                        errors++;
                    }
                }
            }

            return errors;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.WriteLine($"[ERROR] Failed to parse {relativePath}: {ex.Message}");
            return 1;
        }
    }

    private static int ValidateDotnetToolsManifest(string dotnetToolsPath)
    {
        const string relativePath = ".config/dotnet-tools.json";
        try
        {
            string json = File.ReadAllText(dotnetToolsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine($"[ERROR] {relativePath} root element must be a JSON object.");
                return 1;
            }

            if (!root.TryGetProperty("tools", out var toolsObj) || toolsObj.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine($"[ERROR] {relativePath} must contain a 'tools' JSON object.");
                return 1;
            }

            int errors = 0;
            foreach (var tool in toolsObj.EnumerateObject())
            {
                if (tool.Value.ValueKind != JsonValueKind.Object ||
                    !tool.Value.TryGetProperty("version", out var versionProp) ||
                    versionProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(versionProp.GetString()))
                {
                    Console.WriteLine($"[ERROR] {relativePath} tool '{tool.Name}' must define a non-empty string 'version'.");
                    errors++;
                }
            }

            return errors;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.WriteLine($"[ERROR] Failed to parse {relativePath}: {ex.Message}");
            return 1;
        }
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    [GeneratedRegex(@"^\s*FROM\s+(?:--platform=\S+\s+)?(?<image>\S+)(?:\s+AS\s+(?<alias>\S+))?\s*$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DockerfileFromRegex();

    [GeneratedRegex(@"^\s*image:\s*(?<image>\S+)\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ComposeImageRegex();

    [GeneratedRegex(@"^\s*(?:-\s*)?uses:\s*(?<uses>\S+)\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WorkflowUsesRegex();
}
