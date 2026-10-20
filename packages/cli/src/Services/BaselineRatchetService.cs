using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Cadence.Cli.Services;

/// <summary>
/// The <c>baseline-ratchet</c> gate (#5318): a ratchet baseline may only shrink. Every tracked ratchet baseline (the
/// kernel-boundary baseline, <c>.deid-baseline.txt</c>, <c>.secrets.baseline</c> and any other <c>*.baseline.txt</c> or
/// <c>*-baseline.txt</c> file) is compared with its copy at the merge base with <c>origin/main</c>. An entry the branch
/// added is new accepted debt, so the gate fails unless a commit on the branch carries a <c>Baseline-Growth: &lt;reason&gt;</c>
/// trailer. In the pre-commit hook the commit message does not exist yet, so <c>CADENCE_BASELINE_GROWTH=&lt;reason&gt;</c>
/// admits the staged growth instead; the commit must still carry the trailer, or the branch check fails afterwards.
/// </summary>
public static class BaselineRatchetService
{
    /// <summary>The ref a branch is compared with, as in <c>cadence ci</c>.</summary>
    public const string DefaultBaseRef = "origin/main";

    /// <summary>The commit trailer that admits baseline growth on a branch.</summary>
    public const string TrailerKey = "Baseline-Growth";

    /// <summary>Admits staged baseline growth in the pre-commit hook, before the commit (and its trailer) exists.</summary>
    public const string GrowthEnvironmentVariable = "CADENCE_BASELINE_GROWTH";

    private const string Tag = "[BASELINE RATCHET]";
    private const string SecretsBaseline = ".secrets.baseline";
    private const int MaxListed = 20;

    /// <summary>One baseline file that gained entries.</summary>
    /// <param name="Path">The file, relative to the repository root.</param>
    /// <param name="Added">The entries it gained, once per added occurrence.</param>
    public sealed record Growth(string Path, IReadOnlyList<string> Added);

    /// <summary>Whether <paramref name="path"/> names a ratchet baseline the gate checks.</summary>
    public static bool IsRatchetBaseline(string path)
    {
        string name = System.IO.Path.GetFileName(path.Replace('\\', '/'));
        return name.Equals(SecretsBaseline, StringComparison.Ordinal)
            || name.EndsWith(".baseline.txt", StringComparison.Ordinal)
            || name.EndsWith("-baseline.txt", StringComparison.Ordinal);
    }

    /// <summary>
    /// The entries of a baseline: one per non-blank line that is not a <c>#</c> comment, or, for
    /// <c>.secrets.baseline</c>, one <c>filename|type|hashed_secret</c> per allowlisted secret.
    /// </summary>
    /// <exception cref="JsonException"><c>.secrets.baseline</c> is not valid JSON.</exception>
    public static IReadOnlyList<string> Entries(string path, string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        if (System.IO.Path.GetFileName(path).Equals(SecretsBaseline, StringComparison.Ordinal))
        {
            return SecretEntries(content);
        }

        return [.. content.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];
    }

