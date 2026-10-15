using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Compliance;

namespace Cadence.Cli.Services;

/// <summary>
/// <c>cadence gxp diff</c>: compares the regenerated RTM and IQ/OQ/PQ report with the copies in the git index, leaving
/// out the evidence block (tested revision, generation time, database and runner) and the integrity stamp over it.
/// Those change on every run; the results and the test mapping must not (#5324).
/// </summary>
public static class GxpDocsDiffService
{
    /// <summary>The generated documents, relative to the repository root.</summary>
    public static readonly string[] Documents =
    [
        "docs/SDLC/Requirements_Traceability_Matrix.md",
        "docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
    ];

    /// <summary>
    /// Returns 0 when every document matches its indexed copy apart from the evidence block, and 1 otherwise. With
    /// <paramref name="restoreWhenEquivalent"/>, a document that matches is put back to the indexed copy, so a local
    /// CI run leaves no change behind when only the evidence block moved.
    /// </summary>
    public static int Compare(string repoRoot, bool restoreWhenEquivalent = false, Func<string, string?>? readIndexed = null)
    {
        readIndexed ??= path => GitShowIndexed(repoRoot, path);
        int exitCode = 0;
        foreach (var document in Documents)
        {
            string path = Path.Combine(repoRoot, document);
            string? indexed = readIndexed(document);
            string? working = File.Exists(path) ? File.ReadAllText(path) : null;
            if (indexed is null || working is null)
            {
                Console.WriteLine($"[GXP DIFF] {document}: {(indexed is null ? "not in the git index" : "missing from the working tree")}.");
                exitCode = 1;
                continue;
            }

            var expected = RtmEvidenceContext.ComparableContent(indexed).Split('\n');
            var actual = RtmEvidenceContext.ComparableContent(working).Split('\n');
            if (expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                Console.WriteLine($"[GXP DIFF] {document}: results and mappings match the committed copy.");
                if (restoreWhenEquivalent && !string.Equals(indexed, working, StringComparison.Ordinal))
                {
                    File.WriteAllText(path, indexed);
                }

                continue;
            }

            exitCode = 1;
            Console.WriteLine($"[GXP DIFF] {document} differs from the committed copy. Run `pnpm cadence gxp sync` and commit the result:");
            foreach (var line in Differences(expected, actual).Take(40))
            {
                Console.WriteLine($"  {line}");
            }
        }

        return exitCode;
    }

    /// <summary>Lines only one side has, marked <c>-</c> (committed) or <c>+</c> (regenerated), in order.</summary>
    internal static IEnumerable<string> Differences(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
        return expected.Where(l => !actualSet.Contains(l)).Select(l => $"- {l}")
            .Concat(actual.Where(l => !expectedSet.Contains(l)).Select(l => $"+ {l}"));
    }

    private static string? GitShowIndexed(string repoRoot, string document)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = repoRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("show");
        psi.ArgumentList.Add($":{document}");
        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
