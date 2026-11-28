using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Cadence.ToolingCore.Primitives;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class SharedPrimitivesAndSliceMergeTests
{
    [Fact]
    public void WorkspacePrimitives_FindRepoRootAndUtf8Helpers_HandleBomInvalidBytesAndStagedBlobs()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"cadence-ws-prim-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "nested", "deep"));
        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "Cadence.sln"), "");
            string nestedDir = Path.Combine(tempRoot, "nested", "deep");
            WorkspacePrimitives.FindRepoRoot(nestedDir).Should().Be(tempRoot);

            string utf8BomPath = Path.Combine(tempRoot, "bom.txt");
            byte[] bomPayload = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("hello-cadence")];
            File.WriteAllBytes(utf8BomPath, bomPayload);

            WorkspacePrimitives.TryReadUtf8Text(utf8BomPath, "bom.txt", indexReader: null, stripBom: true).Should().Be("hello-cadence");
            WorkspacePrimitives.TryReadUtf8Text(utf8BomPath, "bom.txt", indexReader: null, stripBom: false).Should().Be("\uFEFFhello-cadence");

            string invalidUtf8Path = Path.Combine(tempRoot, "invalid.bin");
            File.WriteAllBytes(invalidUtf8Path, [0xFF, 0xFE, 0xFD]);
            WorkspacePrimitives.TryReadUtf8Text(invalidUtf8Path, "invalid.bin", indexReader: null).Should().BeNull();

            ProcessPrimitives.TryRunCaptured("git", tempRoot, ["init"], out _).Should().BeTrue();
            ProcessPrimitives.TryRunCaptured("git", tempRoot, ["add", "bom.txt"], out _).Should().BeTrue();
            File.WriteAllText(utf8BomPath, "unstaged-working-tree-edit");

            using var stagedReader = GitIndexReader.TryOpen(tempRoot);
            stagedReader.Should().NotBeNull();
            WorkspacePrimitives.TryReadUtf8Text(utf8BomPath, "bom.txt", stagedReader, stripBom: true).Should().Be("hello-cadence");
            WorkspacePrimitives.TryReadUtf8Text(utf8BomPath, "bom.txt", indexReader: null).Should().Be("unstaged-working-tree-edit");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ProcessPrimitives_TryRunCaptured_CapturesStdoutExitCodeAndMissingExecutable()
    {
        bool ranGit = ProcessPrimitives.TryRunCaptured(
            "git",
            workingDirectory: null,
            arguments: ["--version"],
            out var gitOutput);

        ranGit.Should().BeTrue();
        gitOutput.ExitCode.Should().Be(0);
        gitOutput.Stdout.Should().Contain("git version");

        bool ranMissing = ProcessPrimitives.TryRunCaptured(
            "cadence-nonexistent-binary-9f7b2e",
            workingDirectory: null,
            arguments: [],
            out var missingOutput);

        ranMissing.Should().BeFalse();
        missingOutput.ExitCode.Should().Be(-1);
    }

    [Fact]
    public void YamlPrimitives_CommentMappingFlowArrayAndBlockScalars_PreserveQuotedHashesAndFoldedLines()
    {
        YamlPrimitives.CountLeadingSpaces("    steps:").Should().Be(4);
        YamlPrimitives.StripLineComment("  name: \"CI # Gate\" # trailing comment")
            .Should().Be("  name: \"CI # Gate\"");
        YamlPrimitives.StripLineComment("  url: 'https://example.test/#anchor' # note")
            .Should().Be("  url: 'https://example.test/#anchor'");

        YamlPrimitives.TrySplitMapping("  \"runs-on\": ubuntu-latest # comment", out string key, out string rawVal)
            .Should().BeTrue();
        key.Should().Be("runs-on");
        YamlPrimitives.ParseScalar(rawVal).Should().Be("ubuntu-latest");

        var flowItems = YamlPrimitives.ParseFlowStringArray("['opened', \"synchronize\", edited, 'a,b']");
        flowItems.Should().Equal("opened", "synchronize", "edited", "a,b");

        string[] blockLines =
        [
            "    if: >-",
            "      github.event_name == 'push' ||",
            "      github.event_name == 'merge_group'",
            "    steps:",
        ];
        YamlPrimitives.TryParseBlockHeader(">-", out char style, out char chomp, out int indentHint).Should().BeTrue();
        var (folded, nextIndex) = YamlPrimitives.ConsumeBlockScalar(blockLines, 1, blockLines.Length, 4, style, chomp, indentHint);
        folded.Should().Be("github.event_name == 'push' || github.event_name == 'merge_group'");
        nextIndex.Should().Be(3);
    }

    [Fact]
    public void WorkflowExpressionPrimitives_UnwrapSplitExtractAndStrip_IgnoreOperatorsInsideQuotesAndParens()
    {
        string raw = "${{ (github.event_name == 'pull_request' || github.event_name == 'merge_group') && needs.classify.outputs.dotnet == 'true' }}";
        string unwrapped = WorkflowExpressionPrimitives.UnwrapExpression(raw);

        var andParts = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "&&");
        andParts.Should().HaveCount(2);

        string innerOr = WorkflowExpressionPrimitives.UnwrapExpression(andParts[0]);
        var orParts = WorkflowExpressionPrimitives.SplitTopLevelLogical(innerOr, "||");
        orParts.Should().Equal(
            "github.event_name == 'pull_request'",
            "github.event_name == 'merge_group'");

        string shellLine = "echo \"${{ github.event.head_commit.message }}\" && echo '${{ secrets.SAFE }}' # done";
        WorkflowExpressionPrimitives.ExtractExpressions(shellLine).Should().Equal(
            "github.event.head_commit.message",
            "secrets.SAFE");
        WorkflowExpressionPrimitives.StripExpressions(shellLine).Should().Be("echo \"\" && echo '' # done");
    }

    [Fact]
    public void DecomposedRuleSlices_SimulatedParallelGeneratedRegexAdditions_MergeCleanlyWithGitMergeFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"cadence-merge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            const string baseCoordinator = """
                namespace Cadence.ToolingCore.Services;

                public static partial class SampleValidatorService
                {
                    public static void Validate(string line)
                    {
                        ValidateAlphaRules(line);
                        // ---
                        ValidateOmegaRules(line);
                    }
                }
                """;

            string branchACoordinator = baseCoordinator.Replace(
                "        ValidateAlphaRules(line);",
                "        ValidateAlphaRules(line);\n        ValidateBranchARule(line);",
                StringComparison.Ordinal);

            string branchBCoordinator = baseCoordinator.Replace(
                "        ValidateOmegaRules(line);",
                "        ValidateOmegaRules(line);\n        ValidateBranchBRule(line);",
                StringComparison.Ordinal);

            string basePath = Path.Combine(tempDir, "base.cs");
            string oursPath = Path.Combine(tempDir, "ours.cs");
            string theirsPath = Path.Combine(tempDir, "theirs.cs");
            File.WriteAllText(basePath, baseCoordinator);
            File.WriteAllText(oursPath, branchACoordinator);
            File.WriteAllText(theirsPath, branchBCoordinator);

            // Because each rule slice owns its private [GeneratedRegex] in its own partial file
            // (SampleValidatorService.BranchA.cs vs SampleValidatorService.BranchB.cs) rather than appending to a shared
            // bottom-of-file [GeneratedRegex] block, git merge-file merges concurrent rule additions cleanly with exit code 0.
            bool ran = ProcessPrimitives.TryRunCaptured(
                "git",
                tempDir,
                ["merge-file", "-p", oursPath, basePath, theirsPath],
                out var mergeResult);

            ran.Should().BeTrue();
            mergeResult.ExitCode.Should().Be(0, $"git merge-file should succeed without conflict markers: {mergeResult.Stderr}");
            mergeResult.Stdout.Should().Contain("ValidateBranchARule(line);");
            mergeResult.Stdout.Should().Contain("ValidateBranchBRule(line);");
            mergeResult.Stdout.Should().NotContain("<<<<<<<");

            string repoRoot = WorkspacePrimitives.FindRepoRoot();
            string bundleCoordinatorPath = Path.Combine(repoRoot, "packages/tooling_core/src/Services/PublicBundleValidatorService.cs");
            string htmlSlicePath = Path.Combine(repoRoot, "packages/tooling_core/src/Services/PublicBundleValidatorService.HtmlAndCspRules.cs");
            string urlSlicePath = Path.Combine(repoRoot, "packages/tooling_core/src/Services/PublicBundleValidatorService.ExternalUrlRules.cs");
            string contaminationSlicePath = Path.Combine(repoRoot, "packages/tooling_core/src/Services/PublicBundleValidatorService.ContaminationAndBudgets.cs");

            string bundleCoordinator = File.ReadAllText(bundleCoordinatorPath);
            string htmlSlice = File.ReadAllText(htmlSlicePath);
            string urlSlice = File.ReadAllText(urlSlicePath);
            string contaminationSlice = File.ReadAllText(contaminationSlicePath);

            htmlSlice.Should().Contain("[GeneratedRegex(").And.Contain("private static partial Regex");
            urlSlice.Should().Contain("[GeneratedRegex(").And.Contain("private static partial Regex");
            contaminationSlice.Should().Contain("[GeneratedRegex(").And.Contain("private static partial Regex");

            int firstAnchor = bundleCoordinator.IndexOf("ScanExternalAssets(", StringComparison.Ordinal);
            int secondAnchor = bundleCoordinator.IndexOf("ValidateExecutableChunkContamination(", StringComparison.Ordinal);
            firstAnchor.Should().BeGreaterThanOrEqualTo(0);
            secondAnchor.Should().BeGreaterThan(firstAnchor);

            int firstLineEnd = bundleCoordinator.IndexOf('\n', firstAnchor);
            int secondLineEnd = bundleCoordinator.IndexOf('\n', secondAnchor);
            string realOurs = bundleCoordinator.Insert(firstLineEnd + 1, "        // Branch A: invoke new ExternalUrlRules slice check\n");
            string realTheirs = bundleCoordinator.Insert(secondLineEnd + 1, "            // Branch B: invoke new ContaminationAndBudgets slice check\n");

            string realBasePath = Path.Combine(tempDir, "real-base.cs");
            string realOursPath = Path.Combine(tempDir, "real-ours.cs");
            string realTheirsPath = Path.Combine(tempDir, "real-theirs.cs");
            File.WriteAllText(realBasePath, bundleCoordinator);
            File.WriteAllText(realOursPath, realOurs);
            File.WriteAllText(realTheirsPath, realTheirs);

            ProcessPrimitives.TryRunCaptured(
                "git",
                tempDir,
                ["merge-file", "-p", realOursPath, realBasePath, realTheirsPath],
                out var realMerge).Should().BeTrue();
            realMerge.ExitCode.Should().Be(0, $"real coordinator merge should succeed cleanly: {realMerge.Stderr}");
            realMerge.Stdout.Should().NotContain("<<<<<<<");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
