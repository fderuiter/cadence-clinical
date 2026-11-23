using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class RulesetServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ruleset-service-tests-{Guid.NewGuid():N}");

    public RulesetServiceTests()
    {
        Directory.CreateDirectory(_root);
        SeedValidFixture();
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Validate_OnRepositoryRoot_Passes()
    {
        string repoRoot = FindRepositoryRoot();
        using var writer = new StringWriter();

        int exitCode = RulesetService.Validate(repoRoot, writer);

        exitCode.Should().Be(0, writer.ToString());
        writer.ToString().Should().Contain("Validated 3 declarative rulesets");

        foreach (string branchFile in new[] { "main.json", "release_branches.json" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, ".github", "rulesets", branchFile)));
            var bypassActors = doc.RootElement.GetProperty("bypass_actors");
            bypassActors.GetArrayLength().Should().BeGreaterThan(0);
            foreach (var actor in bypassActors.EnumerateArray())
            {
                actor.GetProperty("actor_id").GetInt64().Should().Be(RulesetService.RepositoryAdminRoleId);
                actor.GetProperty("actor_type").GetString().Should().Be("RepositoryRole");
                actor.GetProperty("bypass_mode").GetString().Should().Be("pull_request");
            }
        }

        using var tagDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, ".github", "rulesets", "version_tags.json")));
        var tagBypassActors = tagDoc.RootElement.GetProperty("bypass_actors");
        tagBypassActors.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var actor in tagBypassActors.EnumerateArray())
        {
            actor.GetProperty("actor_id").GetInt64().Should().Be(RulesetService.RepositoryAdminRoleId);
            actor.GetProperty("actor_type").GetString().Should().Be("RepositoryRole");
            actor.GetProperty("bypass_mode").GetString().Should().Be("always");
        }
    }

    [Fact]
    public void Validate_OnValidFixture_Passes()
    {
        using var writer = new StringWriter();

        int exitCode = RulesetService.Validate(_root, writer);

        exitCode.Should().Be(0, writer.ToString());
        writer.ToString().Should().Contain("Validated 3 declarative rulesets");
    }

    [Fact]
    public void Validate_FailsWhenStrictStatusChecksPolicyIsDisabled()
    {
        WriteMainRuleset(strict: false, integrationId: RulesetService.GitHubActionsIntegrationId, firstCheck: "CI Gate");
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        writer.ToString().Should().Contain("strict_required_status_checks_policy");
    }

    [Fact]
    public void Validate_FailsWhenStatusCheckIntegrationIdIsMissingOrWrong()
    {
        WriteMainRuleset(strict: true, integrationId: 99999, firstCheck: "CI Gate");
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        writer.ToString().Should().Contain("integration_id");
    }

    [Fact]
    public void Validate_FailsWhenRulesetReferencesRemovedOrConditionalCheck()
    {
        WriteMainRuleset(strict: true, integrationId: RulesetService.GitHubActionsIntegrationId, firstCheck: "Frontend & Backend Unit Tests");
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        writer.ToString().Should().Contain("Frontend & Backend Unit Tests");
    }

    [Fact]
    public void Validate_FailsWhenPushOnlyOrAutomergeJobIsRequiredInRuleset()
    {
        Write(".github/rulesets/main.json", """
            {
              "name": "main-branch-protection",
              "target": "branch",
              "enforcement": "active",
              "conditions": { "ref_name": { "include": ["refs/heads/main"], "exclude": [] } },
              "rules": [
                { "type": "deletion" },
                { "type": "non_fast_forward" },
                { "type": "pull_request", "parameters": { "required_approving_review_count": 1, "require_code_owner_review": true } },
                {
                  "type": "required_status_checks",
                  "parameters": {
                    "strict_required_status_checks_policy": true,
                    "required_status_checks": [
                      { "context": "CI Gate", "integration_id": 15368 },
                      { "context": "Check for Merge Conflicts", "integration_id": 15368 },
                      { "context": "Enable Automerge", "integration_id": 15368 },
                      { "context": "Parallel E2E Accessibility Testing", "integration_id": 15368 }
                    ]
                  }
                }
              ]
            }
            """);
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        string output = writer.ToString();
        output.Should().Contain("Enable Automerge");
        output.Should().Contain("Parallel E2E Accessibility Testing");
    }

    [Fact]
    public void Validate_FailsWhenCiGateOmitsValidationJobOrLacksIfAlways()
    {
        WriteCiWorkflow(ciGateNeeds: "[unit-tests]", ciGateIf: "success()", checkComplianceResult: false);
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        string output = writer.ToString();
        output.Should().Contain("if: always()");
        output.Should().Contain("compliance");
    }

    [Fact]
    public void Validate_FailsWhenCiGateAllowsRequiredComplianceJobToSkip()
    {
        Write(".github/workflows/ci.yml", """
            name: CI
            on:
              push:
                branches: [main]
              pull_request:
                branches: [main]
            jobs:
              unit-tests:
                name: Frontend Unit Tests
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              compliance:
                name: GxP Compliance & Qualification Reporting
                needs: [unit-tests]
                if: always() && needs.unit-tests.result == 'success'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              ci-gate:
                name: CI Gate
                needs: [unit-tests, compliance]
                if: always()
                runs-on: ubuntu-latest
                steps:
                  - name: Verify validation job outcomes
                    env:
                      UNIT_RESULT: ${{ needs.unit-tests.result }}
                      COMPLIANCE_RESULT: ${{ needs.compliance.result }}
                    run: |
                      if [ "$UNIT_RESULT" != "success" ]; then exit 1; fi
                      if [ "$COMPLIANCE_RESULT" != "success" ] && [ "$COMPLIANCE_RESULT" != "skipped" ]; then exit 1; fi
              classify:
                name: Classify Change
                if: github.event_name == 'pull_request'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              automerge:
                name: Enable Automerge
                needs: [classify, ci-gate]
                if: always() && !cancelled() && github.event_name == 'pull_request' && needs.ci-gate.result == 'success'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
            """);
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        writer.ToString().Should().Contain("must fail when required job 'compliance' is skipped");
    }

    [Theory]
    [InlineData("[]", "bypass_actors")]
    [InlineData("""[{ "actor_id": 2, "actor_type": "RepositoryRole", "bypass_mode": "always" }]""", "actor_id 5")]
    [InlineData("""[{ "actor_id": 5, "actor_type": "Team", "bypass_mode": "always" }]""", "RepositoryRole")]
    [InlineData("""[{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "pull_request" }]""", "bypass_mode 'always'")]
    public void Validate_FailsWhenVersionTagsRulesetMisconfiguresReleaseAdminBypassActors(string bypassActorsJson, string expectedError)
    {
        Write(".github/rulesets/version_tags.json", $$"""
            {
              "name": "version-tags-protection",
              "target": "tag",
              "enforcement": "active",
              "bypass_actors": {{bypassActorsJson}},
              "conditions": { "ref_name": { "include": ["refs/tags/v*"], "exclude": [] } },
              "rules": [
                { "type": "creation" },
                { "type": "required_signatures" },
                { "type": "deletion" },
                { "type": "non_fast_forward" }
              ]
            }
            """);
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        writer.ToString().Should().Contain(expectedError);
    }

    [Theory]
    [InlineData("""[{ "type": "required_signatures" }, { "type": "deletion" }, { "type": "non_fast_forward" }]""", "creation")]
    [InlineData("""[{ "type": "creation" }, { "type": "deletion" }, { "type": "non_fast_forward" }]""", "required_signatures")]
    public void Validate_FailsWhenVersionTagsRulesetOmitsCreationOrRequiredSignatures(string rulesJson, string missingRule)
    {
        Write(".github/rulesets/version_tags.json", $$"""
            {
              "name": "version-tags-protection",
              "target": "tag",
              "enforcement": "active",
              "bypass_actors": [{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always" }],
              "conditions": { "ref_name": { "include": ["refs/tags/v*"], "exclude": [] } },
              "rules": {{rulesJson}}
            }
            """);
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        writer.ToString().Should().Contain(missingRule);
    }

    [Theory]
    [InlineData("fderuiter/cadence-clinical", "fderuiter/cadence-clinical")]
    [InlineData("https://github.com/fderuiter/cadence-clinical.git", "fderuiter/cadence-clinical")]
    [InlineData("git@github.com:fderuiter/cadence-clinical.git", "fderuiter/cadence-clinical")]
    public void TryParseRepository_ExtractsOwnerAndRepo(string raw, string expected)
    {
        RulesetService.TryParseRepository(raw, out string parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Fact]
    public void Sync_DryRun_ResolvesGitRemoteAndMakesZeroGhApiCalls()
    {
        var calls = new List<(string FileName, IReadOnlyList<string> Args)>();
        RulesetService.ProcessRunner runner = (_, file, args) =>
        {
            calls.Add((file, args));
            return file == "git"
                ? new RulesetService.ProcessOutcome(0, "git@github.com:owner/repo.git\n", "")
                : new RulesetService.ProcessOutcome(1, "", "unexpected gh call");
        };
        using var writer = new StringWriter();

        int exitCode = RulesetService.Sync(
            repoRoot: _root,
            dryRun: true,
            runner: runner,
            environment: _ => null,
            output: writer);

        exitCode.Should().Be(0);
        calls.Should().ContainSingle(c => c.FileName == "git");
        calls.Should().NotContain(c => c.FileName == "gh");
        writer.ToString().Should().Contain("Dry-run complete: 3 rulesets validated for owner/repo");
    }

    [Fact]
    public void Sync_LiveMode_UpdatesExistingAndCreatesMissingRulesets()
    {
        var ghCalls = new List<IReadOnlyList<string>>();
        RulesetService.ProcessRunner runner = (_, file, args) =>
        {
            if (file == "gh")
            {
                ghCalls.Add(args);
                if (args[0] == "api" && args[1] == "--paginate")
                {
                    return new RulesetService.ProcessOutcome(
                        0,
                        """[{"id": 42, "name": "main-branch-protection"}][{"id": 77, "name": "version-tags-protection"}]""",
                        "");
                }

                return new RulesetService.ProcessOutcome(0, "{}", "");
            }

            return new RulesetService.ProcessOutcome(1, "", "unexpected");
        };
        using var writer = new StringWriter();

        int exitCode = RulesetService.Sync(
            repoRoot: _root,
            dryRun: false,
            runner: runner,
            environment: key => key == "GITHUB_REPOSITORY" ? "owner/repo" : null,
            output: writer);

        exitCode.Should().Be(0, writer.ToString());
        ghCalls.Should().HaveCount(4);
        ghCalls[0].Should().Equal("api", "--paginate", "repos/owner/repo/rulesets?per_page=100");
        ghCalls[1].Should().ContainInOrder("api", "--method", "PUT", "repos/owner/repo/rulesets/42", "--input");
        ghCalls[2].Should().ContainInOrder("api", "--method", "POST", "repos/owner/repo/rulesets", "--input");
        ghCalls[3].Should().ContainInOrder("api", "--method", "PUT", "repos/owner/repo/rulesets/77", "--input");
    }

    [Fact]
    public void Sync_FailsWithNonZeroExitCode_WhenGhApiReturnsPermissionDenied()
    {
        int callIndex = 0;
        RulesetService.ProcessRunner runner = (_, _, _) =>
        {
            callIndex++;
            return callIndex == 1
                ? new RulesetService.ProcessOutcome(0, "[]", "")
                : new RulesetService.ProcessOutcome(1, "", "gh: Must have admin rights to Repository. (HTTP 403)");
        };
        using var writer = new StringWriter();

        int exitCode = RulesetService.Sync(
            repoRoot: _root,
            dryRun: false,
            repoOverride: "owner/repo",
            runner: runner,
            environment: _ => null,
            output: writer);

        exitCode.Should().Be(1, "permission errors must fail closed instead of being masked");
        writer.ToString().Should().Contain("HTTP 403");
    }

    [Fact]
    public void Validate_AndSync_BothFailClosedWhenRulesetsDirectoryIsAbsent()
    {
        Directory.Delete(Path.Combine(_root, ".github", "rulesets"), recursive: true);

        using var missingDirWriter = new StringWriter();
        RulesetService.Validate(_root, missingDirWriter)
            .Should().Be(1, "cadence check rulesets must fail closed when .github/rulesets is absent");
        missingDirWriter.ToString().Should().Contain("Directory '.github/rulesets' does not exist.");

        using var dryRunWriter = new StringWriter();
        RulesetService.Sync(repoRoot: _root, dryRun: true, repoOverride: "owner/repo", output: dryRunWriter)
            .Should().Be(0, dryRunWriter.ToString());
        dryRunWriter.ToString().Should().Contain("Dry-run validated CI Gate workflow coverage");

        using var syncWriter = new StringWriter();
        RulesetService.Sync(repoRoot: _root, dryRun: false, repoOverride: "owner/repo", output: syncWriter)
            .Should().Be(1, "non-dry-run cadence rulesets sync requires declarative ruleset files");
        syncWriter.ToString().Should().Contain("Directory '.github/rulesets' does not exist.");

        WriteCiWorkflow(ciGateNeeds: "[unit-tests]", ciGateIf: "always()", checkComplianceResult: false);
        using var brokenCiWriter = new StringWriter();
        RulesetService.Validate(_root, brokenCiWriter).Should().Be(1);
        brokenCiWriter.ToString().Should().Contain("Directory '.github/rulesets' does not exist.")
            .And.Contain("compliance", "CI Gate coverage must still run unconditionally when .github/rulesets is missing");

        using var brokenDryRunWriter = new StringWriter();
        RulesetService.Sync(repoRoot: _root, dryRun: true, repoOverride: "owner/repo", output: brokenDryRunWriter)
            .Should().Be(1);
        brokenDryRunWriter.ToString().Should().Contain("compliance");

        Directory.CreateDirectory(Path.Combine(_root, ".github", "rulesets"));
        using var emptyDirWriter = new StringWriter();
        RulesetService.Validate(_root, emptyDirWriter).Should().Be(1);
        emptyDirWriter.ToString().Should().Contain("No '*.json' ruleset files found in '.github/rulesets'.")
            .And.Contain("compliance", "CI Gate coverage must still run unconditionally when .github/rulesets is empty");
    }

    [Fact]
    public void Validate_AllowsPullRequestAdminBypassMode_AndRejectsInvalidBranchBypassActors()
    {
        WriteMainRulesetWithBypassActors("""[{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "pull_request" }]""");
        using (var okWriter = new StringWriter())
        {
            RulesetService.Validate(_root, okWriter).Should().Be(0, okWriter.ToString());
        }

        foreach (string invalidModeActors in new[]
        {
            """[{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always" }]""",
            """[{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "exempt" }]""",
            """[{ "actor_id": 5, "actor_type": "RepositoryRole" }]""",
        })
        {
            WriteMainRulesetWithBypassActors(invalidModeActors);
            using var errWriter = new StringWriter();

            RulesetService.Validate(_root, errWriter).Should().Be(1, $"branch bypass_actors '{invalidModeActors}' must fail closed");
            errWriter.ToString().Should().Contain("bypass_mode").And.Contain("pull_request");
        }

        foreach (string nonAdminActors in new[]
        {
            """[{ "actor_id": 2, "actor_type": "RepositoryRole", "bypass_mode": "pull_request" }]""",
            """[{ "actor_id": 5, "actor_type": "Team", "bypass_mode": "pull_request" }]""",
        })
        {
            WriteMainRulesetWithBypassActors(nonAdminActors);
            using var errWriter = new StringWriter();

            RulesetService.Validate(_root, errWriter).Should().Be(1, $"non-admin branch bypass_actors '{nonAdminActors}' must fail closed");
            errWriter.ToString().Should().Contain("repository admins");
        }
    }

    [Fact]
    public void Validate_FailsWhenNonVersionTagRulesetConfiguresBypassActors()
    {
        SeedValidFixture();
        Write(".github/rulesets/release_tags.json", """
            {
              "name": "release-tags-protection",
              "target": "tag",
              "enforcement": "active",
              "bypass_actors": [{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always" }],
              "conditions": { "ref_name": { "include": ["refs/tags/rc-*"], "exclude": [] } },
              "rules": [{ "type": "deletion" }, { "type": "non_fast_forward" }]
            }
            """);
        using var extraTagWriter = new StringWriter();
        RulesetService.Validate(_root, extraTagWriter).Should().Be(1);
        extraTagWriter.ToString().Should().Contain("release_tags.json").And.Contain("bypass_actors");
    }

    [Fact]
    public void Validate_InPreCommitMode_ReadsStagedIndexBlobs_AndRejectsStagedDeletionsBypassViolationsAndCiGateDrift()
    {
        WriteMainRulesetWithBypassActors("""[{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "pull_request" }]""");
        InitGitRepositoryAndCommitAll();

        // 1. Stage single-file deletion (`git rm --cached`) while leaving valid working-tree file on disk.
        RunGit("rm", "--cached", "-q", ".github/rulesets/main.json");
        File.Exists(Path.Combine(_root, ".github", "rulesets", "main.json")).Should().BeTrue();
        using (var reader = GitIndexReader.TryOpen(_root))
        using (var writer = new StringWriter())
        {
            reader.Should().NotBeNull();
            RulesetService.Validate(_root, writer, preCommit: true, indexReader: reader).Should().Be(1);
            writer.ToString().Should().Contain("Required ruleset file '.github/rulesets/main.json' is missing.");
        }

        QualityGateOrchestrator.RunQualityGates(gateName: "rulesets", preCommit: true, repoRoot: _root)
            .Should().Be(1, "pnpm cadence check --pre-commit must fail closed on staged ruleset deletions");

        // 2. Stage deletion of all ruleset files while leaving valid working-tree copies on disk.
        RunGit("rm", "--cached", "-q", ".github/rulesets/release_branches.json", ".github/rulesets/version_tags.json");
        using (var reader = GitIndexReader.TryOpen(_root))
        using (var writer = new StringWriter())
        {
            RulesetService.Validate(_root, writer, preCommit: true, indexReader: reader).Should().Be(1);
            writer.ToString().Should().Contain("Directory '.github/rulesets' does not exist.");
        }

        // Restore staged rulesets and verify pre-commit passes.
        RunGit("checkout", "HEAD", "--", ".github/rulesets");
        QualityGateOrchestrator.RunQualityGates(gateName: "rulesets", preCommit: true, repoRoot: _root)
            .Should().Be(0);

        // 3. Stage bypass_mode: "always" in the index, then overwrite working-tree file with valid "pull_request".
        WriteMainRulesetWithBypassActors("""[{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always" }]""");
        RunGit("add", ".github/rulesets/main.json");
        WriteMainRulesetWithBypassActors("""[{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "pull_request" }]""");

        using (var reader = GitIndexReader.TryOpen(_root))
        using (var writer = new StringWriter())
        {
            RulesetService.Validate(_root, writer, preCommit: true, indexReader: reader).Should().Be(1);
            writer.ToString().Should().Contain("bypass_mode").And.Contain("always");
        }

        // 4. Stage valid ruleset, then stage broken CI Gate workflow while keeping working-tree ci.yml valid.
        RunGit("add", ".github/rulesets/main.json");
        WriteCiWorkflow(ciGateNeeds: "[unit-tests]", ciGateIf: "always()", checkComplianceResult: false);
        RunGit("add", ".github/workflows/ci.yml");
        WriteCiWorkflow(ciGateNeeds: "[unit-tests, container-security, compliance]", ciGateIf: "always()", checkComplianceResult: true);

        using (var reader = GitIndexReader.TryOpen(_root))
        using (var writer = new StringWriter())
        {
            RulesetService.Validate(_root, writer, preCommit: true, indexReader: reader).Should().Be(1);
            writer.ToString().Should().Contain("compliance");
        }

        RunGit("add", ".github/workflows/ci.yml");
        QualityGateOrchestrator.RunQualityGates(gateName: "rulesets", preCommit: true, repoRoot: _root)
            .Should().Be(0);
    }

    [Fact]
    public void Validate_RejectsAlwaysBypassOnBranchRulesets_AndAcceptsPullRequestBypass()
    {
        WriteMainRuleset(
            strict: true,
            integrationId: RulesetService.GitHubActionsIntegrationId,
            firstCheck: "CI Gate",
            bypassMode: "always");
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        writer.ToString().Should().Contain("restrict 'bypass_mode' to 'pull_request'");
    }

    [Fact]
    public void Validate_RequiresMergeQueueOnMain_AndMergeGroupWorkflowParity()
    {
        WriteMainRuleset(
            strict: true,
            integrationId: RulesetService.GitHubActionsIntegrationId,
            firstCheck: "CI Gate",
            includeMergeQueue: false);
        using var missingQueueWriter = new StringWriter();

        RulesetService.Validate(_root, missingQueueWriter).Should().Be(1);
        missingQueueWriter.ToString().Should().Contain("merge_queue");

        WriteMainRuleset(strict: true, integrationId: RulesetService.GitHubActionsIntegrationId, firstCheck: "CI Gate");
        Write(".github/workflows/conflict-check.yml", """
            name: Conflict Check
            on:
              pull_request:
                types: [opened, synchronize, reopened]
            jobs:
              conflict-check:
                name: Check for Merge Conflicts
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
            """);
        using var missingMergeGroupWriter = new StringWriter();

        RulesetService.Validate(_root, missingMergeGroupWriter).Should().Be(1);
        missingMergeGroupWriter.ToString().Should().Contain("merge_group");
    }

    [Fact]
    public void Sync_ProfilesControlStatusChecksAndMergeQueueProjection_AndRejectUnknownProfile()
    {
        var pausedPayloads = new List<string>();
        var activePayloads = new List<string>();

        RulesetService.ProcessRunner MakeRunner(List<string> payloads) => (_, file, args) =>
        {
            if (file == "gh")
            {
                if (args[0] == "api" && args[1] == "--paginate")
                {
                    return new RulesetService.ProcessOutcome(0, "[]", "");
                }

                int inputIdx = -1;
                for (int i = 0; i < args.Count - 1; i++)
                {
                    if (args[i] == "--input")
                    {
                        inputIdx = i + 1;
                        break;
                    }
                }

                inputIdx.Should().BeGreaterThan(0);
                payloads.Add(File.ReadAllText(args[inputIdx]));
                return new RulesetService.ProcessOutcome(0, "{}", "");
            }

            return new RulesetService.ProcessOutcome(1, "", "unexpected");
        };

        using var pausedWriter = new StringWriter();
        RulesetService.Sync(
            repoRoot: _root,
            dryRun: false,
            repoOverride: "owner/repo",
            profile: RulesetService.PausedActionsProfile,
            runner: MakeRunner(pausedPayloads),
            environment: _ => null,
            output: pausedWriter).Should().Be(0, pausedWriter.ToString());

        pausedPayloads.Should().HaveCount(3);
        using (var mainPausedDoc = JsonDocument.Parse(pausedPayloads[0]))
        {
            var ruleTypes = new List<string>();
            foreach (var rule in mainPausedDoc.RootElement.GetProperty("rules").EnumerateArray())
            {
                ruleTypes.Add(rule.GetProperty("type").GetString()!);
            }

            ruleTypes.Should().Equal("deletion", "non_fast_forward", "pull_request");
            mainPausedDoc.RootElement.GetProperty("bypass_actors")[0].GetProperty("bypass_mode").GetString()
                .Should().Be("pull_request");
        }

        using var activeWriter = new StringWriter();
        RulesetService.Sync(
            repoRoot: _root,
            dryRun: false,
            repoOverride: "owner/repo",
            profile: RulesetService.ActiveActionsProfile,
            runner: MakeRunner(activePayloads),
            environment: _ => null,
            output: activeWriter).Should().Be(0, activeWriter.ToString());

        activePayloads.Should().HaveCount(3);
        using (var mainActiveDoc = JsonDocument.Parse(activePayloads[0]))
        {
            var ruleTypes = new List<string>();
            foreach (var rule in mainActiveDoc.RootElement.GetProperty("rules").EnumerateArray())
            {
                ruleTypes.Add(rule.GetProperty("type").GetString()!);
            }

            ruleTypes.Should().Equal("deletion", "non_fast_forward", "pull_request", "required_status_checks", "merge_queue");
        }

        using var badProfileWriter = new StringWriter();
        RulesetService.Sync(
            repoRoot: _root,
            dryRun: true,
            repoOverride: "owner/repo",
            profile: "invalid-profile",
            output: badProfileWriter).Should().Be(1);
        badProfileWriter.ToString().Should().Contain("Unsupported ruleset profile");
    }

    private void SeedValidFixture()
    {
        WriteMainRuleset(strict: true, integrationId: RulesetService.GitHubActionsIntegrationId, firstCheck: "CI Gate");
        Write(".github/rulesets/release_branches.json", """
            {
              "name": "release-branches-protection",
              "target": "branch",
              "enforcement": "active",
              "bypass_actors": [
                {
                  "actor_id": 5,
                  "actor_type": "RepositoryRole",
                  "bypass_mode": "pull_request"
                }
              ],
              "conditions": { "ref_name": { "include": ["refs/heads/release/*"], "exclude": [] } },
              "rules": [
                { "type": "deletion" },
                { "type": "non_fast_forward" },
                { "type": "pull_request", "parameters": { "required_approving_review_count": 1, "require_code_owner_review": true } },
                {
                  "type": "required_status_checks",
                  "parameters": {
                    "strict_required_status_checks_policy": true,
                    "required_status_checks": [
                      { "context": "CI Gate", "integration_id": 15368 },
                      { "context": "Check for Merge Conflicts", "integration_id": 15368 }
                    ]
                  }
                }
              ]
            }
            """);
        Write(".github/rulesets/version_tags.json", """
            {
              "name": "version-tags-protection",
              "target": "tag",
              "enforcement": "active",
              "bypass_actors": [
                {
                  "actor_id": 5,
                  "actor_type": "RepositoryRole",
                  "bypass_mode": "always"
                }
              ],
              "conditions": { "ref_name": { "include": ["refs/tags/v*"], "exclude": [] } },
              "rules": [
                { "type": "creation" },
                { "type": "required_signatures" },
                { "type": "deletion" },
                { "type": "non_fast_forward" }
              ]
            }
            """);
        WriteCiWorkflow(ciGateNeeds: "[unit-tests, container-security, compliance]", ciGateIf: "always()", checkComplianceResult: true);
        Write(".github/workflows/conflict-check.yml", """
            name: Conflict Check
            on:
              pull_request:
                types: [opened, synchronize, reopened]
              merge_group:
                branches: [main, release/*]
            jobs:
              conflict-check:
                name: Check for Merge Conflicts
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
            """);
    }

    private void WriteMainRuleset(
        bool strict,
        long integrationId,
        string firstCheck,
        string bypassMode = "pull_request",
        bool includeMergeQueue = true)
    {
        string strictJson = strict ? "true" : "false";
        string mergeQueueRule = includeMergeQueue
            ? """
                ,
                {
                  "type": "merge_queue",
                  "parameters": {
                    "check_response_timeout_minutes": 60,
                    "grouping_strategy": "ALLGREEN",
                    "max_entries_to_build": 5,
                    "max_entries_to_merge": 5,
                    "merge_method": "SQUASH",
                    "min_entries_to_merge": 1,
                    "min_entries_to_merge_wait_minutes": 5
                  }
                }
              """
            : "";
        Write(".github/rulesets/main.json", $$"""
            {
              "name": "main-branch-protection",
              "target": "branch",
              "enforcement": "active",
              "bypass_actors": [
                {
                  "actor_id": 5,
                  "actor_type": "RepositoryRole",
                  "bypass_mode": "{{bypassMode}}"
                }
              ],
              "conditions": { "ref_name": { "include": ["refs/heads/main"], "exclude": [] } },
              "rules": [
                { "type": "deletion" },
                { "type": "non_fast_forward" },
                { "type": "pull_request", "parameters": { "required_approving_review_count": 1, "require_code_owner_review": true } },
                {
                  "type": "required_status_checks",
                  "parameters": {
                    "strict_required_status_checks_policy": {{strictJson}},
                    "required_status_checks": [
                      { "context": "{{firstCheck}}", "integration_id": {{integrationId}} },
                      { "context": "Check for Merge Conflicts", "integration_id": {{RulesetService.GitHubActionsIntegrationId}} }
                    ]
                  }
                }{{mergeQueueRule}}
              ]
            }
            """);
    }

    private void WriteCiWorkflow(string ciGateNeeds, string ciGateIf, bool checkComplianceResult)
    {
        string complianceCheck = checkComplianceResult
            ? "if [ \"$COMPLIANCE_RESULT\" != \"success\" ]; then exit 1; fi"
            : "echo skipped-check";
        Write(".github/workflows/ci.yml", $$$"""
            name: CI
            on:
              push:
                branches: [main, release/*]
              pull_request:
                branches: [main, release/*]
              merge_group:
                branches: [main, release/*]
            jobs:
              unit-tests:
                name: Frontend Unit Tests
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              container-security:
                name: Container Build & Vulnerability Scan
                needs: [classify]
                if: always() && !cancelled() && (github.event_name == 'push' || needs.classify.outputs.container_scan == 'true')
                runs-on: ubuntu-latest
                strategy:
                  matrix:
                    target: [gateway]
                steps:
                  - run: echo ok
              accessibility-e2e:
                name: Parallel E2E Accessibility Testing
                if: github.event_name == 'push'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              compliance:
                name: GxP Compliance & Qualification Reporting
                needs: [unit-tests]
                if: always() && needs.unit-tests.result == 'success'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              ci-gate:
                name: CI Gate
                needs: {{{ciGateNeeds}}}
                if: {{{ciGateIf}}}
                runs-on: ubuntu-latest
                steps:
                  - name: Verify validation job outcomes
                    env:
                      EVENT_NAME: ${{ github.event_name }}
                      UNIT_RESULT: ${{ needs.unit-tests.result }}
                      CONTAINER_RESULT: ${{ needs.container-security.result }}
                      COMPLIANCE_RESULT: ${{ needs.compliance.result }}
                    run: |
                      if [ "$UNIT_RESULT" != "success" ]; then exit 1; fi
                      if [ "$EVENT_NAME" = "pull_request" ]; then
                        if [ "$CONTAINER_RESULT" != "success" ] && [ "$CONTAINER_RESULT" != "skipped" ]; then exit 1; fi
                      elif [ "$CONTAINER_RESULT" != "success" ]; then
                        exit 1
                      fi
                      {{{complianceCheck}}}
              classify:
                name: Classify Change
                if: github.event_name == 'pull_request'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              automerge:
                name: Enable Automerge
                needs: [classify, ci-gate]
                if: always() && !cancelled() && github.event_name == 'pull_request' && needs.ci-gate.result == 'success'
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
              release-and-archive:
                name: Call Production Pipeline
                needs: [compliance]
                if: github.event_name == 'push' && (github.ref == 'refs/heads/main' || startsWith(github.ref, 'refs/heads/release/') || startsWith(github.ref, 'refs/tags/v'))
                uses: ./.github/workflows/production-pipeline.yml
              deploy-docs:
                name: Call Deploy Docs
                needs: [compliance]
                if: github.event_name == 'push' && github.ref == 'refs/heads/main'
                uses: ./.github/workflows/deploy-docs.yml
            """);
    }

    private void WriteMainRulesetWithBypassActors(string bypassActorsJson)
    {
        Write(".github/rulesets/main.json", $$"""
            {
              "name": "main-branch-protection",
              "target": "branch",
              "enforcement": "active",
              "bypass_actors": {{bypassActorsJson}},
              "conditions": { "ref_name": { "include": ["refs/heads/main"], "exclude": [] } },
              "rules": [
                { "type": "deletion" },
                { "type": "non_fast_forward" },
                { "type": "pull_request", "parameters": { "required_approving_review_count": 1, "require_code_owner_review": true } },
                {
                  "type": "required_status_checks",
                  "parameters": {
                    "strict_required_status_checks_policy": true,
                    "required_status_checks": [
                      { "context": "CI Gate", "integration_id": {{RulesetService.GitHubActionsIntegrationId}} },
                      { "context": "Check for Merge Conflicts", "integration_id": {{RulesetService.GitHubActionsIntegrationId}} }
                    ]
                  }
                },
                {
                  "type": "merge_queue",
                  "parameters": {
                    "check_response_timeout_minutes": 60,
                    "grouping_strategy": "ALLGREEN",
                    "max_entries_to_build": 5,
                    "max_entries_to_merge": 5,
                    "merge_method": "SQUASH",
                    "min_entries_to_merge": 1,
                    "min_entries_to_merge_wait_minutes": 5
                  }
                }
              ]
            }
            """);
    }

    private void InitGitRepositoryAndCommitAll()
    {
        RunGit("init", "-q", "-b", "main");
        RunGit("config", "user.email", "tests@cadence.example");
        RunGit("config", "user.name", "Cadence Tests");
        RunGit("config", "commit.gpgsign", "false");
        RunGit("add", ".");
        RunGit("commit", "-q", "-m", "seed rulesets and workflows");
    }

    private void RunGit(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        string stderr = process.StandardError.ReadToEnd();
        _ = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {stderr}");
    }

    private void Write(string relativePath, string content)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static string FindRepositoryRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Cadence.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName
                ?? throw new InvalidOperationException("Cadence.sln not found above test directory.");
        }

        return dir;
    }
}
