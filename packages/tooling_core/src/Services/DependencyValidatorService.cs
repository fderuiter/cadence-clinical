using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Cadence.ToolingCore.Services;

public static class DependencyValidatorService
{
    private const string DependabotRelativePath = ".github/dependabot.yml";
    private static readonly string[] ForbiddenPackages = ["node-forge", "elliptic", "jose"];
    private static readonly string[] ForbiddenRuntimeTestPackages =
    [
        "axe-core",
        "@axe-core/playwright",
        "vitest",
        "@vue/test-utils",
        "jsdom",
        "fake-indexeddb",
        "msw",
    ];
    private static readonly string[] DependencySections = ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"];

    public static int ValidateDependencies(
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating dependency manifests and checking frontend dependencies for forbidden cryptographic libraries...");

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var packageManifests = DiscoverPackageManifests(repoRoot);
            string dotnetToolsPath = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
            bool hasDotnetTools = File.Exists(dotnetToolsPath);
            string? dependabotContent = ReadDependabotContent(repoRoot, reader);

            if (packageManifests.Count == 0 && !hasDotnetTools && dependabotContent is null)
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

            if (dependabotContent is not null)
            {
                errors += ValidateDependabotManifest(dependabotContent);
            }

            if (errors > 0)
            {
                Console.WriteLine($"\n[ERROR] Found {errors} dependency manifest or security violation(s).");
                return 1;
            }

            Console.WriteLine("✔ Dependency manifests and frontend crypto checks passed cleanly.");
            return 0;
        }
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

                if (string.Equals(section, "dependencies", StringComparison.Ordinal))
                {
                    foreach (string testPkg in ForbiddenRuntimeTestPackages)
                    {
                        if (depsObj.TryGetProperty(testPkg, out _))
                        {
                            Console.WriteLine($"[DEPENDENCY VIOLATION] Test-only package '{testPkg}' is forbidden in runtime 'dependencies' of {relativePath}; move it to 'devDependencies'.");
                            errors++;
                        }
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

    private static string? ReadDependabotContent(string repoRoot, GitIndexReader? reader)
    {
        if (reader is not null)
        {
            return reader.TryReadBytes(DependabotRelativePath, out byte[] stagedBytes)
                ? Encoding.UTF8.GetString(stagedBytes)
                : null;
        }

        string dependabotPath = Path.Combine(repoRoot, ".github", "dependabot.yml");
        return File.Exists(dependabotPath) ? File.ReadAllText(dependabotPath) : null;
    }

    private static int ValidateDependabotManifest(string content)
    {
        var syntaxErrors = YamlSyntaxValidatorService.ValidateContent(content, DependabotRelativePath);
        if (syntaxErrors.Count > 0)
        {
            foreach (string syntaxError in syntaxErrors)
            {
                Console.WriteLine($"[ERROR] {syntaxError}");
            }

            return syntaxErrors.Count;
        }

        var parseErrors = new List<string>();
        var parsed = ParseDependabotConfig(content, parseErrors);
        if (parseErrors.Count > 0)
        {
            foreach (string parseError in parseErrors)
            {
                Console.WriteLine($"[ERROR] {DependabotRelativePath}: {parseError}");
            }

            return parseErrors.Count;
        }

        int errors = 0;
        if (!string.Equals(parsed.Version, "2", StringComparison.Ordinal))
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: Top-level 'version' must be 2.");
            errors++;
        }

        if (parsed.Updates.Count == 0)
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: 'updates' must define at least one package-ecosystem entry.");
            return errors + 1;
        }

        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        for (int u = 0; u < parsed.Updates.Count; u++)
        {
            errors += ValidateDependabotUpdateEntry(parsed.Updates[u], u, seenTargets);
        }

        return errors;
    }

    private static int ValidateDependabotUpdateEntry(
        DependabotUpdateEntry update,
        int updateIndex,
        HashSet<string> seenTargets)
    {
        int errors = 0;
        if (string.IsNullOrWhiteSpace(update.PackageEcosystem))
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: '#/updates/{updateIndex}' is missing a non-empty 'package-ecosystem'.");
            errors++;
        }

