using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Keeps warning suppressions visible and justified (#4794): the API projects keep requiring XML comments, the CLI
/// suppresses no trim or AOT warning, and every hand-written suppression states its reason.
/// </summary>
public partial class WarningSuppressionPolicyTests
{
    private static readonly string[] XmlDocumentedProjects =
    [
        "packages/contracts/src/Cadence.Contracts.csproj",
        "packages/kernel/src/Cadence.Kernel.Api/Cadence.Kernel.Api.csproj",
        "packages/kernel/src/Cadence.Standards/Cadence.Standards.csproj",
        "packages/kernel/src/Cadence.Rules/Cadence.Rules.csproj",
    ];

    private static readonly string[] SourceRoots = ["apps", "packages", "tests"];

    [Fact]
    public void ApiProjects_RequireXmlComments()
    {
        string root = KernelBoundaryBaseline.RepositoryRoot();

        foreach (string project in XmlDocumentedProjects)
        {
            File.ReadAllText(Path.Combine(root, project)).Should()
                .Contain("<CadenceRequireXmlDocs>true</CadenceRequireXmlDocs>", $"{project} is an API other code builds on");
        }

        File.ReadAllText(Path.Combine(root, "Directory.Build.targets")).Should().Contain("CadenceRequireXmlDocs");
    }

    [Fact]
    public void Cli_SuppressesNoTrimOrAotWarning()
    {
        string csproj = File.ReadAllText(Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), "packages/cli/src/Cadence.Cli.csproj"));

        NoWarnRegex().Matches(csproj).Select(m => m.Groups[1].Value)
            .Should().NotContain(v => IlWarningRegex().IsMatch(v), "an AOT or trim warning means the binary can break at runtime");
        csproj.Should().NotContain("TrimmerRootAssembly").And.NotContain("RootAllApplicationAssemblies");
    }

    [Fact]
    public void HandWrittenSuppressions_StateTheirReason()
    {
        var unjustified = HandWrittenSources()
            .SelectMany(file => UnjustifiedSuppressions(File.ReadAllText(file.FullPath)).Select(line => $"{file.RelativePath}:{line}"))
            .ToList();

        unjustified.Should().BeEmpty(
            "a #pragma warning disable needs a comment on its line and a [SuppressMessage] needs a Justification");
    }

    [Theory]
    [InlineData("#pragma warning disable S101\nclass A {}", 1)]
    [InlineData("#pragma warning disable S101 // RAG is the published acronym\nclass A {}", 0)]
    [InlineData("#pragma warning restore S101\n", 0)]
    [InlineData("[SuppressMessage(\"Naming\", \"S101\")]\nclass A {}", 1)]
    [InlineData("[SuppressMessage(\"Naming\", \"S101\",\n    Justification = \"BOIN is a design name.\")]\nclass A {}", 0)]
    public void UnjustifiedSuppressions_FindsSuppressionsWithoutAReason(string source, int expected)
    {
        UnjustifiedSuppressions(source).Should().HaveCount(expected);
    }

    internal static IEnumerable<int> UnjustifiedSuppressions(string source)
    {
        foreach (Match pragma in PragmaRegex().Matches(source))
        {
            if (!pragma.Groups["rest"].Value.Contains("//", StringComparison.Ordinal))
            {
                yield return LineOf(source, pragma.Index);
            }
        }

        foreach (Match attribute in SuppressMessageRegex().Matches(source))
        {
            if (!attribute.Value.Contains("Justification", StringComparison.Ordinal))
            {
                yield return LineOf(source, attribute.Index);
            }
        }
    }

    private static IEnumerable<(string FullPath, string RelativePath)> HandWrittenSources()
    {
        string root = KernelBoundaryBaseline.RepositoryRoot();
        return SourceRoots
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            .Select(path => (FullPath: path, RelativePath: Path.GetRelativePath(root, path).Replace('\\', '/')))
            .Where(f => !IsGeneratedOrBuildOutput(f.RelativePath) && f.RelativePath != "tests/Cadence.ArchitectureTests/Architecture/WarningSuppressionPolicyTests.cs");
    }

    // EF Core migrations are generated and carry the tool's own suppressions.
    private static bool IsGeneratedOrBuildOutput(string relativePath) =>
        relativePath.Split('/').Any(segment => segment is "bin" or "obj" or "node_modules" or "Migrations");

    private static int LineOf(string source, int index) => source.AsSpan(0, index).Count('\n') + 1;

    [GeneratedRegex(@"^[ \t]*#pragma warning disable(?<rest>[^\r\n]*)", RegexOptions.Multiline)]
    private static partial Regex PragmaRegex();

    [GeneratedRegex(@"SuppressMessage\((?:[^()]|\([^()]*\))*\)")]
    private static partial Regex SuppressMessageRegex();

    [GeneratedRegex(@"<NoWarn>([^<]*)</NoWarn>")]
    private static partial Regex NoWarnRegex();

    [GeneratedRegex(@"\bIL[23]\d{3}\b")]
    private static partial Regex IlWarningRegex();
}
