using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Cadence.ToolingCore.Services;

public static class DependencyValidatorService
{
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

    public static int ValidateDependencies(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating dependency manifests and checking frontend dependencies for forbidden cryptographic libraries...");

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
            errors += ValidateDependabotManifest(dependabotPath);
        }

        if (errors > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {errors} dependency manifest or security violation(s).");
            return 1;
        }

        Console.WriteLine("✔ Dependency manifests and frontend crypto checks passed cleanly.");
        return 0;
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

    private static int ValidateDependabotManifest(string dependabotPath)
    {
        const string relativePath = ".github/dependabot.yml";
        IReadOnlyList<string> yamlErrors = YamlSyntaxValidatorService.ValidateFile(dependabotPath, relativePath);
        if (yamlErrors.Count > 0)
        {
            foreach (string yamlError in yamlErrors)
            {
                Console.WriteLine($"[ERROR] {yamlError}");
            }

            return yamlErrors.Count;
        }

        try
        {
            string[] rawLines = File.ReadAllLines(dependabotPath);
            List<DependabotUpdateEntry> updates = ParseDependabotUpdates(rawLines);
            int errors = 0;
            var seenUpdateTargets = new HashSet<(string Ecosystem, string Directory, string TargetBranch)>();

            for (int u = 0; u < updates.Count; u++)
            {
                DependabotUpdateEntry update = updates[u];
                List<string> directories = update.Directories;
                if (directories.Count == 0 && !string.IsNullOrEmpty(update.Directory))
                {
                    directories = [update.Directory];
                }

                foreach (string dir in directories)
                {
                    var targetKey = (
                        update.PackageEcosystem.ToLowerInvariant(),
                        dir,
                        update.TargetBranch);
                    if (!string.IsNullOrEmpty(update.PackageEcosystem) && !seenUpdateTargets.Add(targetKey))
                    {
                        Console.WriteLine(
                            $"[ERROR] {relativePath}: The property '#/updates/{u}' includes a duplicate ({update.PackageEcosystem}, {dir}).");
                        errors++;
                    }
                }

                errors += ValidateDependabotIgnores(relativePath, u, update.Ignores);
            }

            return errors;
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {ex.Message}");
            return 1;
        }
    }

    private static int ValidateDependabotIgnores(
        string relativePath,
        int updateIndex,
        List<DependabotIgnoreEntry> ignores)
    {
        int errors = 0;
        var seenDependencyVersions = new HashSet<(string Dependency, string Version)>();
        var seenDependencyUpdateTypes = new HashSet<(string Dependency, string UpdateType)>();
        var seenWildcardDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < ignores.Count; i++)
        {
            DependabotIgnoreEntry ignore = ignores[i];
            if (string.IsNullOrWhiteSpace(ignore.DependencyName))
            {
                Console.WriteLine(
                    $"[ERROR] {relativePath}: The property '#/updates/{updateIndex}/ignore/{i}' must define a non-empty 'dependency-name'.");
                errors++;
                continue;
            }

            string depKey = ignore.DependencyName.ToLowerInvariant();
            if (ignore.HasVersions)
            {
                bool reportedDuplicateVersion = false;
                foreach (string version in ignore.Versions)
                {
                    if (!seenDependencyVersions.Add((depKey, version)) && !reportedDuplicateVersion)
                    {
                        Console.WriteLine(
                            $"[ERROR] {relativePath}: The property '#/updates/{updateIndex}/ignore/{i}/versions' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and 'versions'.");
                        errors++;
                        reportedDuplicateVersion = true;
                    }
                }
            }

            if (ignore.HasUpdateTypes)
            {
                bool reportedDuplicateUpdateType = false;
                foreach (string updateType in ignore.UpdateTypes)
                {
                    if (!seenDependencyUpdateTypes.Add((depKey, updateType)) && !reportedDuplicateUpdateType)
                    {
                        Console.WriteLine(
                            $"[ERROR] {relativePath}: The property '#/updates/{updateIndex}/ignore/{i}/update-types' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and 'update-types'.");
                        errors++;
                        reportedDuplicateUpdateType = true;
                    }
                }
            }

            if (!ignore.HasVersions && !ignore.HasUpdateTypes && !seenWildcardDependencies.Add(depKey))
            {
                Console.WriteLine(
                    $"[ERROR] {relativePath}: The property '#/updates/{updateIndex}/ignore/{i}/dependency-name' includes a duplicate wildcard ignore for '{ignore.DependencyName}'.");
                errors++;
            }
        }

