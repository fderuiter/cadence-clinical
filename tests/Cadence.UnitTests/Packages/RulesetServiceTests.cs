using System;
using System.Collections.Generic;
using System.IO;

using Cadence.Cli.Services;

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
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Validate_OnRepositoryRoot_Passes()
    {
        using var writer = new StringWriter();

        int exitCode = RulesetService.Validate(FindRepositoryRoot(), writer);

        exitCode.Should().Be(0, writer.ToString());
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

    [Fact]
    public void Validate_FailsWhenVersionTagsRulesetAllowsBypassActors()
    {
        Write(".github/rulesets/version_tags.json", """
            {
              "name": "version-tags-protection",
              "target": "tag",
              "enforcement": "active",
              "bypass_actors": [{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always" }],
              "conditions": { "ref_name": { "include": ["refs/tags/v*"], "exclude": [] } },
              "rules": [{ "type": "deletion" }, { "type": "non_fast_forward" }]
            }
            """);
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        writer.ToString().Should().Contain("bypass_actors");
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
    public void Validate_PassesWithoutRulesetsDirectory_WhenCiGateCoverageIsValid()
    {
        Directory.Delete(Path.Combine(_root, ".github", "rulesets"), recursive: true);
        using var writer = new StringWriter();

        int exitCode = RulesetService.Validate(_root, writer);

        exitCode.Should().Be(0, writer.ToString());
        writer.ToString().Should().Contain("Validated CI Gate workflow coverage");
    }

    [Fact]
    public void Validate_FailsWithoutRulesetsDirectory_WhenCiGateCoverageIsBroken()
    {
        Directory.Delete(Path.Combine(_root, ".github", "rulesets"), recursive: true);
        WriteCiWorkflow(ciGateNeeds: "[unit-tests]", ciGateIf: "success()", checkComplianceResult: false);
        using var writer = new StringWriter();

        RulesetService.Validate(_root, writer).Should().Be(1);
        string output = writer.ToString();
        output.Should().Contain("if: always()");
        output.Should().Contain("compliance");
    }

    [Fact]
    public void Sync_FailsClosed_WhenRulesetsDirectoryIsMissing()
    {
        Directory.Delete(Path.Combine(_root, ".github", "rulesets"), recursive: true);
        using var writer = new StringWriter();

        int exitCode = RulesetService.Sync(
            repoRoot: _root,
            dryRun: true,
            repoOverride: "owner/repo",
            runner: (_, _, _) => new RulesetService.ProcessOutcome(1, "", "unexpected"),
            environment: _ => null,
            output: writer);

        exitCode.Should().Be(1);
        writer.ToString().Should().Contain("Directory '.github/rulesets' does not exist.");
    }

    private void SeedValidFixture()
    {
        WriteMainRuleset(strict: true, integrationId: RulesetService.GitHubActionsIntegrationId, firstCheck: "CI Gate");
        Write(".github/rulesets/release_branches.json", """
            {
              "name": "release-branches-protection",
              "target": "branch",
              "enforcement": "active",
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
              "conditions": { "ref_name": { "include": ["refs/tags/v*"], "exclude": [] } },
              "rules": [{ "type": "deletion" }, { "type": "non_fast_forward" }]
            }
            """);
        WriteCiWorkflow(ciGateNeeds: "[unit-tests, container-security, compliance]", ciGateIf: "always()", checkComplianceResult: true);
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
    }

    private void WriteMainRuleset(bool strict, long integrationId, string firstCheck)
    {
        string strictJson = strict ? "true" : "false";
        Write(".github/rulesets/main.json", $$"""
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
                    "strict_required_status_checks_policy": {{strictJson}},
                    "required_status_checks": [
                      { "context": "{{firstCheck}}", "integration_id": {{integrationId}} },
                      { "context": "Check for Merge Conflicts", "integration_id": {{RulesetService.GitHubActionsIntegrationId}} }
                    ]
                  }
                }
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
