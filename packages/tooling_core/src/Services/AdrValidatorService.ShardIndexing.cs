using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class AdrValidatorService
{
    [GeneratedRegex(@"^\s*[-*]\s+\[[^\]]*\]\(([^)\s]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex IndexEntryRegex();

    [GeneratedRegex(@"\]\(([^)\s]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();

    /// <summary>
    /// Inserts an ADR bullet into <c>docs/adr/&lt;domain&gt;/index.md</c> sorted alphabetically by filename,
    /// creating the domain shard file from its canonical template if absent.
    /// </summary>
    public static bool InsertSortedDomainShardEntry(
        string shardIndexPath,
        string domainSlug,
        string filename,
        string adrId,
        string title,
        string isoDate)
    {
        var domainDef = TryGetDomainDefinition(domainSlug)
            ?? throw new ArgumentException($"Unknown ADR domain '{domainSlug}'.", nameof(domainSlug));

        string shardDir = Path.GetDirectoryName(shardIndexPath)!;
        Directory.CreateDirectory(shardDir);

        if (!File.Exists(shardIndexPath))
        {
            string anchor = BuildGitHubHeadingSlug(domainDef.SectionTitle);
            string initialContent = string.Join(
                '\n',
                $"# {domainDef.ShortTitle} ADRs (`{domainDef.Slug}`)",
                "",
                $"Active Architectural Decision Records for **{domainDef.SectionTitle}**.",
                "",
                $"- [ADR Hub](../index.md) · [ADR Template](../TEMPLATE.md) · [Frozen Legacy Decisions](../legacy-index.md#{anchor})",
                $"- Scaffold a new decision in this shard with `pnpm cadence scaffold adr --domain {domainDef.Slug} \"<title>\"`.",
                $"- Entries below use `# ADR-{domainDef.Slug.ToUpperInvariant()}-YYYYMMDD-<slug>: <title>` and are sorted alphabetically by filename.",
                "",
                "## Decisions",
                "");
            File.WriteAllText(shardIndexPath, initialContent);
        }

        var lines = File.ReadAllLines(shardIndexPath).Select(l => l.TrimEnd('\r')).ToList();

        int decisionsHeadingIdx = lines.FindIndex(l => l.Trim().Equals("## Decisions", StringComparison.OrdinalIgnoreCase));
        int searchStart = decisionsHeadingIdx >= 0 ? decisionsHeadingIdx + 1 : 0;

        var bulletPositions = new List<(int LineIndex, string TargetFile)>();
        for (int i = searchStart; i < lines.Count; i++)
        {
            if (decisionsHeadingIdx >= 0 && lines[i].StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            var match = IndexEntryRegex().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            string target = match.Groups[1].Value.Split('#')[0].Trim();
            if (target.StartsWith("./", StringComparison.Ordinal))
            {
                target = target[2..];
            }

            if (target.StartsWith("../", StringComparison.Ordinal) || target.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(target, filename, StringComparison.Ordinal))
            {
                return false;
            }

            bulletPositions.Add((i, target));
        }

        string entryLine = string.IsNullOrEmpty(isoDate)
            ? $"- [{adrId}: {title}]({filename})"
            : $"- [{adrId}: {title}]({filename}) ({isoDate})";

        if (bulletPositions.Count == 0)
        {
            if (decisionsHeadingIdx >= 0)
            {
                int insertAt = decisionsHeadingIdx + 1;
                if (insertAt >= lines.Count || lines[insertAt].Length > 0)
                {
                    lines.Insert(insertAt, "");
                    insertAt++;
                }
                else
                {
                    insertAt++;
                }

                lines.Insert(insertAt, entryLine);
            }
            else
            {
                if (lines.Count > 0 && lines[^1].Length > 0)
                {
                    lines.Add("");
                }

                lines.Add("## Decisions");
                lines.Add("");
                lines.Add(entryLine);
            }
        }
        else
        {
            int insertIdx = -1;
            foreach (var (lineIdx, existingTarget) in bulletPositions)
            {
                if (StringComparer.Ordinal.Compare(filename, existingTarget) < 0)
                {
                    insertIdx = lineIdx;
                    break;
                }
            }

            if (insertIdx >= 0)
            {
                lines.Insert(insertIdx, entryLine);
            }
            else
            {
                lines.Insert(bulletPositions[^1].LineIndex + 1, entryLine);
            }
        }

        string normalized = string.Join('\n', lines).TrimEnd('\n') + "\n";
        File.WriteAllText(shardIndexPath, normalized);
        return true;
    }

    /// <summary>
    /// Validates ADR integrity across <c>docs/adr/index.md</c>, <c>docs/adr/legacy-index.md</c>, and all
    /// eight <c>docs/adr/&lt;domain&gt;/index.md</c> shards: valid headings, globally unique ADR IDs,
    /// complete single-entry shard coverage, alphabetical sorting by filename within every shard section,
    /// and valid relative links across all ADR indexes.
    /// </summary>
    public static IReadOnlyList<string> FindIntegrityErrors(string adrDir)
    {
        var errors = new List<string>();

        string hubIndexFile = Path.Combine(adrDir, "index.md");
        if (!File.Exists(hubIndexFile))
        {
            errors.Add($"ADR index '{hubIndexFile}' not found.");
            return errors;
        }

        ValidateIndexLinks(hubIndexFile, "index.md", errors);

        string legacyIndexFile = Path.Combine(adrDir, "legacy-index.md");
        bool hasLegacyIndex = File.Exists(legacyIndexFile);

        // Reject unrecognized subdirectories under docs/adr/
        foreach (string subDir in Directory.GetDirectories(adrDir).Order(StringComparer.Ordinal))
        {
            string subName = Path.GetFileName(subDir);
            if (subName.StartsWith('.'))
            {
                continue;
            }

            if (!DomainsBySlug.ContainsKey(subName))
            {
                errors.Add($"Unknown ADR domain directory '{subName}' under docs/adr/; expected one of: {string.Join(", ", DomainSlugs)}.");
            }
        }

        var filesByNumber = new SortedDictionary<int, List<string>>();
        var filesById = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        static void RegisterId(Dictionary<string, List<string>> map, string id, string relFile)
        {
            if (!map.TryGetValue(id, out var holders))
            {
                holders = [];
                map[id] = holders;
            }

            holders.Add(relFile);
        }

        // 1. Validate frozen legacy ADRs in root docs/adr/*.md
        var legacyAdrFiles = Directory.GetFiles(adrDir, "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !RootIgnoreFiles.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        foreach (string name in legacyAdrFiles)
        {
            string firstLine = File.ReadLines(Path.Combine(adrDir, name)).FirstOrDefault() ?? "";
            int? number = ParseAdrNumber(firstLine);
            if (number is null)
            {
                errors.Add($"File '{name}' has no '# ADR-NNNN: Title' heading on line 1 (found '{firstLine.TrimEnd('\r')}').");
                continue;
            }

            if (!filesByNumber.TryGetValue(number.Value, out var numHolders))
            {
                numHolders = [];
                filesByNumber[number.Value] = numHolders;
            }

            numHolders.Add(name);
            string canonicalLegacyId = number.Value < 1000 ? $"ADR-{number.Value:D3}" : $"ADR-{number.Value}";
            RegisterId(filesById, canonicalLegacyId, name);
        }

        string legacySourceFile = hasLegacyIndex ? legacyIndexFile : hubIndexFile;
        string legacySourceLabel = hasLegacyIndex ? "legacy-index.md" : "index.md";
        if (hasLegacyIndex)
        {
            ValidateIndexLinks(legacyIndexFile, "legacy-index.md", errors);

            // Ensure root index.md stays static and does not directly list per-ADR YYYY-MM-DD-*.md bullets.
            string[] hubLines = File.ReadAllLines(hubIndexFile);
            for (int i = 0; i < hubLines.Length; i++)
            {
                var hubBullet = IndexEntryRegex().Match(hubLines[i]);
                if (!hubBullet.Success)
                {
                    continue;
                }

                string target = hubBullet.Groups[1].Value.Split('#')[0].Trim();
                if (target.StartsWith("./", StringComparison.Ordinal))
                {
                    target = target[2..];
                }

                string targetFileName = Path.GetFileName(target);
                if (FilenameRegex().IsMatch(targetFileName))
                {
                    errors.Add($"index.md line {i + 1} lists ADR '{target}' directly; place new ADRs in docs/adr/<domain>/index.md and keep legacy ADRs in legacy-index.md.");
                }
            }
        }

        var legacyListedCounts = ParseAndValidateShardBullets(
            legacySourceFile,
            legacySourceLabel,
            ignoreNonAdrNavBullets: true,
            errors);

        foreach (string name in legacyAdrFiles)
        {
            int count = legacyListedCounts.GetValueOrDefault(name);
            if (count == 0)
            {
                errors.Add($"File '{name}' is missing from {legacySourceLabel}.");
            }
            else if (count > 1)
            {
                errors.Add($"File '{name}' is listed {count} times in {legacySourceLabel}; list each ADR exactly once.");
            }
        }

        // 2. Validate each of the 8 domain shards under docs/adr/<domain>/
        foreach (var domain in DomainDefinitions)
        {
            string domainDir = Path.Combine(adrDir, domain.Slug);
            if (!Directory.Exists(domainDir))
            {
                continue;
            }

            string shardIndexFile = Path.Combine(domainDir, "index.md");
            string shardLabel = $"{domain.Slug}/index.md";
            if (!File.Exists(shardIndexFile))
            {
                errors.Add($"Domain ADR shard index '{shardLabel}' not found.");
                continue;
            }

            ValidateIndexLinks(shardIndexFile, shardLabel, errors);

            var shardListedCounts = ParseAndValidateShardBullets(
                shardIndexFile,
                shardLabel,
                ignoreNonAdrNavBullets: true,
                errors);

            var domainFiles = Directory.GetFiles(domainDir, "*.md", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !ShardIgnoreFiles.Contains(name))
                .Order(StringComparer.Ordinal)
                .ToList();

            string expectedDomainToken = domain.Slug.ToUpperInvariant();
            foreach (string name in domainFiles)
            {
                string relFile = $"{domain.Slug}/{name}";
                string firstLine = File.ReadLines(Path.Combine(domainDir, name)).FirstOrDefault()?.TrimEnd('\r') ?? "";

                int? accidentalLegacyNum = ParseAdrNumber(firstLine);
                if (accidentalLegacyNum is not null)
                {
                    if (!filesByNumber.TryGetValue(accidentalLegacyNum.Value, out var numHolders))
                    {
                        numHolders = [];
                        filesByNumber[accidentalLegacyNum.Value] = numHolders;
                    }

                    numHolders.Add(relFile);
                }

                var header = ParseDomainAdrHeader(firstLine);
                if (header is null)
                {
                    errors.Add($"File '{relFile}' has no '# ADR-{expectedDomainToken}-YYYYMMDD-<slug>: <title>' heading on line 1 (found '{firstLine}').");
                }
                else
                {
                    RegisterId(filesById, header.Id, relFile);

                    if (!string.Equals(header.Domain, expectedDomainToken, StringComparison.Ordinal))
                    {
                        errors.Add($"File '{relFile}' heading domain '{header.Domain}' does not match directory domain '{expectedDomainToken}'.");
                    }

                    if (FilenameRegex().IsMatch(name))
                    {
                        string expectedCompactDate = name[..10].Replace("-", "", StringComparison.Ordinal);
                        string expectedSlug = Path.GetFileNameWithoutExtension(name)[11..];
                        if (!string.Equals(header.CompactDate, expectedCompactDate, StringComparison.Ordinal) ||
                            !string.Equals(header.Slug, expectedSlug, StringComparison.Ordinal))
                        {
                            errors.Add($"File '{relFile}' heading identifier '{header.Id}' does not match filename date/slug 'ADR-{expectedDomainToken}-{expectedCompactDate}-{expectedSlug}'.");
                        }
                    }
                }

                int count = shardListedCounts.GetValueOrDefault(name);
                if (count == 0)
                {
                    errors.Add($"File '{relFile}' is missing from {shardLabel}.");
                }
                else if (count > 1)
                {
                    errors.Add($"File '{relFile}' is listed {count} times in {shardLabel}; list each ADR exactly once.");
                }
            }
        }

        // 3. Report duplicate legacy numbers or duplicate ADR IDs across all shards
        var reportedDuplicateFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (number, holders) in filesByNumber.Where(kv => kv.Value.Count > 1))
        {
            errors.Add($"ADR-{number:D3} is used by {holders.Count} files: {string.Join(", ", holders)}.");
            reportedDuplicateFiles.Add(string.Join("|", holders));
        }

        foreach (var (id, holders) in filesById
                     .Where(kv => kv.Value.Count > 1)
                     .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            string key = string.Join("|", holders);
            if (reportedDuplicateFiles.Contains(key))
            {
                continue;
            }

            errors.Add($"{id} is used by {holders.Count} files: {string.Join(", ", holders)}.");
        }

        return errors;
    }

    private static void ValidateIndexLinks(string indexFilePath, string indexLabel, List<string> errors)
    {
        string baseDir = Path.GetDirectoryName(indexFilePath)!;
        string[] lines = File.ReadAllLines(indexFilePath);
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match link in MarkdownLinkRegex().Matches(lines[i]))
            {
                string target = link.Groups[1].Value;
                if (target.Contains("://", StringComparison.Ordinal) ||
                    target.StartsWith('#') ||
                    target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string pathPart = target.Split('#')[0];
                if (string.IsNullOrWhiteSpace(pathPart))
                {
                    continue;
                }

                string resolved = Path.GetFullPath(Path.Combine(baseDir, pathPart));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    errors.Add($"{indexLabel} line {i + 1} links to '{target}', which does not exist.");
                }
            }
        }
    }

    private static Dictionary<string, int> ParseAndValidateShardBullets(
        string indexFilePath,
        string indexLabel,
        bool ignoreNonAdrNavBullets,
        List<string> errors)
    {
        var listedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(indexFilePath);
        string? prevInSection = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith('#'))
            {
                prevInSection = null;
                continue;
            }

            var entry = IndexEntryRegex().Match(lines[i]);
            if (!entry.Success)
            {
                continue;
            }

            string rawTarget = entry.Groups[1].Value.Split('#')[0].Trim();
            if (rawTarget.StartsWith("./", StringComparison.Ordinal))
            {
                rawTarget = rawTarget[2..];
            }

            if (ignoreNonAdrNavBullets &&
                (rawTarget.StartsWith("../", StringComparison.Ordinal) ||
                 rawTarget.Contains("://", StringComparison.Ordinal) ||
                 string.Equals(rawTarget, "TEMPLATE.md", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(rawTarget, "index.md", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(rawTarget, "legacy-index.md", StringComparison.OrdinalIgnoreCase) ||
                 rawTarget.EndsWith("/index.md", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            listedCounts[rawTarget] = listedCounts.GetValueOrDefault(rawTarget) + 1;

            if (prevInSection is not null &&
                StringComparer.Ordinal.Compare(prevInSection, rawTarget) > 0)
            {
                errors.Add($"{indexLabel} line {i + 1}: '{rawTarget}' is not sorted alphabetically by filename (appears after '{prevInSection}').");
            }

            prevInSection = rawTarget;
        }

        return listedCounts;
    }

    private static string BuildGitHubHeadingSlug(string heading)
    {
        var chars = new List<char>(heading.Length);
        foreach (char ch in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
            {
                chars.Add(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                chars.Add('-');
            }
        }

        return new string([.. chars]);
    }
}
