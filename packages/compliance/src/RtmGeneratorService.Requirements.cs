using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Cadence.Compliance;

public static partial class RtmGeneratorService
{
    [GeneratedRegex(@"[-*]\s*\*\*Trace[\s-]*(\d+)\s*:\s*(.+?):\s*\*\*\s*(.*)")]
    private static partial Regex SrsListRegex();

    [GeneratedRegex(@"#{1,6}\s*Trace[\s-]*(\d+)\s*:\s*(.*)")]
    private static partial Regex SrsHeadingRegex();

    [GeneratedRegex(@"Parent PRD:\s*`?(PRD-[A-Z0-9]+-\d+)`?", RegexOptions.IgnoreCase)]
    private static partial Regex SrsParentPrdRegex();

    [GeneratedRegex(@"\|\s*`?(Trace-\d+|SRS-[A-Z0-9]+-\d+)`?\s*\|\s*`?(PRD-[A-Z0-9]+-\d+)`?\s*\|\s*([^|]+)\|\s*([^|]+)\|")]
    private static partial Regex SrsTableRegex();

    [GeneratedRegex(@"#{1,6}\s*(PRD-[A-Z0-9]+-\d+)\s*:\s*(.*)")]
    private static partial Regex PrdHeadingRegex();

    [GeneratedRegex(@"[-*]\s*\*\*(PRD-[A-Z0-9]+-\d+)\s*:\s*(.+?)\*\*")]
    private static partial Regex PrdBoldRegex();

    [GeneratedRegex(@"^\s*(?:[-*]\s*)?\*\*Evidence:\*\*\s*PostgreSQL\b")]
    private static partial Regex PrdDatabaseEvidenceRegex();

    /// <summary>Loads PRD requirements plus the SRS/Trace aliases that point at them.</summary>
    public static Dictionary<string, RequirementInfo> LoadRequirements(string repoRoot)
    {
        var srsReqs = ParseSrs(Path.Combine(repoRoot, "docs"));
        var prdReqs = ParsePrd(Path.Combine(repoRoot, "docs", "SDLC"));

        var allRequirements = new Dictionary<string, RequirementInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in prdReqs) allRequirements[kv.Key] = kv.Value;
        foreach (var kv in srsReqs)
        {
            allRequirements.TryAdd(kv.Key, kv.Value);
        }

        return allRequirements;
    }

    public static Dictionary<string, RequirementInfo> ParseSrs(string docsDir)
    {
        var reqs = new Dictionary<string, RequirementInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(docsDir)) return reqs;

        string repoRoot = ResolveDefaultRepoRoot();
        var mdFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);
        foreach (var file in mdFiles)
        {
            string fileName = Path.GetFileName(file);
            if (fileName is "Requirements_Traceability_Matrix.md" or "IQ_OQ_PQ_Execution_Report.md") continue;

            string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            string[] lines = File.ReadAllLines(file);

            RequirementInfo? currentTrace = null;

            foreach (var line in lines)
            {
                var tmatch = SrsTableRegex().Match(line);
                if (tmatch.Success)
                {
                    string reqId = tmatch.Groups[1].Value.Trim();
                    string parentId = tmatch.Groups[2].Value.Trim();
                    string title = tmatch.Groups[4].Value.Trim();
                    reqs[reqId] = new RequirementInfo
                    {
                        Id = reqId,
                        Title = title,
                        ParentPrdId = parentId,
                        IsLegacyAlias = true,
                        Source = relPath
                    };
                    continue;
                }

                var match = SrsListRegex().Match(line);
                if (match.Success)
                {
                    string reqId = $"Trace-{match.Groups[1].Value.Trim()}";
                    currentTrace = new RequirementInfo
                    {
                        Id = reqId,
                        Title = match.Groups[2].Value.Trim(),
                        Description = match.Groups[3].Value.Trim(),
                        IsLegacyAlias = true,
                        Source = relPath
                    };
                    reqs[reqId] = currentTrace;
                    continue;
                }

                var hmatch = SrsHeadingRegex().Match(line);
                if (hmatch.Success)
                {
                    string reqId = $"Trace-{hmatch.Groups[1].Value.Trim()}";
                    currentTrace = new RequirementInfo
                    {
                        Id = reqId,
                        Title = hmatch.Groups[2].Value.Trim(),
                        IsLegacyAlias = true,
                        Source = relPath
                    };
                    reqs[reqId] = currentTrace;
                    continue;
                }

                if (currentTrace != null)
                {
                    var pmatch = SrsParentPrdRegex().Match(line);
                    if (pmatch.Success)
                    {
                        currentTrace.ParentPrdId = pmatch.Groups[1].Value.Trim();
                    }

                    if (line.StartsWith('#') || line.StartsWith("---", StringComparison.Ordinal))
                    {
                        currentTrace = null;
                    }
                }
            }
        }

        return reqs;
    }

    public static Dictionary<string, RequirementInfo> ParsePrd(string sdlcDir)
    {
        var reqs = new Dictionary<string, RequirementInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(sdlcDir)) return reqs;

        string repoRoot = ResolveDefaultRepoRoot();
        var mdFiles = Directory.GetFiles(sdlcDir, "*.md", SearchOption.AllDirectories);
        foreach (var file in mdFiles)
        {
            string fileName = Path.GetFileName(file);
            if (fileName is "Requirements_Traceability_Matrix.md" or "IQ_OQ_PQ_Execution_Report.md") continue;

            string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            string[] lines = File.ReadAllLines(file);
            RequirementInfo? current = null;

            foreach (var line in lines)
            {
                if (line.StartsWith('#'))
                {
                    current = null;
                }

                var hmatch = PrdHeadingRegex().Match(line);
                if (hmatch.Success)
                {
                    string reqId = hmatch.Groups[1].Value.Trim();
                    reqs[reqId] = current = new RequirementInfo
                    {
                        Id = reqId,
                        Title = hmatch.Groups[2].Value.Trim(),
                        Source = relPath
                    };
                }
                else if (current is not null && PrdDatabaseEvidenceRegex().IsMatch(line))
                {
                    current.RequiresDatabase = true;
                }
                else
                {
                    var bmatch = PrdBoldRegex().Match(line);
                    if (bmatch.Success)
                    {
                        string reqId = bmatch.Groups[1].Value.Trim();
                        reqs[reqId] = new RequirementInfo
                        {
                            Id = reqId,
                            Title = bmatch.Groups[2].Value.Trim(),
                            Source = relPath
                        };
                    }
                }
            }
        }

        return reqs;
    }
}
