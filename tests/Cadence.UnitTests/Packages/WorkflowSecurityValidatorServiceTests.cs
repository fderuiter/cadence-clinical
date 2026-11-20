using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class WorkflowSecurityValidatorServiceTests : IDisposable
{
    private const string ValidCheckoutSha = "3d3c42e5aac5ba805825da76410c181273ba90b1"; // pragma: allowlist secret
    private const string ValidSetupNodeSha = "949feb2413d6458794dcd2491c4babbbce0c15c1"; // pragma: allowlist secret
    private const string ValidGithubScriptSha = "3a2844b7e9c422d3c10d287c895573f7108da1b3"; // pragma: allowlist secret
    private const string ValidDockerDigest = "10a5c69a83035ab5239f5620d708520e8b28f5aa1f2b20143e6024532edadfb5"; // pragma: allowlist secret

    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "workflow-security-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ValidateWorkflowSecurity_Passes_OnLiveRepositoryWorkflows()
    {
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: RepoRoot()).Should().Be(0);
    }

    [Fact]
    public void ValidateWorkflowSecurity_FailsClosed_WhenNoWorkflowOrActionFilesExist()
    {
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);
    }

    [Theory]
    [InlineData(".evil/repo@main")]
    [InlineData(".evil/repo@3d3c42e5aac5ba805825da76410c181273ba90b1 # v1")]
    [InlineData("..evil/repo@3d3c42e5aac5ba805825da76410c181273ba90b1 # v1")]
    public void ValidateWorkflowSecurity_RejectsDotPrefixedExternalActions(string actionRef)
    {
        Write(".github/workflows/ci.yml", $$"""
            name: CI
            on: push
            permissions:
              contents: read
            jobs:
              build:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - uses: {{actionRef}}
            """);

        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);
    }

    [Fact]
    public void ValidateWorkflowSecurity_AllowsRelativeLocalPaths_AndRejectsLocalPathWithRef()
    {
        Write(".github/workflows/ci.yml", $$"""
            name: CI
            on: push
            permissions:
              contents: read
            jobs:
              local-steps:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - uses: ./.github/actions/local-setup
                  - uses: ../shared-actions/lint
              reusable:
                uses: ./.github/workflows/reusable.yml
            """);

        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(0);

        Write(".github/workflows/bad-local.yml", """
            name: Bad Local
            on: push
            permissions:
              contents: read
            jobs:
              build:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - uses: ./.github/actions/local-setup@main
            """);

        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);
    }

    [Theory]
    [InlineData("docker://alpine:latest")]
    [InlineData("docker://alpine:3d3c42e5aac5ba805825da76410c181273ba90b1 # v3")]
    [InlineData("docker://alpine@3d3c42e5aac5ba805825da76410c181273ba90b1 # v3")]
    [InlineData("docker://alpine@sha256:3d3c42e5aac5ba805825da76410c181273ba90b1 # v3")]
    public void ValidateWorkflowSecurity_RejectsInvalidAndFortyHexDockerTags(string dockerUses)
    {
        Write(".github/workflows/docker.yml", $$"""
            name: Docker Check
            on: push
            permissions:
              contents: read
            jobs:
              scan:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - uses: {{dockerUses}}
            """);

        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);
    }

    [Fact]
    public void ValidateWorkflowSecurity_AcceptsValidSha256DockerReferences()
    {
        Write(".github/workflows/docker.yml", $$"""
            name: Docker Check
            on: push
            permissions:
              contents: read
            jobs:
              scan:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - uses: docker://alpine:3.20@sha256:{{ValidDockerDigest}}
            """);
        Write(".github/actions/container-step/action.yml", $$"""
            name: Container Step
            description: Runs pinned container image
            runs:
              using: "docker"
              image: "docker://ghcr.io/cadence/scanner:1.0@sha256:{{ValidDockerDigest}}"
            """);

        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(0);
    }

    [Theory]
    [InlineData("action.yml", "actions/setup-node@v7")]
    [InlineData("action.yaml", "actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1")]
    public void ValidateWorkflowSecurity_RejectsUnpinnedOrUncommentedCompositeActions(string fileName, string stepUses)
    {
        Write($".github/actions/setup/{fileName}", $$"""
            name: Setup Composite
            description: Composite action fixture
            runs:
              using: "composite"
              steps:
                - name: Setup Node
                  uses: {{stepUses}}
            """);

        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);
    }

    [Fact]
    public void ValidateWorkflowSecurity_AcceptsPinnedCompositeActionWithPersistCredentialsFalse()
    {
        Write(".github/actions/setup/action.yaml", $$"""
            name: Setup Composite
            description: Composite action fixture
            runs:
              using: "composite"
              steps:
                - name: Check out
                  uses: actions/checkout@{{ValidCheckoutSha}} # v7
                  with:
                    persist-credentials: false
                - name: Setup Node
                  uses: actions/setup-node@{{ValidSetupNodeSha}} # v7
            """);

        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("with:\n          fetch-depth: 0")]
    [InlineData("with:\n          persist-credentials: true")]
    public void ValidateWorkflowSecurity_RejectsPersistedCheckoutCredentials_InWorkflowsAndCompositeActions(string withBlock)
    {
        Write(".github/workflows/checkout.yml", $$"""
            name: Checkout Check
            on: push
            permissions:
              contents: read
            jobs:
              build:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - name: Check out
                    uses: actions/checkout@{{ValidCheckoutSha}} # v7
                    {{withBlock}}
            """);

        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        File.Delete(Path.Combine(_root, ".github", "workflows", "checkout.yml"));
        Write(".github/actions/checkout-composite/action.yml", $$"""
            name: Composite Checkout
            description: Composite checkout fixture
            runs:
              using: "composite"
              steps:
                - name: Check out
                  uses: actions/checkout@{{ValidCheckoutSha}} # v7
                  {{withBlock}}
            """);

        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);
    }

    [Fact]
    public void ValidateWorkflowSecurity_RejectsMissingOrUnscopedWritePermissions()
    {
        // 1. Missing top-level permissions
        Write(".github/workflows/no-perms.yml", $$"""
            name: Missing Permissions
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/setup-node@{{ValidSetupNodeSha}} # v7
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 2. Top-level write permission
        Write(".github/workflows/no-perms.yml", $$"""
            name: Broad Top-Level Write
            on: push
            permissions:
              contents: write
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/setup-node@{{ValidSetupNodeSha}} # v7
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 3. Job-level write-all
        Write(".github/workflows/no-perms.yml", $$"""
            name: Write All Job
            on: push
            permissions:
              contents: read
            jobs:
              build:
                runs-on: ubuntu-latest
                permissions: write-all
                steps:
                  - uses: actions/setup-node@{{ValidSetupNodeSha}} # v7
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 4. contents: write on pull_request job
        Write(".github/workflows/no-perms.yml", $$"""
            name: PR Contents Write
            on:
              pull_request:
            permissions:
              contents: read
            jobs:
              automerge:
                runs-on: ubuntu-latest
                permissions:
                  contents: write
                  pull-requests: write
                steps:
                  - uses: actions/setup-node@{{ValidSetupNodeSha}} # v7
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);
    }

    [Fact]
    public void ValidateWorkflowSecurity_EnforcesBotActorGatingAndIssuesOnlyScopeOnIssueComment()
    {
        // Ungated issue_comment job with write permissions fails
        Write(".github/workflows/comment.yml", $$"""
            name: Ungated Comment Bot
            on:
              issue_comment:
                types: [created]
            permissions:
              contents: read
            jobs:
              sanitize:
                if: github.event.issue.pull_request && contains(github.event.comment.body, '[CI/CD Fix Attempt ')
                runs-on: ubuntu-latest
                permissions:
                  issues: write
                steps:
                  - uses: actions/github-script@{{ValidGithubScriptSha}} # v9
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // Gated issue_comment job that still requests pull-requests: write fails
        Write(".github/workflows/comment.yml", $$"""
            name: Over-privileged Comment Bot
            on:
              issue_comment:
                types: [created]
            permissions:
              contents: read
            jobs:
              sanitize:
                if: >-
                  github.event.issue.pull_request != null &&
                  github.event.comment.user.type == 'Bot' &&
                  github.event.comment.user.login == 'github-actions[bot]'
                runs-on: ubuntu-latest
                permissions:
                  issues: write
                  pull-requests: write
                steps:
                  - uses: actions/github-script@{{ValidGithubScriptSha}} # v9
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // Properly gated issue_comment job with issues: write only passes
        Write(".github/workflows/comment.yml", $$"""
            name: Least-Privilege Comment Bot
            on:
              issue_comment:
                types: [created]
            permissions:
              contents: read
            jobs:
              sanitize:
                if: >-
                  github.event.issue.pull_request != null &&
                  github.event.comment.user.type == 'Bot' &&
                  github.event.comment.user.login == 'github-actions[bot]'
                runs-on: ubuntu-latest
                permissions:
                  issues: write
                steps:
                  - uses: actions/github-script@{{ValidGithubScriptSha}} # v9
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(0);
    }

    [Theory]
    [InlineData("docs/.vitepress/config.mjs", false)]
    [InlineData("docs/.vitepress/README.md", false)]
    [InlineData("docs/SDLC/01_Product_Requirements_Document_PRD.md", false)]
    [InlineData("docs/openapi/execution_openapi.json", false)]
    [InlineData("docs/openapi/README.md", false)]
    [InlineData("docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md", false)]
    [InlineData("docs/adr/index.md", false)]
    [InlineData("docs/SRS.md", false)]
    [InlineData("AGENTS.md", false)]
    [InlineData("CLAUDE.md", false)]
    [InlineData("SECURITY.md", false)]
    [InlineData(".agents/skills/tdd/SKILL.md", false)]
    [InlineData(".github/pull_request_template.md", false)]
    [InlineData(".github/workflows/ci.yml", false)]
    [InlineData("scripts/setup_wizard.sh", false)]
    [InlineData("docs/migration/phase0/04_scaffold_solution.sh", false)]
    [InlineData("README.md", true)]
    [InlineData("CONTRIBUTING.md", true)]
    [InlineData("docs/CI.md", true)]
    [InlineData("docs/TESTING.md", true)]
    [InlineData("apps/execution/README.md", true)]
    [InlineData("packages/cli/README.md", true)]
    public void CiWorkflowClassifyJob_ClassifiesChangedPathsWithStrictAllowlist(string changedFile, bool expectedSafe)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string ciPath = Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml");
        string classifyLoop = ExtractClassifyLoopFromCiWorkflow(File.ReadAllLines(ciPath));

        string workDir = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        string changedFilesPath = Path.Combine(workDir, "changed_files.txt");
        string githubOutputPath = Path.Combine(workDir, "github_output.txt");
        File.WriteAllText(changedFilesPath, changedFile + "\n");
        File.WriteAllText(githubOutputPath, string.Empty);

        var psi = new ProcessStartInfo("bash")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["GITHUB_OUTPUT"] = githubOutputPath;
        psi.ArgumentList.Add("-eo");
        psi.ArgumentList.Add("pipefail");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(classifyLoop);

        using var proc = Process.Start(psi)!;
        string stderr = proc.StandardError.ReadToEnd();
        proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        proc.ExitCode.Should().Be(0, $"classify script failed on '{changedFile}': {stderr}");
        string output = File.ReadAllText(githubOutputPath);
        output.Should().Contain($"is_safe={(expectedSafe ? "true" : "false")}",
            $"'{changedFile}' must classify as is_safe={expectedSafe}");
    }

    [Fact]
    public void CiAndSanitizeWorkflows_EnforceLeastPrivilegeAndFailLoudlyOnLabels()
    {
        string ciContent = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        ciContent.Should().NotContain("gh label create", "classify must not recreate repository labels on pull requests");
        ciContent.Should().NotContain("Failed to apply label. Ignoring.", "classify must fail loudly on label errors");
        ciContent.Should().NotContain("check-action-pinning.py", "retired Python pinning script must not be invoked");

        File.Exists(Path.Combine(RepoRoot(), "scripts", "check-action-pinning.py")).Should().BeFalse();

        string sanitizeContent = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "sanitize-bot-comment.yml"));
        sanitizeContent.Should().Contain("github.event.comment.user.type == 'Bot'");
        sanitizeContent.Should().NotContain("pull-requests: write");
        sanitizeContent.Should().Contain("issues: write");
    }

    private static string ExtractClassifyLoopFromCiWorkflow(string[] lines)
    {
        int start = Array.FindIndex(lines, l => l.Trim() == "SAFE=true");
        int end = Array.FindIndex(lines, start, l => l.Contains("echo \"container_scan=$CONTAINER_SCAN\" >> \"$GITHUB_OUTPUT\"", StringComparison.Ordinal));
        start.Should().BeGreaterThan(0);
        end.Should().BeGreaterThan(start);

        var scriptLines = lines[start..(end + 1)]
            .Select(l => l.Length >= 10 ? l[10..] : l.TrimStart());
        return string.Join('\n', scriptLines);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n") + "\n");
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Cadence.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName ?? throw new InvalidOperationException("Cadence.sln not found.");
        }

        return dir;
    }
}
