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

    [Fact]
    public void ValidateWorkflowSecurity_RejectsUnguardedWorkflowDispatchOnDeployAttestOrPublishJobs()
    {
        // 1. Standalone workflow_dispatch on a deployment/attestation workflow without ref guard fails
        Write(".github/workflows/deploy.yml", $$"""
            name: Unguarded Deploy
            on:
              workflow_call:
              workflow_dispatch:
            permissions:
              contents: read
            jobs:
              deploy:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                  pages: write
                  id-token: write
                steps:
                  - uses: actions/setup-node@{{ValidSetupNodeSha}} # v7
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 2. Reusable caller on workflow_dispatch without ref guard fails
        Write(".github/workflows/deploy.yml", """
            name: Unguarded Caller
            on:
              push:
                branches: [main]
              workflow_dispatch:
            permissions:
              contents: read
            jobs:
              call-release:
                if: github.event_name == 'push' || github.event_name == 'workflow_dispatch'
                permissions:
                  contents: write
                  id-token: write
                  attestations: write
                uses: ./.github/workflows/production-pipeline.yml
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 3. Ref-guarded workflow_dispatch caller restricted to main, release/*, and v* passes
        Write(".github/workflows/deploy.yml", """
            name: Ref-Guarded Caller
            on:
              push:
                branches: [main, release/*]
                tags: ["v*"]
              workflow_dispatch:
                inputs:
                  target:
                    description: "Target"
                    type: choice
                    options: [all, release-and-archive]
            permissions:
              contents: read
            jobs:
              call-release:
                if: >-
                  (github.event_name == 'push' || github.event_name == 'workflow_dispatch') &&
                  (github.ref == 'refs/heads/main' || startsWith(github.ref, 'refs/heads/release/') || startsWith(github.ref, 'refs/tags/v'))
                permissions:
                  contents: write
                  packages: write
                  id-token: write
                  attestations: write
                uses: ./.github/workflows/production-pipeline.yml
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(0);
    }

    [Fact]
    public void ValidateWorkflowSecurity_EnforcesTypedDispatchInputsAndBlocksDirectRunInterpolation()
    {
        // 1. Free-form string workflow_dispatch input fails
        Write(".github/workflows/dispatch.yml", $$"""
            name: Untyped Dispatch Input
            on:
              workflow_dispatch:
                inputs:
                  tag:
                    description: "Free-form tag"
                    type: string
            permissions:
              contents: read
            jobs:
              check:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - uses: actions/setup-node@{{ValidSetupNodeSha}} # v7
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 2. Direct ${{ inputs.* }} interpolation inside run: fails even when input is typed choice
        Write(".github/workflows/dispatch.yml", """
            name: Interpolated Dispatch Input
            on:
              workflow_dispatch:
                inputs:
                  target:
                    description: "Target"
                    type: choice
                    options: [all, validate-only]
            permissions:
              contents: read
            jobs:
              check:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - name: Unsafe echo
                    run: echo "${{ inputs.target }}"
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 3. Typed choice/boolean inputs passed via env: pass
        Write(".github/workflows/dispatch.yml", """
            name: Safe Dispatch Input
            on:
              workflow_dispatch:
                inputs:
                  target:
                    description: "Target"
                    type: choice
                    options: [all, validate-only]
                  dry_run:
                    description: "Dry run"
                    type: boolean
            permissions:
              contents: read
            jobs:
              check:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - name: Safe echo via env
                    env:
                      TARGET: ${{ inputs.target }}
                      DRY_RUN: ${{ inputs.dry_run }}
                    run: echo "$TARGET $DRY_RUN"
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(0);
    }

    [Theory]
    [InlineData("docs/.vitepress/config.mjs", false)]
    [InlineData("docs/.vitepress/README.md", false)]
    [InlineData("docs/SDLC/01_Product_Requirements_Document_PRD.md", false)]
    [InlineData("docs/openapi/execution_openapi.json", false)]
    [InlineData("docs/openapi/README.md", false)]
    [InlineData("docs/schema/README.md", false)]
    [InlineData("docs/api/execution.md", false)]
    [InlineData("docs/CDISC/README.md", false)]
    [InlineData("docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md", false)]
    [InlineData("docs/adr/index.md", false)]
    [InlineData("docs/SRS.md", false)]
    [InlineData("docs/blog/posts.data.mjs", false)]
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
    [InlineData("docs/blog/index.md", true)]
    [InlineData("docs/blog/2026-11-19-keeping-cadence-in-one-repository.md", true)]
    [InlineData("docs/agents/domain.md", true)]
    [InlineData("docs/ai/AI_AGENTS.md", true)]
    [InlineData("docs/specs/ecrf_designer_ide_spec.md", true)]
    [InlineData("apps/execution/README.md", true)]
    [InlineData("apps/safety/Schemas/ich-e2b-r3/README.md", true)]
    [InlineData("packages/cli/README.md", true)]
    [InlineData("packages/kernel/src/Cadence.Rules/README.md", true)]
    [InlineData("tests/fixtures/soa/README.md", true)]
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
    public void ValidateWorkflowSecurity_RejectsForkCheckoutsAndUntrustedHeadRefs()
    {
        // 1. Fork repository checkout is rejected
        Write(".github/workflows/conflict-check.yml", $$$"""
            name: Conflict Check
            on:
              pull_request:
                types: [opened, synchronize, reopened, edited]
            permissions:
              contents: read
            jobs:
              conflict-check:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - uses: actions/checkout@{{{ValidCheckoutSha}}} # v7
                    with:
                      persist-credentials: false
                      repository: ${{ github.event.pull_request.head.repo.full_name || github.repository }}
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 2. Untrusted pull_request.head.ref checkout is rejected
        Write(".github/workflows/conflict-check.yml", $$$"""
            name: Conflict Check
            on:
              pull_request:
                types: [opened, synchronize, reopened, edited]
            permissions:
              contents: read
            jobs:
              conflict-check:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - uses: actions/checkout@{{{ValidCheckoutSha}}} # v7
                    with:
                      persist-credentials: false
                      ref: ${{ github.event.pull_request.head.ref }}
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 3. Trusted base.ref checkout on GITHUB_REPOSITORY passes
        Write(".github/workflows/conflict-check.yml", $$$"""
            name: Conflict Check
            on:
              pull_request:
                types: [opened, synchronize, reopened, edited]
            permissions:
              contents: read
            jobs:
              conflict-check:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - uses: actions/checkout@{{{ValidCheckoutSha}}} # v7
                    with:
                      persist-credentials: false
                      ref: ${{ github.event.pull_request.base.ref }}
                      fetch-depth: 0
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(0);
    }

    [Fact]
    public void ValidateWorkflowSecurity_RequiresSameRepoForkGuardOnPullRequestWriteScopes()
    {
        // 1. Unguarded pull-requests: write with mutating gh pr edit fails
        Write(".github/workflows/classify.yml", $$$"""
            name: Classify
            on:
              pull_request:
            permissions:
              contents: read
            jobs:
              classify:
                if: github.event_name == 'pull_request'
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                  pull-requests: write
                steps:
                  - name: Apply label
                    if: steps.classify.outputs.is_safe == 'true'
                    run: gh pr edit "$PR_NUMBER" --repo "${{ github.repository }}" --add-label "safe-change"
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 2. Second unguarded mutating step alongside a guarded step still fails
        Write(".github/workflows/classify.yml", $$$"""
            name: Classify
            on:
              pull_request:
            permissions:
              contents: read
            jobs:
              classify:
                if: github.event_name == 'pull_request'
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                  pull-requests: write
                steps:
                  - name: Apply label
                    if: steps.classify.outputs.is_safe == 'true' && github.event.pull_request.head.repo.full_name == github.repository
                    run: gh pr edit "$PR_NUMBER" --repo "${{ github.repository }}" --add-label "safe-change"
                  - name: Post comment without fork guard
                    run: gh pr comment "$PR_NUMBER" --body "hi"
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 3. Read-only gh api step + fork-guarded gh pr edit step passes
        Write(".github/workflows/classify.yml", $$$"""
            name: Classify
            on:
              pull_request:
            permissions:
              contents: read
            jobs:
              classify:
                if: github.event_name == 'pull_request'
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                  pull-requests: write
                steps:
                  - name: Read changed files
                    run: gh api repos/${{ github.repository }}/pulls/${{ github.event.pull_request.number }}/files
                  - name: Apply label
                    if: steps.classify.outputs.is_safe == 'true' && github.event.pull_request.head.repo.full_name == github.repository
                    run: gh pr edit "$PR_NUMBER" --repo "${{ github.repository }}" --add-label "safe-change"
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(0);
    }

    [Fact]
    public void ValidateWorkflowSecurity_RejectsMaskedShellErrorsAndAllowsExpressions()
    {
        // 1. Masked gh pr view error ('|| echo "UNKNOWN"') fails
        Write(".github/workflows/masked.yml", """
            name: Masked
            on: push
            permissions:
              contents: read
            jobs:
              check:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - name: Masked status
                    run: |
                      STATUS=$(gh pr view "$PR_NUMBER" --json mergeable --jq .mergeable 2>/dev/null || echo "UNKNOWN")
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 2. Masked inline command ('|| true') fails
        Write(".github/workflows/masked.yml", """
            name: Masked Inline
            on: push
            permissions:
              contents: read
            jobs:
              check:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - name: Swallow failure
                    run: dotnet test || true
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(1);

        // 3. GitHub expression fallbacks and conditional tests pass
        Write(".github/workflows/masked.yml", $$$"""
            name: Fail Closed
            on: push
            permissions:
              contents: read
            jobs:
              check:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                steps:
                  - name: Scoped check
                    env:
                      BASE_REF: ${{ github.base_ref || 'main' }}
                    run: |
                      echo "Checking ${{ github.event.pull_request.number || '0' }}"
                      if [ "$BASE_REF" = "main" ] || [ "$BASE_REF" = "release/1.0" ]; then
                        echo "Valid target"
                      fi
            """);
        WorkflowSecurityValidatorService.ValidateWorkflowSecurity(repoRoot: _root).Should().Be(0);
    }

    [Fact]
    public void ConflictCheckWorkflow_FailsClosedOnConflictsAndFetchErrorsViaGitMergeTree()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string conflictWorkflowPath = Path.Combine(RepoRoot(), ".github", "workflows", "conflict-check.yml");
        string raw = File.ReadAllText(conflictWorkflowPath);
        raw.Should().Contain("types: [opened, synchronize, reopened, edited]");
        raw.Should().Contain("merge_group:");
        raw.Should().NotContain("pull-requests:");
        raw.Should().NotContain("head.repo");
        raw.Should().NotContain("gh pr view");

        string mergeTreeScript = ExtractConflictCheckScript(File.ReadAllLines(conflictWorkflowPath));

        string bareOrigin = Directory.CreateDirectory(Path.Combine(_root, "origin.git")).FullName;
        string seedWork = Directory.CreateDirectory(Path.Combine(_root, "seed")).FullName;
        string runnerClone = Path.Combine(_root, "runner");

        RunGit(bareOrigin, "init", "--bare", "--initial-branch=main");
        RunGit(seedWork, "init", "--initial-branch=main");
        RunGit(seedWork, "config", "user.email", "ci@example.invalid");
        RunGit(seedWork, "config", "user.name", "CI Fixture");
        RunGit(seedWork, "remote", "add", "origin", bareOrigin);

        File.WriteAllText(Path.Combine(seedWork, "shared.txt"), "line-1\nline-2\n");
        RunGit(seedWork, "add", "shared.txt");
        RunGit(seedWork, "commit", "-m", "initial");
        RunGit(seedWork, "push", "origin", "main");

        // PR 42: clean change on a separate file
        RunGit(seedWork, "checkout", "-b", "pr-clean", "main");
        File.WriteAllText(Path.Combine(seedWork, "docs.md"), "# Safe change\n");
        RunGit(seedWork, "add", "docs.md");
        RunGit(seedWork, "commit", "-m", "add docs");
        RunGit(seedWork, "push", "origin", "HEAD:refs/pull/42/head");

        // Advance main on shared.txt
        RunGit(seedWork, "checkout", "main");
        File.WriteAllText(Path.Combine(seedWork, "shared.txt"), "main-updated\nline-2\n");
        RunGit(seedWork, "add", "shared.txt");
        RunGit(seedWork, "commit", "-m", "advance main");
        RunGit(seedWork, "push", "origin", "main");

        // PR 43: conflicting edit to shared.txt from initial base
        RunGit(seedWork, "checkout", "-b", "pr-conflict", "HEAD~1");
        File.WriteAllText(Path.Combine(seedWork, "shared.txt"), "fork-conflicting-edit\nline-2\n");
        RunGit(seedWork, "add", "shared.txt");
        RunGit(seedWork, "commit", "-m", "conflicting edit");
        RunGit(seedWork, "push", "origin", "HEAD:refs/pull/43/head");

        RunGit(_root, "clone", "--branch", "main", bareOrigin, runnerClone);

        // 1. Clean PR passes (exit 0)
        var (cleanExit, cleanOut, cleanErr) = RunConflictScript(runnerClone, mergeTreeScript, "main", "42");
        cleanExit.Should().Be(0, $"clean PR should merge cleanly: {cleanOut}\n{cleanErr}");

        // 2. Conflicting PR fails closed (exit 1)
        var (conflictExit, conflictOut, _) = RunConflictScript(runnerClone, mergeTreeScript, "main", "43");
        conflictExit.Should().NotBe(0);
        conflictOut.Should().Contain("::error::");

        // 3. Missing PR ref / fetch error fails closed (exit non-zero)
        var (missingExit, _, _) = RunConflictScript(runnerClone, mergeTreeScript, "main", "999");
        missingExit.Should().NotBe(0);

        // 4. merge_group event validates HEAD against refs/heads/<base> cleanly and fails closed on conflict
        RunGit(runnerClone, "fetch", "origin", "+refs/pull/42/head:refs/remotes/origin/pr/42", "+refs/pull/43/head:refs/remotes/origin/pr/43");
        RunGit(runnerClone, "checkout", "--detach", "refs/remotes/origin/pr/42");
        var (mgCleanExit, mgCleanOut, mgCleanErr) = RunConflictScript(runnerClone, mergeTreeScript, "refs/heads/main", "", "merge_group");
        mgCleanExit.Should().Be(0, $"clean merge_group should pass: {mgCleanOut}\n{mgCleanErr}");

        RunGit(runnerClone, "checkout", "--detach", "refs/remotes/origin/pr/43");
        var (mgConflictExit, mgConflictOut, _) = RunConflictScript(runnerClone, mergeTreeScript, "refs/heads/main", "", "merge_group");
        mgConflictExit.Should().NotBe(0);
        mgConflictOut.Should().Contain("::error::");
    }

    [Fact]
    public void CiAndSanitizeWorkflows_EnforceLeastPrivilegeAndFailLoudlyOnLabels()
    {
        string ciContent = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        ciContent.Should().NotContain("gh label create", "classify must not recreate repository labels on pull requests");
        ciContent.Should().NotContain("Failed to apply label. Ignoring.", "classify must fail loudly on label errors");
        ciContent.Should().NotContain("check-action-pinning.py", "retired Python pinning script must not be invoked");
        ciContent.Should().NotContain("gh pr merge", "GITHUB_TOKEN automerge must remain retired so merges trigger push workflows");
        ciContent.Should().Contain("steps.classify.outputs.is_safe == 'true' && github.event.pull_request.head.repo.full_name == github.repository");

        File.Exists(Path.Combine(RepoRoot(), "scripts", "check-action-pinning.py")).Should().BeFalse();

        string sanitizeContent = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "sanitize-bot-comment.yml"));
        sanitizeContent.Should().Contain("github.event.comment.user.type == 'Bot'");
        sanitizeContent.Should().Contain("github.event.comment.user.login == 'github-actions[bot]'");
        sanitizeContent.Should().NotContain("google-labs-jules[bot]",
            "GITHUB_TOKEN cannot edit issue comments authored by external GitHub Apps");
        sanitizeContent.Should().NotContain("google-labs-stitch[bot]",
            "GITHUB_TOKEN cannot edit issue comments authored by external GitHub Apps");
        sanitizeContent.Should().NotContain("pull-requests: write");
        sanitizeContent.Should().Contain("issues: write");
    }

    private static string ExtractConflictCheckScript(string[] lines)
    {
        int start = Array.FindIndex(lines, l => l.Trim() == "set -euo pipefail");
        start.Should().BeGreaterThan(0);

        var scriptLines = lines[start..]
            .TakeWhile(l => l.Length == 0 || l.StartsWith("          ", StringComparison.Ordinal))
            .Select(l => l.Length >= 10 ? l[10..] : string.Empty);
        return string.Join('\n', scriptLines);
    }

    private static void RunGit(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)!;
        string stderr = proc.StandardError.ReadToEnd();
        proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        proc.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {stderr}");
    }

    private static (int ExitCode, string Stdout, string Stderr) RunConflictScript(
        string workingDir,
        string script,
        string baseRef,
        string prNumber,
        string eventName = "pull_request")
    {
        var psi = new ProcessStartInfo("bash")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["EVENT_NAME"] = eventName;
        psi.Environment["BASE_REF"] = baseRef;
        psi.Environment["PR_NUMBER"] = prNumber;
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);

        using var proc = Process.Start(psi)!;
        string stderr = proc.StandardError.ReadToEnd();
        string stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
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