    /// <summary>
    /// The entries in <paramref name="after"/> that <paramref name="before"/> does not have, counted as a multiset: a
    /// reordered list adds nothing, and a duplicated entry adds one.
    /// </summary>
    public static IReadOnlyList<string> Additions(IEnumerable<string> before, IEnumerable<string> after)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string entry in before)
        {
            remaining[entry] = remaining.GetValueOrDefault(entry) + 1;
        }

        var added = new List<string>();
        foreach (string entry in after)
        {
            if (remaining.TryGetValue(entry, out int count) && count > 0)
            {
                remaining[entry] = count - 1;
            }
            else
            {
                added.Add(entry);
            }
        }

        added.Sort(StringComparer.Ordinal);
        return added;
    }

    /// <summary>
    /// Checks every ratchet baseline against the merge base of <paramref name="baseRef"/> and <c>HEAD</c>: the staged copy
    /// with <paramref name="preCommit"/>, otherwise the working tree (committed and uncommitted changes). Returns 0 when no
    /// baseline grew or the growth is admitted, and 1 otherwise. Without the base ref the check is skipped locally and fails
    /// in CI (<c>CI</c> or <c>GITHUB_ACTIONS</c> set), where a missing ref would otherwise let any growth through.
    /// </summary>
    public static int Check(string repoRoot, bool preCommit, string baseRef = DefaultBaseRef, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        string? mergeBase = Git(repoRoot, "merge-base", baseRef, "HEAD")?.Trim();
        if (string.IsNullOrEmpty(mergeBase))
        {
            if (IsCi(environment))
            {
                Console.WriteLine($"{Tag} {baseRef} not found or shares no history with HEAD, so the baselines cannot be compared.");
                Console.WriteLine($"  Fetch it with full history (actions/checkout 'fetch-depth: 0', or 'git fetch origin main').");
                return 1;
            }

            Console.WriteLine($"{Tag} Skipped: {baseRef} not found or shares no history with HEAD. Run 'git fetch origin main' to check the baselines.");
            return 0;
        }

        List<Growth> growth;
        try
        {
            growth = FindGrowth(repoRoot, mergeBase, preCommit);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"{Tag} {SecretsBaseline} is not valid JSON: {ex.Message}");
            return 1;
        }

        IReadOnlyList<string> reasons = growth.Count > 0 ? TrailerReasons(repoRoot, mergeBase) : [];
        return Evaluate(growth, reasons, preCommit ? environment(GrowthEnvironmentVariable) : null, preCommit, baseRef);
    }

    /// <summary>The decision, separated from git so it can be tested.</summary>
    /// <param name="growth">The baselines that gained entries.</param>
    /// <param name="trailerReasons">The <c>Baseline-Growth</c> trailer values on the branch's commits.</param>
    /// <param name="stagedReason">The <c>CADENCE_BASELINE_GROWTH</c> value, honoured only in the pre-commit hook.</param>
    /// <param name="preCommit">Whether the staged files were checked.</param>
    /// <param name="baseRef">The ref the branch was compared with, for messages.</param>
    public static int Evaluate(IReadOnlyList<Growth> growth, IReadOnlyList<string> trailerReasons, string? stagedReason, bool preCommit, string baseRef = DefaultBaseRef)
    {
        if (growth.Count == 0)
        {
            Console.WriteLine($"{Tag} No ratchet baseline grew since the merge base with {baseRef}.");
            return 0;
        }

        string summary = string.Join(", ", growth.Select(g => $"{g.Path} +{g.Added.Count}"));
        if (trailerReasons.Count > 0)
        {
            Console.WriteLine($"{Tag} Baseline growth admitted by a '{TrailerKey}:' trailer ({summary}):");
            foreach (string reason in trailerReasons)
            {
                Console.WriteLine($"  {TrailerKey}: {reason}");
            }

            return 0;
        }

        if (preCommit && !string.IsNullOrWhiteSpace(stagedReason))
        {
            Console.WriteLine($"{Tag} {GrowthEnvironmentVariable} admits the staged baseline growth ({summary}).");
            Console.WriteLine($"  Put '{TrailerKey}: {stagedReason.Trim()}' in this commit's message: 'cadence check' and CI require the trailer.");
            return 0;
        }

        Console.WriteLine($"{Tag} These ratchet baselines grew since the merge base with {baseRef}, but a baseline may only shrink:");
        foreach (var file in growth)
        {
            Console.WriteLine($"  {file.Path}: {file.Added.Count} new entr{(file.Added.Count == 1 ? "y" : "ies")}");
            foreach (string entry in file.Added.Take(MaxListed))
            {
                Console.WriteLine($"    + {entry}");
            }

            if (file.Added.Count > MaxListed)
            {
                Console.WriteLine($"    ... and {file.Added.Count - MaxListed} more");
            }
        }

        Console.WriteLine("  Fix the new violations instead of recording them. If the debt must be accepted, say why in the commit:");
        Console.WriteLine($"    a '{TrailerKey}: <reason>' trailer on a commit of the branch (git commit --trailer \"{TrailerKey}: <reason>\"),");
        Console.WriteLine($"    and, for the commit that stages the growth, {GrowthEnvironmentVariable}=\"<reason>\" for the pre-commit hook.");
        return 1;
    }

    private static List<Growth> FindGrowth(string repoRoot, string mergeBase, bool preCommit)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        paths.UnionWith(Lines(Git(repoRoot, "ls-tree", "-r", "--name-only", mergeBase)).Where(IsRatchetBaseline));
        paths.UnionWith(Lines(Git(repoRoot, "ls-files")).Where(IsRatchetBaseline));

        var growth = new List<Growth>();
        foreach (string path in paths)
        {
            string? before = Git(repoRoot, "show", $"{mergeBase}:{path}");
            string? after = preCommit ? Git(repoRoot, "show", $":{path}") : ReadWorkingTree(repoRoot, path);
            var added = Additions(Entries(path, before), Entries(path, after));
            if (added.Count > 0)
            {
                growth.Add(new Growth(path, added));
            }
        }

        return growth;
    }

    private static List<string> TrailerReasons(string repoRoot, string mergeBase) =>
        [.. Lines(Git(repoRoot, "log", $"--format=%(trailers:key={TrailerKey},valueonly)", $"{mergeBase}..HEAD"))];

    private static List<string> SecretEntries(string content)
    {
        using var document = JsonDocument.Parse(content);
        var entries = new List<string>();
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Object)
        {
            return entries;
        }

        foreach (var file in results.EnumerateObject())
        {
            if (file.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var secret in file.Value.EnumerateArray())
            {
                entries.Add($"{file.Name}|{Property(secret, "type")}|{Property(secret, "hashed_secret")}");
            }
        }

        return entries;
    }

    private static string Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool IsCi(Func<string, string?> environment) =>
        IsTrue(environment("CI")) || IsTrue(environment("GITHUB_ACTIONS"));

    private static bool IsTrue(string? value) =>
        value is not null && (value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1");

    private static string? ReadWorkingTree(string repoRoot, string path)
    {
        string full = System.IO.Path.Combine(repoRoot, path);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    private static string[] Lines(string? output) =>
        (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Git(string repoRoot, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = repoRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            var error = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            error.GetAwaiter().GetResult();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
