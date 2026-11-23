using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Cadence.Cli.Services;

/// <summary>
/// Renders the gate tables in <c>docs/cli.md</c> and <c>docs/CI.md</c> from <see cref="QualityGates"/>. Each table sits
/// between a <see cref="BeginMarker"/> and an <see cref="EndMarker"/> line; <c>cadence generate-cli-docs</c> rewrites
/// it and the <c>cli-docs</c> gate fails when it is stale. The tables are written the way Prettier formats them, so
/// the pre-commit Prettier check leaves them alone.
/// </summary>
public static class GateDocs
{
    /// <summary>The line that opens a generated gate table.</summary>
    public const string BeginMarker = "<!-- quality-gates:begin (generated from QualityGates.cs by `pnpm cadence generate-cli-docs`; do not edit) -->";

    /// <summary>The line that closes a generated gate table.</summary>
    public const string EndMarker = "<!-- quality-gates:end -->";

    /// <summary>The documents that carry a generated gate table, relative to the repository root, and their tables.</summary>
    public static IReadOnlyList<(string RelativePath, Func<string> Render)> Regions { get; } =
    [
        ("docs/cli.md", RenderCliTable),
        ("docs/CI.md", RenderCiTable),
    ];

    /// <summary>The <c>docs/cli.md</c> table: names, whether a gate takes files, and what the pre-commit hook does.</summary>
    public static string RenderCliTable() =>
        RenderTable(
            ["Gate", "Also accepted", "Takes `[files]`", "Pre-commit hook (`--pre-commit`)"],
            QualityGates.All.Select(g => new[]
            {
                $"`{g.Name}`",
                string.Join(", ", g.Aliases.Select(a => $"`{a}`")),
                g.TakesFiles ? "yes" : "",
                g.PreCommit switch
                {
                    PreCommitMode.Skipped => $"Skipped: {g.PreCommitNote}",
                    PreCommitMode.Quick => $"Quick: {g.PreCommitNote}",
                    _ => g.TakesFiles ? "Runs on the staged files" : "Runs",
                },
            }));

    /// <summary>The <c>docs/CI.md</c> table: what each gate checks, preceded by the gate count.</summary>
    public static string RenderCiTable() =>
        $"`pnpm cadence check` runs these {QualityGates.All.Count} gates in this order:\n\n" +
        RenderTable(["Gate", "What it checks"], QualityGates.All.Select(g => new[] { $"`{g.Name}`", g.Checks }));

    /// <summary>Replaces each document's generated table. Returns 1 when a document or its markers are missing.</summary>
    public static int Write(string repoRoot)
    {
        int exitCode = 0;
        foreach (var (relativePath, render) in Regions)
        {
            string path = Path.Combine(repoRoot, relativePath);
            string? updated = File.Exists(path) ? ReplaceRegion(File.ReadAllText(path), render()) : null;
            if (updated is null)
            {
                Console.WriteLine($"[ERROR] {relativePath} is missing or has no '{EndMarker}'-terminated gate table to update.");
                exitCode = 1;
                continue;
            }

            File.WriteAllText(path, updated);
        }

        return exitCode;
    }

    /// <summary>Returns <c>true</c> when every document's generated table matches the registry.</summary>
    public static bool Check(string repoRoot)
    {
        bool passed = true;
        foreach (var (relativePath, render) in Regions)
        {
            string path = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(path))
            {
                Console.WriteLine($"[ERROR] {relativePath} not found; it must carry the generated quality gate table.");
                passed = false;
                continue;
            }

            string content = File.ReadAllText(path).ReplaceLineEndings("\n");
            string? expected = ReplaceRegion(content, render());
            if (expected is null)
            {
                Console.WriteLine($"[ERROR] {relativePath} has no generated quality gate table (the '{BeginMarker}' and '{EndMarker}' lines).");
                passed = false;
            }
            else if (expected != content)
            {
                Console.WriteLine($"[ERROR] The quality gate table in {relativePath} does not match packages/cli/src/Services/QualityGates.cs. Run 'pnpm cadence generate-cli-docs'.");
                passed = false;
            }
        }

        return passed;
    }

    /// <summary>
    /// Puts <paramref name="body"/> between the markers in <paramref name="document"/>, or returns <c>null</c> when the
    /// markers are missing or out of order.
    /// </summary>
    public static string? ReplaceRegion(string document, string body)
    {
        string text = document.ReplaceLineEndings("\n");
        int begin = text.IndexOf(BeginMarker + "\n", StringComparison.Ordinal);
        int end = text.IndexOf("\n" + EndMarker, StringComparison.Ordinal);
        if (begin < 0 || end < begin)
        {
            return null;
        }

        int bodyStart = begin + BeginMarker.Length + 1;
        return text[..bodyStart] + "\n" + body.TrimEnd('\n') + "\n" + text[end..];
    }

    /// <summary>Renders a Markdown table with every column padded to its widest cell, as Prettier does.</summary>
    public static string RenderTable(IReadOnlyList<string> header, IEnumerable<string[]> rows)
    {
        var cells = rows.Prepend(header.ToArray()).Select(r => r.Select(Escape).ToArray()).ToList();
        int[] widths = Enumerable.Range(0, header.Count)
            .Select(i => Math.Max(3, cells.Max(r => r[i].Length)))
            .ToArray();

        var sb = new StringBuilder();
        AppendRow(sb, cells[0], widths);
        AppendRow(sb, widths.Select(w => new string('-', w)).ToArray(), widths);
        foreach (string[] row in cells.Skip(1))
        {
            AppendRow(sb, row, widths);
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string[] row, int[] widths)
    {
        sb.Append('|');
        for (int i = 0; i < row.Length; i++)
        {
            sb.Append(' ').Append(row[i].PadRight(widths[i])).Append(" |");
        }

        sb.Append('\n');
    }

    private static string Escape(string cell) => cell.Replace("|", "\\|", StringComparison.Ordinal);
}
