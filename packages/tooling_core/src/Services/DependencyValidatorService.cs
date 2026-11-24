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
        string? dependabotPath = ResolveDependabotPath(repoRoot);

        if (packageManifests.Count == 0 && !hasDotnetTools && dependabotPath is null)
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

        if (dependabotPath is not null)
        {
            string relativeDependabot = Path.GetRelativePath(repoRoot, dependabotPath).Replace('\\', '/');
            errors += ValidateDependabotManifest(dependabotPath, relativeDependabot);
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

    private static string? ResolveDependabotPath(string repoRoot)
    {
        string yml = Path.Combine(repoRoot, ".github", "dependabot.yml");
        if (File.Exists(yml))
        {
            return yml;
        }

        string yaml = Path.Combine(repoRoot, ".github", "dependabot.yaml");
        return File.Exists(yaml) ? yaml : null;
    }

    private static int ValidateDependabotManifest(string dependabotPath, string relativePath)
    {
        var syntaxErrors = YamlSyntaxValidatorService.ValidateFile(dependabotPath, relativePath);
        if (syntaxErrors.Count > 0)
        {
            foreach (string syntaxError in syntaxErrors)
            {
                Console.WriteLine($"[ERROR] {syntaxError}");
            }

            return syntaxErrors.Count;
        }

        string content;
        try
        {
            content = File.ReadAllText(dependabotPath);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {ex.Message}");
            return 1;
        }

        var updates = ParseDependabotUpdates(content);
        if (updates.Count == 0)
        {
            Console.WriteLine($"[ERROR] {relativePath} must define at least one entry under 'updates'.");
            return 1;
        }

        int errors = 0;
        var seenEcosystemDirs = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < updates.Count; i++)
        {
            errors += ValidateDependabotUpdate(updates[i], i, seenEcosystemDirs, relativePath);
        }

        return errors;
    }

    private static int ValidateDependabotUpdate(
        DependabotUpdateEntry update,
        int updateIdx,
        HashSet<string> seenEcosystemDirs,
        string relativePath)
    {
        int errors = 0;
        if (string.IsNullOrWhiteSpace(update.PackageEcosystem))
        {
            Console.WriteLine($"[ERROR] {relativePath}: '#/updates/{updateIdx}' must specify a non-empty 'package-ecosystem'.");
            errors++;
        }

        if (update.Directories.Count == 0)
        {
            Console.WriteLine($"[ERROR] {relativePath}: '#/updates/{updateIdx}' must specify 'directory' or 'directories'.");
            errors++;
        }
        else if (!string.IsNullOrWhiteSpace(update.PackageEcosystem))
        {
            foreach (string dir in update.Directories)
            {
                string key = $"{update.PackageEcosystem}\u001F{dir}";
                if (!seenEcosystemDirs.Add(key))
                {
                    Console.WriteLine($"[ERROR] {relativePath}: '#/updates/{updateIdx}' duplicates package-ecosystem '{update.PackageEcosystem}' for directory '{dir}'.");
                    errors++;
                }
            }
        }

        errors += ValidateDependabotIgnores(update.Ignores, updateIdx, relativePath);
        return errors;
    }

    private static int ValidateDependabotIgnores(
        IReadOnlyList<DependabotIgnoreEntry> ignores,
        int updateIdx,
        string relativePath)
    {
        int errors = 0;
        var seenVersions = new HashSet<string>(StringComparer.Ordinal);
        var seenUpdateTypes = new HashSet<string>(StringComparer.Ordinal);
        var seenBlanket = new HashSet<string>(StringComparer.Ordinal);

        for (int j = 0; j < ignores.Count; j++)
        {
            errors += ValidateSingleDependabotIgnore(
                ignores[j],
                updateIdx,
                j,
                seenVersions,
                seenUpdateTypes,
                seenBlanket,
                relativePath);
        }

        return errors;
    }

    private static int ValidateSingleDependabotIgnore(
        DependabotIgnoreEntry ignore,
        int updateIdx,
        int ignoreIdx,
        HashSet<string> seenVersions,
        HashSet<string> seenUpdateTypes,
        HashSet<string> seenBlanket,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(ignore.DependencyName))
        {
            Console.WriteLine($"[ERROR] {relativePath}: '#/updates/{updateIdx}/ignore/{ignoreIdx}' must specify a non-empty 'dependency-name'.");
            return 1;
        }

        int errors = 0;
        string dep = ignore.DependencyName;
        string updateTypesScope = string.Join("\u001E", ignore.UpdateTypes.OrderBy(u => u, StringComparer.Ordinal));

        foreach (string version in ignore.Versions)
        {
            if (!seenVersions.Add($"{dep}\u001F{updateTypesScope}\u001F{version}"))
            {
                Console.WriteLine($"[ERROR] {relativePath}: The property '#/updates/{updateIdx}/ignore/{ignoreIdx}/versions' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and 'versions'.");
                errors++;
            }
        }

        string versionsScope = string.Join("\u001E", ignore.Versions.OrderBy(v => v, StringComparer.Ordinal));
        foreach (string updateType in ignore.UpdateTypes)
        {
            if (!seenUpdateTypes.Add($"{dep}\u001F{versionsScope}\u001F{updateType}"))
            {
                Console.WriteLine($"[ERROR] {relativePath}: The property '#/updates/{updateIdx}/ignore/{ignoreIdx}/update-types' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and 'update-types'.");
                errors++;
            }
        }

        if (ignore.Versions.Count == 0 && ignore.UpdateTypes.Count == 0 && !seenBlanket.Add(dep))
        {
            Console.WriteLine($"[ERROR] {relativePath}: The property '#/updates/{updateIdx}/ignore/{ignoreIdx}' includes a duplicate blanket ignore for '{dep}'.");
            errors++;
        }

        return errors;
    }

    private static List<DependabotUpdateEntry> ParseDependabotUpdates(string content)
    {
        string[] lines = content.ReplaceLineEndings("\n").Split('\n');
        var state = new DependabotParseState();
        int i = 0;
        while (i < lines.Length)
        {
            ProcessDependabotLine(lines, ref i, state);
            i++;
        }

        return state.Updates;
    }

    private static void ProcessDependabotLine(string[] lines, ref int index, DependabotParseState state)
    {
        string stripped = StripYamlComment(lines[index]);
        string trimmed = stripped.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        int indent = stripped.Length - stripped.TrimStart(' ').Length;
        if (state.TryConsumePendingList(lines, ref index, indent, trimmed))
        {
            return;
        }

        if (indent == 0)
        {
            state.EnterRootSection(trimmed);
            return;
        }

        if (!state.InUpdates)
        {
            return;
        }

        state.HandleUpdatesLine(lines, ref index, indent, trimmed);
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
                if (quote == '"' && c == '\\' && i + 1 < line.Length)
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
            else if (c == '#' && (i == 0 || line[i - 1] is ' ' or '\t'))
            {
                return line[..i];
            }

            i++;
        }

        return line;
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
        public string? PackageEcosystem { get; set; }
        public List<string> Directories { get; } = [];
        public List<DependabotIgnoreEntry> Ignores { get; } = [];
    }

    private sealed class DependabotIgnoreEntry
    {
        public string? DependencyName { get; set; }
        public List<string> Versions { get; } = [];
        public List<string> UpdateTypes { get; } = [];
    }

    private sealed class DependabotParseState
    {
        public List<DependabotUpdateEntry> Updates { get; } = [];
        public bool InUpdates { get; private set; }
        private int _updateItemDashIndent = -1;
        private int _ignoreKeyIndent = -1;
        private int _ignoreDashIndent = -1;
        private DependabotUpdateEntry? _currentUpdate;
        private DependabotIgnoreEntry? _currentIgnore;
        private List<string>? _pendingList;
        private int _pendingListKeyIndent = -1;

        public void EnterRootSection(string trimmed)
        {
            InUpdates = string.Equals(trimmed, "updates:", StringComparison.Ordinal);
            _currentUpdate = null;
            _currentIgnore = null;
            _ignoreKeyIndent = -1;
            _pendingList = null;
        }

        public bool TryConsumePendingList(string[] lines, ref int index, int indent, string trimmed)
        {
            if (_pendingList is null)
            {
                return false;
            }

            if (indent <= _pendingListKeyIndent)
            {
                _pendingList = null;
                _pendingListKeyIndent = -1;
                return false;
            }

            if (trimmed.StartsWith('['))
            {
                ConsumeFlowSequence(lines, ref index, trimmed, _pendingList);
                _pendingList = null;
                _pendingListKeyIndent = -1;
                return true;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                AddNonEmptyScalar(trimmed[2..], _pendingList);
                return true;
            }

            _pendingList = null;
            _pendingListKeyIndent = -1;
            return false;
        }

        private static void ConsumeFlowSequence(string[] lines, ref int index, string initialSegment, List<string> target)
        {
            var sb = new StringBuilder();
            int depth = 0;
            char quote = '\0';

            for (int lineCursor = index; lineCursor < lines.Length; lineCursor++)
            {
                string part = lineCursor == index ? initialSegment : StripYamlComment(lines[lineCursor]);
                if (AppendUntilBalancedBracket(part, sb, ref depth, ref quote))
                {
                    index = lineCursor;
                    break;
                }

                sb.Append(' ');
                index = lineCursor;
            }

            ParseFlowItems(sb.ToString(), target);
        }

        private static bool AppendUntilBalancedBracket(string part, StringBuilder sb, ref int depth, ref char quote)
        {
            int i = 0;
            while (i < part.Length)
            {
                char c = part[i];
                sb.Append(c);
                if (UpdateQuoteState(part, ref i, c, sb, ref quote))
                {
                    i++;
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']' && --depth == 0)
                {
                    return true;
                }

                i++;
            }

            return false;
        }

        private static bool UpdateQuoteState(string part, ref int i, char c, StringBuilder sb, ref char quote)
        {
            if (quote == '\0')
            {
                if (c is '"' or '\'')
                {
                    quote = c;
                    return true;
                }

                return false;
            }

            if (quote == '"' && c == '\\' && i + 1 < part.Length)
            {
                sb.Append(part[++i]);
                return true;
            }

            if (c == quote)
            {
                quote = '\0';
            }

            return true;
        }

        private static void ParseFlowItems(string flowText, List<string> target)
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

            var current = new StringBuilder();
            char quote = '\0';
            int i = 0;
            while (i < inner.Length)
            {
                char c = inner[i];
                if (quote == '\0' && c == ',')
                {
                    AddNonEmptyScalar(current.ToString(), target);
                    current.Clear();
                    i++;
                    continue;
                }

                current.Append(c);
                UpdateQuoteState(inner, ref i, c, current, ref quote);
                i++;
            }

            AddNonEmptyScalar(current.ToString(), target);
        }

        private static void AddNonEmptyScalar(string raw, List<string> target)
        {
            string unquoted = UnquoteYamlScalar(raw);
            if (!string.IsNullOrWhiteSpace(unquoted))
            {
                target.Add(unquoted);
            }
        }

        private static string UnquoteYamlScalar(string raw)
        {
            string s = raw.Trim();
            if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            {
                return s[1..^1];
            }

            return s;
        }

        public void HandleUpdatesLine(string[] lines, ref int index, int indent, string trimmed)
        {
            if (_ignoreKeyIndent >= 0 && indent <= _ignoreKeyIndent)
            {
                _ignoreKeyIndent = -1;
                _ignoreDashIndent = -1;
                _currentIgnore = null;
            }

            if (_ignoreKeyIndent < 0 && IsSequenceEntry(trimmed) && (_updateItemDashIndent < 0 || indent == _updateItemDashIndent))
            {
                _updateItemDashIndent = indent;
                _currentUpdate = new DependabotUpdateEntry();
                Updates.Add(_currentUpdate);
                string rest = trimmed.Length > 1 ? trimmed[1..].TrimStart() : "";
                if (rest.Length > 0)
                {
                    ApplyUpdateProperty(lines, ref index, indent + 2, rest);
                }

                return;
            }

            if (_currentUpdate is null)
            {
                return;
            }

            if (_ignoreKeyIndent >= 0)
            {
                HandleIgnoreLine(lines, ref index, indent, trimmed);
            }
            else
            {
                ApplyUpdateProperty(lines, ref index, indent, trimmed);
            }
        }

        private void HandleIgnoreLine(string[] lines, ref int index, int indent, string trimmed)
        {
            if (IsSequenceEntry(trimmed) && (_ignoreDashIndent < 0 || indent == _ignoreDashIndent))
            {
                _ignoreDashIndent = indent;
                _currentIgnore = new DependabotIgnoreEntry();
                _currentUpdate!.Ignores.Add(_currentIgnore);
                string rest = trimmed.Length > 1 ? trimmed[1..].TrimStart() : "";
                if (rest.Length > 0)
                {
                    ApplyIgnoreProperty(lines, ref index, indent + 2, rest);
                }

                return;
            }

            if (_currentIgnore is not null)
            {
                ApplyIgnoreProperty(lines, ref index, indent, trimmed);
            }
        }

        private void ApplyUpdateProperty(string[] lines, ref int index, int keyIndent, string segment)
        {
            if (!TrySplitKeyValue(segment, out string key, out string value))
            {
                return;
            }

            if (string.Equals(key, "package-ecosystem", StringComparison.Ordinal))
            {
                _currentUpdate!.PackageEcosystem = UnquoteYamlScalar(value);
            }
            else if (string.Equals(key, "directory", StringComparison.Ordinal))
            {
                AddNonEmptyScalar(value, _currentUpdate!.Directories);
            }
            else if (string.Equals(key, "directories", StringComparison.Ordinal))
            {
                AssignListValue(lines, ref index, keyIndent, value, _currentUpdate!.Directories);
            }
            else if (string.Equals(key, "ignore", StringComparison.Ordinal))
            {
                _ignoreKeyIndent = keyIndent;
                _ignoreDashIndent = -1;
                _currentIgnore = null;
            }
        }

        private void ApplyIgnoreProperty(string[] lines, ref int index, int keyIndent, string segment)
        {
            if (!TrySplitKeyValue(segment, out string key, out string value))
            {
                return;
            }

            if (string.Equals(key, "dependency-name", StringComparison.Ordinal))
            {
                _currentIgnore!.DependencyName = UnquoteYamlScalar(value);
            }
            else if (string.Equals(key, "versions", StringComparison.Ordinal))
            {
                AssignListValue(lines, ref index, keyIndent, value, _currentIgnore!.Versions);
            }
            else if (string.Equals(key, "update-types", StringComparison.Ordinal))
            {
                AssignListValue(lines, ref index, keyIndent, value, _currentIgnore!.UpdateTypes);
            }
        }

        private void AssignListValue(string[] lines, ref int index, int keyIndent, string value, List<string> target)
        {
            if (value.Length == 0)
            {
                _pendingList = target;
                _pendingListKeyIndent = keyIndent;
            }
            else if (value.StartsWith('['))
            {
                ConsumeFlowSequence(lines, ref index, value, target);
            }
            else
            {
                AddNonEmptyScalar(value, target);
            }
        }

        private static bool IsSequenceEntry(string trimmed) =>
            string.Equals(trimmed, "-", StringComparison.Ordinal)
            || trimmed.StartsWith("- ", StringComparison.Ordinal);

        private static bool TrySplitKeyValue(string segment, out string key, out string value)
        {
            int colon = segment.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                key = "";
                value = "";
                return false;
            }

            key = UnquoteYamlScalar(segment[..colon]);
            value = segment[(colon + 1)..].Trim();
            return key.Length > 0;
        }
    }
}