        var directories = ResolveUpdateDirectories(update);
        if (directories.Count == 0)
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: '#/updates/{updateIndex}' must specify 'directory' or 'directories'.");
            errors++;
        }

        string targetBranch = update.TargetBranch ?? string.Empty;
        foreach (string dir in directories)
        {
            string targetKey = $"{update.PackageEcosystem}\0{dir}\0{targetBranch}";
            if (!seenTargets.Add(targetKey))
            {
                Console.WriteLine($"[DEPENDABOT VIOLATION] {DependabotRelativePath}: '#/updates/{updateIndex}' duplicates package-ecosystem '{update.PackageEcosystem}' and directory '{dir}'.");
                errors++;
            }
        }

        var seenVersionIgnores = new HashSet<string>(StringComparer.Ordinal);
        var seenUpdateTypeIgnores = new HashSet<string>(StringComparer.Ordinal);
        var seenBareIgnores = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < update.Ignores.Count; i++)
        {
            errors += ValidateDependabotIgnoreEntry(
                update.Ignores[i],
                updateIndex,
                i,
                seenVersionIgnores,
                seenUpdateTypeIgnores,
                seenBareIgnores);
        }

        return errors;
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

    private static int ValidateDependabotIgnoreEntry(
        DependabotIgnoreEntry ignore,
        int updateIndex,
        int ignoreIndex,
        HashSet<string> seenVersionIgnores,
        HashSet<string> seenUpdateTypeIgnores,
        HashSet<string> seenBareIgnores)
    {
        if (string.IsNullOrWhiteSpace(ignore.DependencyName))
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: '#/updates/{updateIndex}/ignore/{ignoreIndex}' is missing a non-empty 'dependency-name'.");
            return 1;
        }

        int errors = 0;
        string depName = ignore.DependencyName!;

        if (ignore.HasVersions)
        {
            errors += ValidateIgnoreListUniqueness(
                depName,
                ignore.Versions,
                propertyName: "versions",
                updateIndex,
                ignoreIndex,
                seenVersionIgnores);
        }

        if (ignore.HasUpdateTypes)
        {
            errors += ValidateIgnoreListUniqueness(
                depName,
                ignore.UpdateTypes,
                propertyName: "update-types",
                updateIndex,
                ignoreIndex,
                seenUpdateTypeIgnores);
        }

        if (!ignore.HasVersions && !ignore.HasUpdateTypes && !seenBareIgnores.Add(depName))
        {
            Console.WriteLine(
                $"[DEPENDABOT VIOLATION] {DependabotRelativePath}: The property '#/updates/{updateIndex}/ignore/{ignoreIndex}/dependency-name' includes a duplicate ignore for '{depName}'.");
            errors++;
        }

        return errors;
    }

    private static int ValidateIgnoreListUniqueness(
        string depName,
        List<string> values,
        string propertyName,
        int updateIndex,
        int ignoreIndex,
        HashSet<string> seenCombinations)
    {
        if (values.Count == 0)
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: '#/updates/{updateIndex}/ignore/{ignoreIndex}/{propertyName}' must not be empty.");
            return 1;
        }

        int errors = 0;
        var uniqueItems = new HashSet<string>(values, StringComparer.Ordinal);
        if (uniqueItems.Count != values.Count)
        {
            Console.WriteLine(
                $"[DEPENDABOT VIOLATION] {DependabotRelativePath}: The property '#/updates/{updateIndex}/ignore/{ignoreIndex}/{propertyName}' contains duplicate values.");
            errors++;
        }

        bool hasDuplicateCombination = false;
        foreach (string item in uniqueItems)
        {
            if (!seenCombinations.Add($"{depName}\0{item}"))
            {
                hasDuplicateCombination = true;
            }
        }

        if (hasDuplicateCombination)
        {
            Console.WriteLine(
                $"[DEPENDABOT VIOLATION] {DependabotRelativePath}: The property '#/updates/{updateIndex}/ignore/{ignoreIndex}/{propertyName}' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and '{propertyName}'.");
            errors++;
        }

        return errors;
    }

    private static ParsedDependabotConfig ParseDependabotConfig(string content, List<string> parseErrors)
    {
        string[] rawLines = content.ReplaceLineEndings("\n").Split('\n');
        string[] lines = new string[rawLines.Length];
        for (int i = 0; i < rawLines.Length; i++)
        {
            lines[i] = StripYamlLineComment(rawLines[i]);
        }

        var config = new ParsedDependabotConfig();
        bool inUpdates = false;
        int updatesIndent = -1;
        int updateItemIndent = -1;
        DependabotUpdateEntry? currentUpdate = null;

        bool inIgnore = false;
        int ignoreKeyIndent = -1;
        int ignoreItemIndent = -1;
        DependabotIgnoreEntry? currentIgnore = null;

        int lineIdx = 0;
        while (lineIdx < lines.Length)
        {
            string line = lines[lineIdx];
            if (string.IsNullOrWhiteSpace(line))
            {
                lineIdx++;
                continue;
            }

            int indent = CountLeadingSpaces(line);
            string trimmed = line[indent..].TrimEnd();
            if (trimmed is "---" or "...")
            {
                lineIdx++;
                continue;
            }

            if (indent == 0)
            {
                inUpdates = false;
                inIgnore = false;
                currentUpdate = null;
                currentIgnore = null;

                if (TrySplitMapping(trimmed, out string rootKey, out string rootVal))
                {
                    if (string.Equals(rootKey, "version", StringComparison.Ordinal))
                    {
                        config.Version = UnquoteYamlScalar(rootVal);
                    }
                    else if (string.Equals(rootKey, "updates", StringComparison.Ordinal))
                    {
                        inUpdates = true;
                        updatesIndent = 0;
                        updateItemIndent = -1;
                    }
                }

                lineIdx++;
                continue;
            }

            if (!inUpdates || indent <= updatesIndent)
            {
                lineIdx++;
                continue;
            }

            if (IsSequenceItem(trimmed, out string afterUpdateDash) &&
                (updateItemIndent == -1 || indent == updateItemIndent))
            {
                updateItemIndent = indent;
                currentUpdate = new DependabotUpdateEntry();
                config.Updates.Add(currentUpdate);
                inIgnore = false;
                ignoreKeyIndent = -1;
                ignoreItemIndent = -1;
                currentIgnore = null;

                if (afterUpdateDash.Length > 0)
                {
                    int virtualIndent = indent + (trimmed.Length - afterUpdateDash.Length);
                    ApplyUpdatePropertyLine(lines, ref lineIdx, virtualIndent, afterUpdateDash, currentUpdate, ref inIgnore, ref ignoreKeyIndent, ref ignoreItemIndent, parseErrors);
                }

                lineIdx++;
                continue;
            }

            if (currentUpdate is null)
            {
                lineIdx++;
                continue;
            }

            if (inIgnore && indent > ignoreKeyIndent)
            {
                if (IsSequenceItem(trimmed, out string afterIgnoreDash) &&
                    (ignoreItemIndent == -1 || indent == ignoreItemIndent))
                {
                    ignoreItemIndent = indent;
                    currentIgnore = new DependabotIgnoreEntry();
                    currentUpdate.Ignores.Add(currentIgnore);

                    if (afterIgnoreDash.Length > 0)
                    {
                        int virtualIndent = indent + (trimmed.Length - afterIgnoreDash.Length);
                        ApplyIgnorePropertyLine(lines, ref lineIdx, virtualIndent, afterIgnoreDash, currentIgnore, parseErrors);
                    }

                    lineIdx++;
                    continue;
                }

                if (currentIgnore is not null && (ignoreItemIndent == -1 || indent > ignoreItemIndent))
                {
                    ApplyIgnorePropertyLine(lines, ref lineIdx, indent, trimmed, currentIgnore, parseErrors);
                }

                lineIdx++;
                continue;
            }

            if (inIgnore && indent <= ignoreKeyIndent)
            {
                inIgnore = false;
                currentIgnore = null;
            }

            if (indent > updateItemIndent)
            {
                ApplyUpdatePropertyLine(lines, ref lineIdx, indent, trimmed, currentUpdate, ref inIgnore, ref ignoreKeyIndent, ref ignoreItemIndent, parseErrors);
            }

            lineIdx++;
        }

        return config;
    }

    private static void ApplyUpdatePropertyLine(
        string[] lines,
        ref int lineIdx,
        int propIndent,
        string trimmedLine,
        DependabotUpdateEntry update,
        ref bool inIgnore,
        ref int ignoreKeyIndent,
        ref int ignoreItemIndent,
        List<string> parseErrors)
    {
        if (!TrySplitMapping(trimmedLine, out string key, out string val))
        {
            return;
        }

        switch (key)
        {
            case "package-ecosystem":
                update.PackageEcosystem = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "directory":
                update.Directory = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "directories":
                update.Directories.AddRange(ReadStringListValue(lines, ref lineIdx, propIndent, val, parseErrors));
                break;
            case "target-branch":
                update.TargetBranch = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "ignore":
                inIgnore = true;
                ignoreKeyIndent = propIndent;
                ignoreItemIndent = -1;
                break;
        }
    }

    private static void ApplyIgnorePropertyLine(
        string[] lines,
        ref int lineIdx,
        int propIndent,
        string trimmedLine,
        DependabotIgnoreEntry ignore,
        List<string> parseErrors)
    {
        if (!TrySplitMapping(trimmedLine, out string key, out string val))
        {
            return;
        }

        switch (key)
        {
            case "dependency-name":
                ignore.DependencyName = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "versions":
                ignore.HasVersions = true;
                ignore.Versions.AddRange(ReadStringListValue(lines, ref lineIdx, propIndent, val, parseErrors));
                break;
            case "update-types":
                ignore.HasUpdateTypes = true;
                ignore.UpdateTypes.AddRange(ReadStringListValue(lines, ref lineIdx, propIndent, val, parseErrors));
                break;
        }
    }

    private static string ReadScalarValue(string[] lines, ref int lineIdx, int propIndent, string inlineVal)
    {
        if (!string.IsNullOrWhiteSpace(inlineVal))
        {
            return UnquoteYamlScalar(inlineVal);
        }

        int next = PeekNextContentLine(lines, lineIdx + 1);
        if (next >= 0)
        {
            int nextIndent = CountLeadingSpaces(lines[next]);
            string nextTrimmed = lines[next][nextIndent..].Trim();
            if (nextIndent > propIndent && !nextTrimmed.StartsWith('-') && !TrySplitMapping(nextTrimmed, out _, out _))
            {
                lineIdx = next;
                return UnquoteYamlScalar(nextTrimmed);
            }
        }

        return string.Empty;
    }

    private static List<string> ReadStringListValue(
        string[] lines,
        ref int lineIdx,
        int propIndent,
        string inlineVal,
        List<string> parseErrors)
    {
        string trimmedInline = inlineVal.Trim();
        if (trimmedInline.StartsWith('['))
        {
            return ParseFlowStringArray(lines, ref lineIdx, trimmedInline, parseErrors);
        }

        if (trimmedInline.Length > 0)
        {
            parseErrors.Add($"Expected YAML sequence on line {lineIdx + 1}, found scalar '{trimmedInline}'.");
            return [];
        }

        int next = PeekNextContentLine(lines, lineIdx + 1);
        if (next < 0)
        {
            return [];
        }

        int nextIndent = CountLeadingSpaces(lines[next]);
        if (nextIndent <= propIndent)
        {
            return [];
        }

        string nextTrimmed = lines[next][nextIndent..].Trim();
        if (nextTrimmed.StartsWith('['))
        {
            lineIdx = next;
            return ParseFlowStringArray(lines, ref lineIdx, nextTrimmed, parseErrors);
        }

        var items = new List<string>();
        int seqIndent = -1;
        while (true)
        {
            int peek = PeekNextContentLine(lines, lineIdx + 1);
            if (peek < 0)
            {
                break;
            }

            int peekIndent = CountLeadingSpaces(lines[peek]);
            if (peekIndent <= propIndent)
            {
                break;
            }

            string peekTrimmed = lines[peek][peekIndent..].TrimEnd();
            if (!IsSequenceItem(peekTrimmed, out string itemText))
            {
                break;
            }

            if (seqIndent == -1)
            {
                seqIndent = peekIndent;
            }
            else if (peekIndent != seqIndent)
            {
                break;
            }

            lineIdx = peek;
            items.Add(UnquoteYamlScalar(itemText));
        }

        return items;
    }

    private static List<string> ParseFlowStringArray(
        string[] lines,
        ref int lineIdx,
        string initialSegment,
        List<string> parseErrors)
    {
        var combined = new StringBuilder(initialSegment);
        while (!HasBalancedClosingBracket(combined.ToString()))
        {
            if (lineIdx + 1 >= lines.Length)
            {
                parseErrors.Add("Unclosed flow sequence '[' in dependabot configuration.");
                return [];
            }

            lineIdx++;
            combined.Append(' ').Append(lines[lineIdx].Trim());
        }

        string full = combined.ToString().Trim();
        int closeIdx = FindMatchingCloseBracket(full);
        if (closeIdx < 0)
        {
            parseErrors.Add("Malformed flow sequence '[' in dependabot configuration.");
            return [];
        }

        string inner = full[1..closeIdx];
        var result = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';

        int i = 0;
        while (i < inner.Length)
        {
            char c = inner[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < inner.Length)
                {
                    current.Append(c).Append(inner[i + 1]);
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < inner.Length && inner[i + 1] == '\'')
                {
                    current.Append("''");
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                current.Append(c);
                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                current.Append(c);
                i++;
                continue;
            }

            if (c == ',')
            {
                string part = current.ToString().Trim();
                if (part.Length > 0)
                {
                    result.Add(UnquoteYamlScalar(part));
                }

                current.Clear();
                i++;
                continue;
            }

            current.Append(c);
            i++;
        }

        string tail = current.ToString().Trim();
        if (tail.Length > 0)
        {
            result.Add(UnquoteYamlScalar(tail));
        }

        return result;
    }

    private static bool HasBalancedClosingBracket(string text) => FindMatchingCloseBracket(text) >= 0;

    private static int FindMatchingCloseBracket(string text)
    {
        int depth = 0;
        char quote = '\0';
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }

            i++;
        }

        return -1;
    }

    private static int PeekNextContentLine(string[] lines, int startIdx)
    {
        for (int i = startIdx; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSequenceItem(string trimmed, out string remainder)
    {
        if (string.Equals(trimmed, "-", StringComparison.Ordinal))
        {
            remainder = string.Empty;
            return true;
        }

        if (trimmed.Length > 1 && trimmed[0] == '-' && (trimmed[1] == ' ' || trimmed[1] == '\t'))
        {
            remainder = trimmed[1..].TrimStart(' ', '\t');
            return true;
        }

        remainder = string.Empty;
        return false;
    }

    private static bool TrySplitMapping(string segment, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        char quote = '\0';
        int flowDepth = 0;

        int i = 0;
        while (i < segment.Length)
        {
            char c = segment[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < segment.Length)
                {
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < segment.Length && segment[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '[' or '{')
            {
                flowDepth++;
            }
            else if (c is ']' or '}' && flowDepth > 0)
            {
                flowDepth--;
            }
            else if (flowDepth == 0 && c == ':' && (i + 1 == segment.Length || segment[i + 1] is ' ' or '\t'))
            {
                key = UnquoteYamlScalar(segment[..i].Trim());
                value = segment[(i + 1)..].Trim();
                return key.Length > 0;
            }

            i++;
        }

        return false;
    }

    private static string StripYamlLineComment(string line)
    {
        char quote = '\0';
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < line.Length)
                {
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < line.Length && line[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                i++;
                continue;
            }

            if (c == '#' && (i == 0 || line[i - 1] is ' ' or '\t'))
            {
                return line[..i].TrimEnd();
            }

            i++;
        }

        return line.TrimEnd();
    }

    private static string UnquoteYamlScalar(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static int CountLeadingSpaces(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
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
}