        return errors;
    }

    private static List<DependabotUpdateEntry> ParseDependabotUpdates(string[] rawLines)
    {
        var entries = new List<(int Indent, string Text)>();
        foreach (string rawLine in rawLines)
        {
            string stripped = StripYamlComment(rawLine);
            if (string.IsNullOrWhiteSpace(stripped))
            {
                continue;
            }

            int indent = CountLeadingSpaces(stripped);
            entries.Add((indent, stripped[indent..]));
        }

        var updates = new List<DependabotUpdateEntry>();
        int pos = 0;
        while (pos < entries.Count)
        {
            if (entries[pos].Indent == 0 &&
                TrySplitYamlKeyValue(entries[pos].Text, out string rootKey, out string rootVal) &&
                string.Equals(rootKey, "updates", StringComparison.Ordinal))
            {
                if (rootVal.StartsWith('['))
                {
                    ConsumeFlowSequence(entries, ref pos, rootVal);
                    return updates;
                }

                pos++;
                ParseUpdateItems(entries, ref pos, updates);
                return updates;
            }

            pos++;
        }

        return updates;
    }

    private static void ParseUpdateItems(
        List<(int Indent, string Text)> entries,
        ref int pos,
        List<DependabotUpdateEntry> updates)
    {
        if (pos >= entries.Count || entries[pos].Indent == 0)
        {
            return;
        }

        int updateItemIndent = entries[pos].Indent;
        while (pos < entries.Count && entries[pos].Indent >= updateItemIndent)
        {
            if (entries[pos].Indent > updateItemIndent ||
                !TrySplitSequenceEntry(entries[pos].Text, out string afterDash, out int spacesAfterDash))
            {
                pos++;
                continue;
            }

            var update = new DependabotUpdateEntry();
            int updatePropIndent;
            if (afterDash.Length > 0)
            {
                updatePropIndent = updateItemIndent + 1 + spacesAfterDash;
                ReadUpdateProperty(entries, ref pos, updatePropIndent, afterDash, update);
            }
            else
            {
                pos++;
                updatePropIndent = pos < entries.Count && entries[pos].Indent > updateItemIndent
                    ? entries[pos].Indent
                    : updateItemIndent + 2;
            }

            while (pos < entries.Count && entries[pos].Indent > updateItemIndent)
            {
                if (entries[pos].Indent == updatePropIndent)
                {
                    ReadUpdateProperty(entries, ref pos, updatePropIndent, entries[pos].Text, update);
                }
                else
                {
                    pos++;
                }
            }

            updates.Add(update);
        }
    }

    private static void ReadUpdateProperty(
        List<(int Indent, string Text)> entries,
        ref int pos,
        int propIndent,
        string propText,
        DependabotUpdateEntry update)
    {
        if (!TrySplitYamlKeyValue(propText, out string key, out string val))
        {
            pos++;
            return;
        }

        switch (key)
        {
            case "package-ecosystem":
                update.PackageEcosystem = UnquoteYamlScalar(val);
                pos++;
                break;
            case "directory":
                update.Directory = UnquoteYamlScalar(val);
                pos++;
                break;
            case "target-branch":
                update.TargetBranch = UnquoteYamlScalar(val);
                pos++;
                break;
            case "directories":
                update.Directories.AddRange(ReadStringSequence(entries, ref pos, propIndent, val));
                break;
            case "ignore":
                update.Ignores.AddRange(ReadIgnoreItems(entries, ref pos, propIndent, val));
                break;
            default:
                pos++;
                break;
        }
    }

    private static List<DependabotIgnoreEntry> ReadIgnoreItems(
        List<(int Indent, string Text)> entries,
        ref int pos,
        int ignoreKeyIndent,
        string inlineVal)
    {
        var ignores = new List<DependabotIgnoreEntry>();
        if (inlineVal.StartsWith('['))
        {
            ConsumeFlowSequence(entries, ref pos, inlineVal);
            return ignores;
        }

        pos++;
        if (pos >= entries.Count || entries[pos].Indent <= ignoreKeyIndent)
        {
            return ignores;
        }

        int ignoreItemIndent = entries[pos].Indent;
        while (pos < entries.Count && entries[pos].Indent >= ignoreItemIndent)
        {
            if (entries[pos].Indent > ignoreItemIndent ||
                !TrySplitSequenceEntry(entries[pos].Text, out string afterDash, out int spacesAfterDash))
            {
                pos++;
                continue;
            }

            var item = new DependabotIgnoreEntry();
            int itemPropIndent;
            if (afterDash.Length > 0)
            {
                itemPropIndent = ignoreItemIndent + 1 + spacesAfterDash;
                ReadIgnoreProperty(entries, ref pos, itemPropIndent, afterDash, item);
            }
            else
            {
                pos++;
                itemPropIndent = pos < entries.Count && entries[pos].Indent > ignoreItemIndent
                    ? entries[pos].Indent
                    : ignoreItemIndent + 2;
            }

            while (pos < entries.Count && entries[pos].Indent > ignoreItemIndent)
            {
                if (entries[pos].Indent == itemPropIndent)
                {
                    ReadIgnoreProperty(entries, ref pos, itemPropIndent, entries[pos].Text, item);
                }
                else
                {
                    pos++;
                }
            }

            ignores.Add(item);
        }

        return ignores;
    }

    private static void ReadIgnoreProperty(
        List<(int Indent, string Text)> entries,
        ref int pos,
        int propIndent,
        string propText,
        DependabotIgnoreEntry item)
    {
        if (!TrySplitYamlKeyValue(propText, out string key, out string val))
        {
            pos++;
            return;
        }

        switch (key)
        {
            case "dependency-name":
                item.DependencyName = UnquoteYamlScalar(val);
                pos++;
                break;
            case "versions":
                item.HasVersions = true;
                item.Versions.AddRange(ReadStringSequence(entries, ref pos, propIndent, val));
                break;
            case "update-types":
                item.HasUpdateTypes = true;
                item.UpdateTypes.AddRange(ReadStringSequence(entries, ref pos, propIndent, val));
                break;
            default:
                pos++;
                break;
        }
    }

    private static List<string> ReadStringSequence(
        List<(int Indent, string Text)> entries,
        ref int pos,
        int propIndent,
        string inlineVal)
    {
        if (inlineVal.StartsWith('['))
        {
            string flow = ConsumeFlowSequence(entries, ref pos, inlineVal);
            return ParseFlowStringArray(flow);
        }

        if (inlineVal.Length > 0)
        {
            pos++;
            return [UnquoteYamlScalar(inlineVal)];
        }

        pos++;
        if (pos >= entries.Count)
        {
            return [];
        }

        if (entries[pos].Indent > propIndent && entries[pos].Text.StartsWith('['))
        {
            string flow = ConsumeFlowSequence(entries, ref pos, entries[pos].Text);
            return ParseFlowStringArray(flow);
        }

        var items = new List<string>();
        while (pos < entries.Count &&
               entries[pos].Indent >= propIndent &&
               TrySplitSequenceEntry(entries[pos].Text, out string seqItem, out _))
        {
            if (seqItem.Length > 0)
            {
                items.Add(UnquoteYamlScalar(seqItem));
            }

            pos++;
        }

        return items;
    }

    private static string ConsumeFlowSequence(
        List<(int Indent, string Text)> entries,
        ref int pos,
        string firstSegment)
    {
        var sb = new StringBuilder(firstSegment);
        int depth = CountBracketDelta(firstSegment);
        pos++;
        while (depth > 0 && pos < entries.Count)
        {
            sb.Append(' ').Append(entries[pos].Text);
            depth += CountBracketDelta(entries[pos].Text);
            pos++;
        }

        return sb.ToString();
    }

    private static int CountBracketDelta(string text)
    {
        int delta = 0;
        char quote = '\0';
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\')
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
                delta++;
            }
            else if (c == ']')
            {
                delta--;
            }

            i++;
        }

        return delta;
    }

    private static List<string> ParseFlowStringArray(string flow)
    {
        string trimmed = flow.Trim();
        if (trimmed.StartsWith('['))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith(']'))
        {
            trimmed = trimmed[..^1];
        }

        var items = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        int i = 0;

        while (i < trimmed.Length)
        {
            char c = trimmed[i];
            if (quote != '\0')
            {
                current.Append(c);
                if (quote == '"' && c == '\\' && i + 1 < trimmed.Length)
                {
                    current.Append(trimmed[i + 1]);
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < trimmed.Length && trimmed[i + 1] == '\'')
                {
                    current.Append(trimmed[i + 1]);
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
                current.Append(c);
            }
            else if (c == ',')
            {
                string token = current.ToString().Trim();
                if (token.Length > 0)
                {
                    items.Add(UnquoteYamlScalar(token));
                }

                current.Clear();
            }
            else
            {
                current.Append(c);
            }

            i++;
        }

        string tail = current.ToString().Trim();
        if (tail.Length > 0)
        {
            items.Add(UnquoteYamlScalar(tail));
        }

        return items;
    }

    private static bool TrySplitSequenceEntry(string text, out string afterDash, out int spacesAfterDash)
    {
        if (string.Equals(text, "-", StringComparison.Ordinal))
        {
            afterDash = string.Empty;
            spacesAfterDash = 0;
            return true;
        }

        if (text.Length > 1 && text[0] == '-' && (text[1] == ' ' || text[1] == '\t'))
        {
            int ws = 1;
            while (ws < text.Length && (text[ws] == ' ' || text[ws] == '\t'))
            {
                ws++;
            }

            spacesAfterDash = ws - 1;
            afterDash = text[ws..];
            return true;
        }

        afterDash = string.Empty;
        spacesAfterDash = 0;
        return false;
    }

    private static bool TrySplitYamlKeyValue(string text, out string key, out string value)
    {
        char quote = '\0';
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\')
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
                i++;
                continue;
            }

            if (c == ':' && (i + 1 == text.Length || text[i + 1] == ' ' || text[i + 1] == '\t'))
            {
                key = UnquoteYamlScalar(text[..i]);
                value = text[(i + 1)..].Trim();
                return key.Length > 0;
            }

            i++;
        }

        key = string.Empty;
        value = string.Empty;
        return false;
    }

    private static string StripYamlComment(string line)
    {
        char quote = '\0';
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\')
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

            if (c == '#' && (i == 0 || line[i - 1] == ' ' || line[i - 1] == '\t'))
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

    private sealed class DependabotUpdateEntry
    {
        public string PackageEcosystem { get; set; } = string.Empty;

        public string Directory { get; set; } = string.Empty;

        public string TargetBranch { get; set; } = string.Empty;

        public List<string> Directories { get; } = [];

        public List<DependabotIgnoreEntry> Ignores { get; } = [];
    }

    private sealed class DependabotIgnoreEntry
    {
        public string DependencyName { get; set; } = string.Empty;

        public bool HasVersions { get; set; }

        public List<string> Versions { get; } = [];

        public bool HasUpdateTypes { get; set; }

        public List<string> UpdateTypes { get; } = [];
    }
}
