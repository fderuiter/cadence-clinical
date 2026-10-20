using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

using Cadence.Kernel.Time;

namespace Cadence.Compliance;

/// <summary>
/// What a generated RTM and IQ/OQ/PQ report were tested against: the source revision, when they were generated, the
/// database the tests reached and the runner that produced the results (#5324). Both documents print it in a
/// delimited block, and <see cref="ComparableContent"/> leaves that block out so a regenerated document can be
/// compared with the committed one on results and mappings alone.
/// </summary>
/// <param name="Revision">The commit the tests ran on, and whether the working tree had other changes.</param>
/// <param name="GeneratedAtUtc">When the documents were generated, in UTC.</param>
/// <param name="Database">The PostgreSQL server the database tests reached, or why there was none.</param>
/// <param name="Runner">The command that ran the tests, and the operating system and .NET runtime.</param>
public sealed record RtmEvidenceContext(string Revision, string GeneratedAtUtc, string Database, string Runner)
{
    /// <summary>Opens the evidence block in a generated document.</summary>
    public const string StartMarker = "<!-- evidence-context:start -->";

    /// <summary>Closes the evidence block in a generated document.</summary>
    public const string EndMarker = "<!-- evidence-context:end -->";

    private const string StampSeparator = "\n---\n\n## Integrity Stamp";

    /// <summary>
    /// The context of a generation that recorded none, such as a draft or a unit test. The documents say so instead
    /// of naming a revision they were not tested on.
    /// </summary>
    public static RtmEvidenceContext NotRecorded { get; } =
        new("not recorded", "not recorded", "not recorded", "not recorded");

    /// <summary>
    /// The context of a test run about to be reported by <paramref name="runner"/> (for example
    /// <c>pnpm cadence gxp sync</c>). <see cref="Database"/> is left empty: <see cref="RtmGeneratorService.GenerateRtm"/>
    /// fills it from what the database fixtures recorded during the run.
    /// </summary>
    public static RtmEvidenceContext Capture(string repoRoot, string runner, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new(
            DescribeGitRevision(repoRoot),
            clock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "",
            $"`{runner}` on {DescribeRuntime()}");
    }

    /// <summary>The evidence block, between <see cref="StartMarker"/> and <see cref="EndMarker"/>.</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine(StartMarker);
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Tested revision:** {Revision}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Generated (UTC):** {GeneratedAtUtc}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Database:** {Database}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Runner:** {Runner}");
        sb.AppendLine(EndMarker);
        return sb.ToString();
    }

    /// <summary>
    /// A generated document without its evidence block and integrity stamp, with line endings normalised: what must
    /// match between a regenerated document and the committed one. The stamp hashes the evidence block, so it is
    /// left out too; <see cref="GxPSigner.VerifyStamp"/> still checks each copy on its own.
    /// </summary>
    public static string ComparableContent(string document)
    {
        var text = document.Replace("\r\n", "\n", StringComparison.Ordinal);
        int stamp = text.LastIndexOf(StampSeparator, StringComparison.Ordinal);
        if (stamp >= 0)
        {
            text = text[..stamp];
        }

        int start = text.IndexOf(StartMarker, StringComparison.Ordinal);
        int end = text.IndexOf(EndMarker, StringComparison.Ordinal);
        if (start >= 0 && end > start)
        {
            text = text[..start] + text[(end + EndMarker.Length)..];
        }

        return text.Trim();
    }

    /// <summary>
    /// <c>HEAD</c>'s commit and whether the working tree differs from it outside the generated documents and test
    /// results, for example <c>`abc123…` (clean working tree)</c>. Commit the code first and then run
    /// <c>gxp sync</c>, so the recorded revision is exactly what was tested.
    /// </summary>
    public static string DescribeGitRevision(string repoRoot)
    {
        string? head = Git(repoRoot, "rev-parse", "HEAD")?.Trim();
        if (string.IsNullOrEmpty(head))
        {
            return "unknown (not a git checkout)";
        }

        string status = Git(repoRoot, "status", "--porcelain", "--untracked-files=normal", "--",
            ".", ":(exclude)docs/SDLC/Requirements_Traceability_Matrix.md", ":(exclude)docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
            ":(exclude)TestResults") ?? "";
        int changed = status.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        return changed == 0
            ? $"`{head}` (clean working tree)"
            : $"`{head}` plus uncommitted changes to {changed} file(s)";
    }

    private static string? Git(string repoRoot, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>The line describing the operating system and .NET runtime, appended to a runner name.</summary>
    public static string DescribeRuntime() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}";
}
