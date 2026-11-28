using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Zero-baseline architecture sentinels enforcing that every non-registry C# source file across
/// <c>packages/tooling_core</c>, <c>packages/cli</c>, and <c>packages/compliance</c> stays at or below 500
/// non-empty lines and that private <c>FindRepoRoot</c> duplicates never recur.
/// </summary>
public partial class PackageFileSizeSentinelTests
{
    public const int MaxNonEmptyLines = 500;

    private static readonly string[] TargetPackageDirectories =
    [
        "packages/tooling_core",
        "packages/cli",
        "packages/compliance",
    ];

    private static readonly HashSet<string> ExemptDeclarativeRegistries = new(StringComparer.Ordinal)
    {
        "packages/cli/src/Services/QualityGates.cs",
        "packages/cli/src/Services/CiPlan.cs",
    };

    [GeneratedRegex(@"\bprivate\s+static\s+string\s+FindRepoRoot\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateFindRepoRootRegex();

    [Fact(DisplayName = "TargetPackages_NonRegistryCSharpFiles_MustNotExceed500NonEmptyLines")]
    [Trait("Category", "Architecture")]
    public void TargetPackagesNonRegistryCSharpFilesMustNotExceed500NonEmptyLines()
    {
        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();

        foreach (string exemptPath in ExemptDeclarativeRegistries)
        {
            string fullExemptPath = Path.Combine(repoRoot, exemptPath.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(fullExemptPath).Should().BeTrue(
                $"exempt declarative registry '{exemptPath}' must exist; remove stale exemptions immediately");
        }

        var files = EnumerateTargetPackageCSharpFiles(repoRoot)
            .Select(fullPath =>
            {
                string relPath = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
                return (RelativePath: relPath, Lines: (IReadOnlyList<string>)File.ReadAllLines(fullPath));
            })
            .ToList();

        files.Should().NotBeEmpty("target package directories must contain C# source files");

        var violations = EvaluateFileSizeCeiling(files);
        violations.Should().BeEmpty(
            $"every non-registry C# file in {string.Join(", ", TargetPackageDirectories)} must contain at most {MaxNonEmptyLines} non-empty lines (!string.IsNullOrWhiteSpace(line)); split growing services into vertical slice files instead of adding baseline exemptions");
    }

    [Fact(DisplayName = "TargetPackages_MustContainZeroPrivateFindRepoRootDuplicates")]
    [Trait("Category", "Architecture")]
    public void TargetPackagesMustContainZeroPrivateFindRepoRootDuplicates()
    {
        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();

        var files = EnumerateTargetPackageCSharpFiles(repoRoot)
            .Select(fullPath =>
            {
                string relPath = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
                return (RelativePath: relPath, Content: File.ReadAllText(fullPath));
            })
            .ToList();

        var duplicates = EvaluatePrivateFindRepoRootDuplicates(files);
        duplicates.Should().BeEmpty(
            "workspace root resolution must delegate to shared WorkspacePrimitives.FindRepoRoot() rather than duplicating private FindRepoRoot helpers");
    }

    [Fact(DisplayName = "PackageFileSizeSentinel_Bites_At501NonEmptyLines_AndAllows500OrExemptRegistries")]
    [Trait("Category", "Architecture")]
    public void PackageFileSizeSentinelBitesAt501NonEmptyLinesAndAllows500OrExemptRegistries()
    {
        string[] fiveHundredWithBlanks =
        [
            .. Enumerable.Range(1, 500).Select(i => $"    public const int Item{i} = {i};"),
            "",
            "   ",
            "\t",
        ];
        string[] fiveHundredAndOne =
        [
            .. Enumerable.Range(1, 501).Select(i => $"    public const int Item{i} = {i};"),
        ];
        string[] nineHundredRegistry =
        [
            .. Enumerable.Range(1, 900).Select(i => $"    public const int Gate{i} = {i};"),
        ];

        var syntheticFiles = new (string RelativePath, IReadOnlyList<string> Lines)[]
        {
            ("packages/tooling_core/src/Services/AtLimitService.cs", fiveHundredWithBlanks),
            ("packages/tooling_core/src/Services/OverLimitService.cs", fiveHundredAndOne),
            ("packages/cli/src/Services/QualityGates.cs", nineHundredRegistry),
            ("packages/cli/src/Services/CiPlan.cs", nineHundredRegistry),
            ("packages/cli/src/Services/NonExemptCliService.cs", fiveHundredAndOne),
            ("packages/compliance/src/OverLimitScanner.cs", fiveHundredAndOne),
        };

        var violations = EvaluateFileSizeCeiling(syntheticFiles);
        violations.Should().Equal(
            "packages/cli/src/Services/NonExemptCliService.cs (501 non-empty lines > 500)",
            "packages/compliance/src/OverLimitScanner.cs (501 non-empty lines > 500)",
            "packages/tooling_core/src/Services/OverLimitService.cs (501 non-empty lines > 500)");
    }

    [Fact(DisplayName = "PrivateFindRepoRootSentinel_Bites_WhenDuplicateHelperIsIntroduced")]
    [Trait("Category", "Architecture")]
    public void PrivateFindRepoRootSentinelBitesWhenDuplicateHelperIsIntroduced()
    {
        var syntheticFiles = new (string RelativePath, string Content)[]
        {
            ("packages/tooling_core/src/Services/GoodService.cs", "public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();"),
            ("packages/tooling_core/src/Services/BadService.cs", "private static string FindRepoRoot() => Directory.GetCurrentDirectory();"),
        };

        var duplicates = EvaluatePrivateFindRepoRootDuplicates(syntheticFiles);
        duplicates.Should().Equal("packages/tooling_core/src/Services/BadService.cs");
    }

    internal static IReadOnlyList<string> EvaluateFileSizeCeiling(
        IEnumerable<(string RelativePath, IReadOnlyList<string> Lines)> files)
    {
        var violations = new List<string>();
        foreach (var (relativePath, lines) in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            string normalized = relativePath.Replace('\\', '/');
            if (ExemptDeclarativeRegistries.Contains(normalized))
            {
                continue;
            }

            int nonEmptyCount = CountNonEmptyLines(lines);
            if (nonEmptyCount > MaxNonEmptyLines)
            {
                violations.Add($"{normalized} ({nonEmptyCount} non-empty lines > {MaxNonEmptyLines})");
            }
        }

        return violations;
    }

    internal static IReadOnlyList<string> EvaluatePrivateFindRepoRootDuplicates(
        IEnumerable<(string RelativePath, string Content)> files) =>
        files
            .Where(f => PrivateFindRepoRootRegex().IsMatch(f.Content))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

    internal static int CountNonEmptyLines(IEnumerable<string> lines) =>
        lines.Count(line => !string.IsNullOrWhiteSpace(line));

    private static IEnumerable<string> EnumerateTargetPackageCSharpFiles(string repoRoot)
    {
        foreach (string packageDir in TargetPackageDirectories)
        {
            string rootPath = Path.Combine(repoRoot, packageDir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            var stack = new Stack<string>();
            stack.Push(rootPath);
            while (stack.Count > 0)
            {
                string current = stack.Pop();
                foreach (string subDir in Directory.EnumerateDirectories(current))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (dirName is "bin" or "obj" or "node_modules" or ".git")
                    {
                        continue;
                    }

                    stack.Push(subDir);
                }

                foreach (string file in Directory.EnumerateFiles(current, "*.cs"))
                {
                    yield return file;
                }
            }
        }
    }
}
